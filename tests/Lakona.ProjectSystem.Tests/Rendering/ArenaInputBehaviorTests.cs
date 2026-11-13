using System.Runtime.Loader;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Server;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class ArenaInputBehaviorTests
{
    [Fact]
    public void Generated_input_rejects_nonfinite_components_without_changing_state_and_accepts_later_input()
    {
        using var fixture = new InputFixture();
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var input in new[] { (invalid, 0f), (0f, invalid), (invalid, invalid) })
        {
            var before = fixture.Apply(0.25f, -0.5f);
            Assert.Equal(before, fixture.Apply(input.Item1, input.Item2));
            Assert.Equal(new[] { 0f, 1f, 0f, 1f }, fixture.Apply(0f, 1f));
        }
    }

    [Fact]
    public void Generated_input_preserves_short_vectors_stops_and_normalizes_large_finite_vectors()
    {
        using var fixture = new InputFixture();
        Assert.Equal(new[] { 0.25f, -0.5f, 0.25f, -0.5f }, fixture.Apply(0.25f, -0.5f));
        Assert.Equal(new[] { 0f, 0f, 0.25f, -0.5f }, fixture.Apply(0f, 0f));
        Assert.Equal(new[] { 0.0001f, 0f, 0.25f, -0.5f }, fixture.Apply(0.0001f, 0f));
        foreach (var (x, y, expectedX, expectedY) in new[]
        {
            (3f, 4f, 0.6f, 0.8f),
            (float.MaxValue, 0f, 1f, 0f),
            (0f, -float.MaxValue, 0f, -1f),
            (float.MaxValue, -float.MaxValue, MathF.Sqrt(0.5f), -MathF.Sqrt(0.5f))
        })
        {
            var state = fixture.Apply(x, y);
            Assert.All(state, value => Assert.True(float.IsFinite(value)));
            Assert.InRange(MathF.Abs(state[0] - expectedX), 0, 0.000001f);
            Assert.InRange(MathF.Abs(state[1] - expectedY), 0, 0.000001f);
            Assert.Equal(state[..2], state[2..]);
        }
    }

    [Fact]
    public void Generated_input_keeps_unknown_offline_and_dead_players_unchanged()
    {
        using var fixture = new InputFixture();
        var before = fixture.Apply(0.25f, -0.5f);
        Assert.Equal(before, fixture.Apply(1, 0, false, true, true));
        Assert.Equal(before, fixture.Apply(1, 0, true, false, true));
        Assert.Equal(before, fixture.Apply(1, 0, true, true, false));
    }

    private sealed class InputFixture : IDisposable
    {
        private readonly AssemblyLoadContext context = new("ArenaInputProbe", isCollectible: true);
        private readonly Func<float, float, bool, bool, bool, float[]> apply;

        public InputFixture()
        {
            var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
                "InputTest", ".", ClientEngine.Godot, TransportKind.Kcp, SerializerKind.MemoryPack,
                NuGetForUnitySource.OpenUpm, DeploymentProfile.None));
            var builder = new GenerationPlanBuilder("Root");
            new HotfixRenderer().AddFiles(spec, builder);
            var source = Assert.Single(builder.Build().Files, file => file.RelativePath == "Server/Hotfix/Game/GameWorldBehavior.cs").Content;
            var method = Assert.Single(CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>(), method => method.Identifier.Text == "SubmitInputAsync");
            // Execute the emitted method unchanged. These stand-ins only hold state;
            // the full generated application is separately exercised through the E2E client.
            var probe = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                public sealed class Player { public bool IsOnline = true, IsAlive = true; public float InputX, InputY, DirectionX, DirectionY; }
                public sealed class GameWorldActor { public Dictionary<string, Player> PlayersByConnection = new(); }
                public sealed class GameInputRequest { public string ConnectionId = "player"; public float DirectionX, DirectionY; }
                public sealed class Probe {
                    readonly GameWorldActor world = new();
                    readonly Player player = new();
                    public Probe() { world.PlayersByConnection.Add("player", player); }
                    public float[] Apply(float x, float y, bool online, bool alive, bool known) {
                        player.IsOnline = online; player.IsAlive = alive;
                        SubmitInputAsync(world, new GameInputRequest { ConnectionId = known ? "player" : "unknown", DirectionX = x, DirectionY = y }).GetAwaiter().GetResult();
                        return new[] { player.InputX, player.InputY, player.DirectionX, player.DirectionY };
                    }
                """ + method.ToFullString() + "}";
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("ArenaInputProbe" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(probe)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            stream.Position = 0;
            var type = context.LoadFromStream(stream).GetType("Probe")!;
            apply = type.GetMethod("Apply")!.CreateDelegate<Func<float, float, bool, bool, bool, float[]>>(Activator.CreateInstance(type));
        }

        public float[] Apply(float x, float y, bool online = true, bool alive = true, bool known = true)
            => apply(x, y, online, alive, known);

        public void Dispose() => context.Unload();
    }
}
