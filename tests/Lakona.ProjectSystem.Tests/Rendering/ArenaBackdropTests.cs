using System.Runtime.Loader;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Rendering.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class ArenaBackdropTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedBackdrop_UsesTheSameWorldTransformAsEntities(bool godot)
    {
        var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame", ".", godot ? ClientEngine.Godot : ClientEngine.Unity,
            TransportKind.Kcp, SerializerKind.MemoryPack, NuGetForUnitySource.OpenUpm, DeploymentProfile.None, ProjectSpecTestOptionPresence.NuGetForUnitySource));
        var source = godot ? GodotClientCodeTemplates.RenderGameScene(spec) : UnityClientCodeTemplates.RenderGameController(spec);
        // Execute the emitted drawing and camera methods unchanged; the primitive stubs
        // below only record draw coordinates and do not implement the projection.
        var methods = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => new[] { "DrawArenaBackdrop", "CameraCenter", "WorldToScreen" }.Contains(method.Identifier.Text));
        var call = godot ? "DrawArenaBackdrop(arena);" : "DrawArenaBackdrop(new Painter2D(), arena);";
        var project = godot ? "WorldToScreen(arena, 10f, 10f)" : "WorldToScreen(arena, 10f, 10f, _world)";
        var fixture = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public class Probe {
                WorldSnapshot _world = new WorldSnapshot();
                long _localPlayerId = 1;
                static List<Vector2> vertical = new List<Vector2>();
                static List<Vector2> horizontal = new List<Vector2>();
                static List<Vector2> rings = new List<Vector2>();
                static void DrawRect(Painter2D p, Rect r, Color c) { if(r.width == 1) vertical.Add(r.position); else if(r.height == 1) horizontal.Add(r.position); }
                static void DrawRect(Rect2 r, Color c) {}
                static void DrawLine(Vector2 a, Vector2 b, Color c, float width = 1) { if(width == 1) { if(a.X == b.X) vertical.Add(a); else horizontal.Add(a); } }
                static void DrawLine(Painter2D p, Vector2 a, Vector2 b, Color c, float width) {}
                static void DrawRing(Painter2D p, Vector2 c, float r, Color color, float width) { rings.Add(c); }
                static void DrawArc(Vector2 c, float r, float from, float to, int count, Color color, float width) { rings.Add(c); }
            """ + string.Join("\n", methods.Select(method => method.ToFullString())) + $$"""
                public float[] Capture(float x, float y, float width, float height, bool login) {
                    _world = login ? null : new WorldSnapshot();
                    _localPlayerId = login ? 0 : 1;
                    if(!login) { _world.Players[0].X = x; _world.Players[0].Y = y; }
                    var arena = new {{(godot ? "Rect2" : "Rect")}}(0, 0, width, height);
                    vertical.Clear(); horizontal.Clear(); rings.Clear();
                    {{call}}
                    var entity = login ? new Vector2(0, 0) : {{project}};
                    return new[] { vertical[0].X, horizontal[0].Y, vertical[1].X - vertical[0].X,
                        rings[0].X, rings[0].Y, entity.X, entity.Y };
                }
            }
            """ + """
            public class WorldSnapshot { public float Width = 40, Height = 30; public List<Player> Players = new List<Player> { new Player() }; }
            public class Player { public long PlayerId = 1; public float X, Y; }
            public class Painter2D {}
            public struct Color { public Color(string s) {} public Color(float r, float g, float b, float a = 1) {} }
            public struct Vector2 {
                public float X,Y; public float x => X; public float y => Y;
                public Vector2(float x,float y) {X=x;Y=y;}
                public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X+b.X,a.Y+b.Y);
                public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X-b.X,a.Y-b.Y);
            }
            public struct Rect {
                public float x,y,width,height;
                public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
                public Vector2 center => new Vector2(x+width/2,y+height/2);
                public Vector2 position => new Vector2(x,y);
                public float xMin => x; public float yMin => y; public float xMax => x+width; public float yMax => y+height;
            }
            public struct Rect2 {
                public Vector2 Position,Size;
                public Rect2(float x,float y,float w,float h){Position=new Vector2(x,y);Size=new Vector2(w,h);}
                public Vector2 GetCenter()=>new Vector2(Position.X+Size.X/2,Position.Y+Size.Y/2);
                public Vector2 End => Position+Size;
            }
            public static class Mathf {
                public static float Min(float a,float b)=>MathF.Min(a,b);
                public static float Max(float a,float b)=>MathF.Max(a,b);
                public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("BackdropProbe" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(fixture, cancellationToken: TestContext.Current.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("BackdropProbe", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(stream).GetType("Probe")!;
            var instance = Activator.CreateInstance(type);
            float[] Capture(float x, float y, float w = 800, float h = 600, bool login = false) =>
                (float[])type.GetMethod("Capture")!.Invoke(instance, [x, y, w, h, login])!;
            foreach (var size in new[] { (800f, 600f), (1200f, 800f), (1600f, 900f) })
            {
                var before = Capture(20, 15, size.Item1, size.Item2);
                Assert.InRange(MathF.Abs(before[2] - size.Item2 / 12f), 0, 0.01f);
                foreach (var step in new[] { 0.25f, -0.25f, 1.25f, -1.25f })
                {
                    var after = Capture(20 + step, 15 + step, size.Item1, size.Item2);
                    var dx = after[5] - before[5];
                    var dy = after[6] - before[6];
                    Assert.NotEqual(0, dx);
                    Assert.NotEqual(0, dy);
                    // A periodic grid may wrap by one cell, but its world phase must match entities.
                    float Phase(float n) => (n % before[2] + before[2]) % before[2];
                    Assert.InRange(MathF.Abs(Phase(after[0] - before[0]) - Phase(dx)), 0, 0.01f);
                    Assert.InRange(MathF.Abs(Phase(after[1] - before[1]) - Phase(dy)), 0, 0.01f);
                    Assert.InRange(MathF.Abs((after[3] - before[3]) - dx), 0, 0.01f);
                    Assert.InRange(MathF.Abs((after[4] - before[4]) - dy), 0, 0.01f);
                }
            }
            Assert.Equal(Capture(39, 29), Capture(40, 30));
            Assert.Equal(Capture(0, 0), Capture(1, 1)); // Both positions clamp to the same camera at the edge.
            Assert.Equal(Capture(0, 0, login: true), Capture(20, 15, login: true));
        }
        finally { context.Unload(); }
    }
}
