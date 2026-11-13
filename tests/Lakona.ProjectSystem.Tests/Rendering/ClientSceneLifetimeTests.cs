using System.Runtime.Loader;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Rendering.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class ClientSceneLifetimeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Exit_owns_pending_client_and_ignores_late_connect_or_login(bool godot, bool connecting, bool failure)
    {
        using var scene = new SceneFixture(godot);
        scene.HoldConnect = connecting;
        var login = scene.Begin();
        Assert.True(scene.HasClient);
        scene.Exit();
        Assert.False(scene.HasClient);
        Assert.Equal(1, scene.Disposals);
        var mutations = scene.UiWrites;
        scene.Complete(connecting, failure);
        await login.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(mutations, scene.UiWrites);
        Assert.Equal(0, scene.PlayerId);
        Assert.False(scene.HasClient);
        Assert.Equal(connecting ? 0 : 1, scene.LoginCalls);
        Assert.Equal(1, scene.Disposals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Old_login_completion_cannot_change_or_release_replacement(bool godot, bool failure)
    {
        using var scene = new SceneFixture(godot);
        var old = scene.Begin();
        await scene.Reset();
        scene.ReplaceClient();
        var current = scene.Begin();
        var writes = scene.UiWrites;
        scene.Complete(false, failure, previous: true);
        await old.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(scene.HasClient);
        Assert.True(scene.LoginPending);
        Assert.Equal(0, scene.Disposals);
        Assert.Equal(writes, scene.UiWrites);
        scene.Complete(false, false);
        await current.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(7, scene.PlayerId);
        Assert.False(scene.LoginPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_input_failure_cannot_close_new_connection_or_clear_its_pending_input(bool godot)
    {
        using var scene = new SceneFixture(godot);
        var login = scene.Begin(); scene.Complete(false, false); await login;
        var oldInput = scene.Send();
        await scene.Reset(); scene.ReplaceClient();
        var nextLogin = scene.Begin(); scene.Complete(false, false); await nextLogin;
        var nextInput = scene.Send();
        var writes = scene.UiWrites;
        scene.FailPreviousInput();
        await oldInput.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(scene.HasClient);
        Assert.True(scene.InputPending);
        Assert.Equal(0, scene.Disposals);
        Assert.Equal(writes, scene.UiWrites);
        scene.CompleteInput(); await nextInput;
        Assert.False(scene.InputPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_login_cleans_up_and_allows_retry_and_cleanup_errors_are_observed(bool godot)
    {
        using var scene = new SceneFixture(godot);
        scene.FailDisposal = true;
        var login = scene.Begin(); scene.Complete(false, true); await login;
        Assert.False(scene.HasClient);
        Assert.False(scene.LoginPending);
        Assert.Equal(1, scene.Disposals);
        Assert.Equal(1, scene.CleanupErrors);
        scene.ReplaceClient();
        var retry = scene.Begin(); scene.Complete(false, false); await retry;
        Assert.Equal(7, scene.PlayerId);
        Assert.True(scene.HasClient);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Godot_smoke_ignores_late_login_after_exit(bool failure)
    {
        using var scene = new SceneFixture(true);
        var smoke = scene.Smoke();
        scene.Exit();
        scene.Complete(false, failure);
        await smoke.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, scene.QuitCalls);
        Assert.Equal(1, scene.Disposals);
    }

    private sealed class SceneFixture : IDisposable
    {
        private readonly AssemblyLoadContext context = new("SceneLifetime", isCollectible: true);
        private readonly dynamic probe;
        public SceneFixture(bool godot)
        {
            var spec = new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions("Lifetime", ".",
                godot ? ClientEngine.Godot : ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack,
                NuGetForUnitySource.OpenUpm, DeploymentProfile.None));
            var source = godot ? GodotClientCodeTemplates.RenderGameScene(spec) : UnityClientCodeTemplates.RenderGameController(spec);
            var members = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => new[] { "ConnectAsync", "SendInputAsync", "IsCurrentConnection", "DisposeClientAsync", "OnDestroy", "_ExitTree", "RunHeadlessSmokeAsync" }.Contains(method.Identifier.Text));
            // Execute generated lifecycle methods unchanged. Engine stand-ins count UI
            // writes, while controlled network tasks deliberately ignore cancellation
            // so a late completion cannot be mistaken for successful cancellation.
            var fixture = """
                using System;
                using System.Collections.Generic;
                using System.Threading;
                using System.Threading.Tasks;
                public class Engine { public virtual void _ExitTree() {} }
                public class Probe : Engine {
                    CancellationTokenSource? _cts;
                    GameClient? _client;
                    WorldSnapshot? _world;
                    long _localPlayerId;
                    bool _loginPending, _inputPending, _exiting;
                    List<int> _hitEffects = new();
                    Control _nameField = new(), _connectButton = new(), _statusLabel = new(), _loginPanel = new(), _hud = new();
                    Arena? _arenaView = null;
                    Root? _root = null;
                    public GameClient Current = new(), Previous = new();
                    public Probe() { Control.Writes = 0; Debug.Errors = 0; Tree.Quits = 0; }
                    object CreateLakonaGameClientOptions() => Current;
                    void SetStatus(string value) { _statusLabel.Text = value; }
                    void ShowGame() { _hud.Visible = true; }
                    void ShowLogin(string value) { _localPlayerId = 0; SetStatus(value); }
                    void QueueRedraw() { Control.Writes++; }
                    View GetViewportRect() => new();
                    Tree GetTree() => new();
                    void GenerateArenaVisualContent(object value) {}
                    void OnRootGeometryChanged(GeometryChangedEvent value) {}
                    public Task Begin() => ConnectAsync();
                    public Task Reset() { ShowLogin("reset"); return DisposeClientAsync(); }
                    public void ReplaceClient() { Previous = Current; Current = new(); }
                    public bool HasClient => _client != null;
                    public bool LoginPending => _loginPending;
                    public bool InputPending => _inputPending;
                    public long PlayerId => _localPlayerId;
                    public int UiWrites => Control.Writes;
                    public int CleanupErrors => Debug.Errors;
                    public int QuitCalls => Tree.Quits;
                """ + string.Join("\n", members.Select(method => method.ToFullString())) +
                (godot ? "public Task Send() => SendInputAsync(new Vector2()); public void Exit() => _ExitTree(); public Task Smoke() => RunHeadlessSmokeAsync();"
                    : "public Task Send() => SendInputAsync(1, 0); public void Exit() => OnDestroy(); public Task Smoke() => Task.CompletedTask;") + "}" + """
                public class WorldSnapshot {}
                public class Reply { public bool Success = true; public string Error = "rejected"; public long PlayerId = 7; public WorldSnapshot World = new(); }
                public class GameClient {
                    readonly GameClient? owner;
                    public GameClient() {}
                    public GameClient(object options) { owner = (GameClient)options; }
                    public bool HoldConnect, FailDisposal;
                    public int Disposals, LoginCalls;
                    public TaskCompletionSource Connected = new();
                    public TaskCompletionSource<Reply> LoggedIn = new();
                    public TaskCompletionSource Input = new();
                    public Task ConnectAsync(CancellationToken token) => owner!.HoldConnect ? owner.Connected.Task : Task.CompletedTask;
                    public Task<Reply> LoginAsync(string name) { owner!.LoginCalls++; return owner.LoggedIn.Task; }
                    public Task SubmitInputAsync(float x, float y) => owner!.Input.Task;
                    public ValueTask DisposeAsync() { owner!.Disposals++; return owner.FailDisposal ? new ValueTask(Task.FromException(new Exception("dispose"))) : default; }
                    public void Complete(bool connecting, bool failure) {
                        if(connecting) { if(failure) Connected.SetException(new Exception("connect")); else Connected.SetResult(); }
                        else { if(failure) LoggedIn.SetException(new Exception("login")); else LoggedIn.SetResult(new Reply()); }
                    }
                }
                public class Control {
                    public static int Writes;
                    public string value => "player";
                    public string Text { get => "player"; set => Writes++; }
                    public string text { get => "player"; set => Writes++; }
                    public bool Disabled { set => Writes++; }
                    public bool Visible { set => Writes++; }
                    public void SetEnabled(bool enabled) { Writes++; }
                }
                public class Arena { public event Action<object>? generateVisualContent; }
                public class Root { public void UnregisterCallback<T>(Action<T> callback) {} }
                public class GeometryChangedEvent {}
                public class Vector2 { public float X, Y; }
                public class View { public Vector2 Size = new(); }
                public class Tree { public static int Quits; public void Quit(int code) { Quits++; } }
                public static class Debug { public static int Errors; public static void LogException(Exception e) { Errors++; } }
                public static class GD { public static void Print(string s) {} public static void PrintErr(string s) { Debug.Errors++; } }
                """;
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("SceneLifetime" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(fixture)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            stream.Position = 0;
            probe = Activator.CreateInstance(context.LoadFromStream(stream).GetType("Probe")!)!;
        }
        public bool HoldConnect { set => probe.Current.HoldConnect = value; }
        public bool FailDisposal { set => probe.Current.FailDisposal = value; }
        public bool HasClient => probe.HasClient;
        public bool LoginPending => probe.LoginPending;
        public bool InputPending => probe.InputPending;
        public long PlayerId => probe.PlayerId;
        public int UiWrites => probe.UiWrites;
        public int CleanupErrors => probe.CleanupErrors;
        public int QuitCalls => probe.QuitCalls;
        public int Disposals => probe.Current.Disposals;
        public int LoginCalls => probe.Current.LoginCalls;
        public Task Begin() => probe.Begin();
        public Task Reset() => probe.Reset();
        public Task Send() => probe.Send();
        public Task Smoke() => probe.Smoke();
        public void Exit() => probe.Exit();
        public void ReplaceClient() => probe.ReplaceClient();
        public void Complete(bool connecting, bool failure, bool previous = false) => (previous ? probe.Previous : probe.Current).Complete(connecting, failure);
        public void FailPreviousInput() => probe.Previous.Input.SetException(new Exception("old send"));
        public void CompleteInput() => probe.Current.Input.SetResult();
        public void Dispose() { probe.Exit(); context.Unload(); }
    }
}
