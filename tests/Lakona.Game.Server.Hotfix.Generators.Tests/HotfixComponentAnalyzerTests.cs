using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixComponentAnalyzerTests
{
    private const string Imports = "using System.Threading; using System.Threading.Tasks; using Lakona.Game.Server.Hotfix.Abstractions; ";

    private static Microsoft.CodeAnalysis.MetadataReference StateReference() =>
        AnalyzerTestHost.CreateReference("StableStates", Imports + """
            [ComponentState] public sealed class State { public int Value; }
            [ComponentState] public sealed class OtherState { }
            public sealed class Box<T> { public sealed class Nested { } }
            """);

    [Theory]
    [InlineData("State[]")]
    [InlineData("System.Collections.Generic.IEnumerable<State>")]
    [InlineData("System.Func<State>")]
    [InlineData("System.Action<State>")]
    [InlineData("System.Lazy<State>")]
    [InlineData("System.Collections.Generic.Dictionary<string, System.Lazy<State[]>>")]
    [InlineData("(State, int)")]
    [InlineData("Box<State>.Nested")]
    public async Task State_wrappers_are_rejected_for_components_and_other_consumers(string dependency)
    {
        foreach (var marker in new[] { "[HotfixComponent]", "" })
        {
            var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
                + marker + " public sealed class Consumer(" + dependency + " states) { }", StateReference());
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061"
                && diagnostic.GetMessage().Contains("directly"));
        }
    }

    [Theory]
    [InlineData("State state = null")]
    [InlineData("ref State state")]
    [InlineData("in State state")]
    [InlineData("[Microsoft.Extensions.DependencyInjection.FromKeyedServices(\"other\")] State state")]
    public async Task State_injection_is_required_unkeyed_and_by_value(string parameter)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component { public Component(" + parameter + ") { } }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("private readonly State _a, _b; public Component(State state) { _a = state; _b = state; }")]
    [InlineData("private readonly State _a; private State B { get; } public Component(State state) { _a = state; B = state; }")]
    [InlineData("private State A { get; } private State B { get; } public Component(State state) { A = state; B = state; }")]
    public async Task State_can_be_stored_in_only_one_field_or_auto_property(string storage)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component { " + storage + " }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061"
            && diagnostic.GetMessage().Contains("at most one field"));
    }

    [Theory]
    [InlineData("private readonly State _state = state; public int Read() => state.Value;")]
    [InlineData("private State Stored { get; } = state; public int Read() { var local = state; return local.Value; }")]
    public async Task Primary_constructor_capture_cannot_duplicate_explicit_State_storage(string body)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(State state) { " + body + " }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061"
            && diagnostic.GetMessage().Contains("capture"));
    }

    [Theory]
    [InlineData("private readonly object _state = state;")]
    [InlineData("private object Stored { get; } = (object)state;")]
    [InlineData("private readonly System.Func<State> _state = () => state;")]
    public async Task State_storage_cannot_erase_or_wrap_its_type(string body)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(State state) { " + body + " }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("private System.Func<State> Factory() => () => state;")]
    [InlineData("private State[] Values => new[] { state };")]
    public async Task State_cannot_be_exposed_through_wrapped_member_types(string member)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(State state) { " + member + " }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061"
            && diagnostic.GetMessage().Contains("without collections or wrappers"));
    }

    [Theory]
    [InlineData("public int Read() => state.Value;")]
    [InlineData("private readonly State _state = state; public int Read() => _state.Value; private State View => _state;")]
    [InlineData("private State Stored { get; } = state; public int Read() => Stored.Value;")]
    [InlineData("private readonly State _state = state ?? throw new System.ArgumentNullException(nameof(state));")]
    [InlineData("private readonly State _state = state; public string Name => nameof(state);")]
    public async Task One_State_storage_location_and_computed_views_remain_valid(string body)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(State state) { " + body + " }", StateReference());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Stable_resource_interfaces_wrappers_and_multiple_dependencies_remain_valid()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Logging;
            [HotfixComponent] public sealed class Component(State state, IResource resource,
                IEnumerable<IResource> resources, Func<MutableResource> factory, Lazy<IResource> lazy,
                IConfiguration configuration, ILogger<Component> logger, ILogger<State> stateLogger)
            {
                private readonly State _state = state;
                private readonly IResource _resource = resource;
                public void Update() { _resource.Update(); _state.Value++; }
            }
            """, StateReference(), MutableResourceReference(),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger<>).Assembly.Location));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task State_ownership_does_not_depend_on_NodeRole()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            [NodeRole("gateway")] [HotfixComponent] public sealed class A(State state) { }
            [NodeRole("data")] [HotfixComponent] public sealed class B(State state) { }
            """, StateReference());
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "LAKONA20061"));
    }

    [Fact]
    public async Task Component_cannot_inject_two_different_States()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(State state, OtherState other) { }", StateReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061"
            && diagnostic.GetMessage().Contains("at most one state"));
    }

    private static Microsoft.CodeAnalysis.MetadataReference MutableResourceReference(string role = "ServerApp", bool marked = false) =>
        AnalyzerTestHost.CreateReference("StableResources", $$"""
            using System;
            using System.Reflection;
            using Lakona.Game.Server.Hotfix.Abstractions;
            [assembly: AssemblyMetadata("LakonaProjectRole", "{{role}}")]
            namespace App;
            public interface IResource
            {
                int Count { get; set; }
                void Update();
            }
            {{(marked ? "[ComponentState]" : "")}}
            public sealed class MutableResource
            {
                public int Count;
                public string Text { get; set; }
                public object Connection;
                public int this[int index] { get => Count; set => Count = value; }
                public void Update() { Count++; }
                public static explicit operator ResourceSnapshot(MutableResource resource) => new() { Count = resource.Count };
            }
            public sealed class ResourceSnapshot { public int Count; }
            """);

    [Theory]
    [InlineData("client.Connection = new object();")]
    [InlineData("client.Count += 1;")]
    [InlineData("client.Text ??= \"ready\";")]
    [InlineData("++client.Count;")]
    [InlineData("client.Count--;")]
    [InlineData("(client.Count, _) = (1, 2);")]
    [InlineData("System.Threading.Interlocked.Exchange(ref client.Count, 1);")]
    [InlineData("int.TryParse(\"1\", out client.Count);")]
    [InlineData("client[0] = 1;")]
    [InlineData("((MutableResource)client).Count = 1;")]
    public async Task Direct_writes_to_injected_App_dependencies_require_a_State_marker(string write)
    {
        var source = Imports + """
            using App;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            [HotfixComponent] public sealed class Component(MutableResource client)
            {
                [ComponentConfigureServices]
                public static void Configure(IServiceCollection services, IConfiguration configuration) =>
                    services.AddSingleton<MutableResource>();
                public void Write() {
            """ + write + "} }";
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(source, MutableResourceReference());
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20062"));
        Assert.Contains("[ComponentState]", diagnostic.GetMessage());
        Assert.Contains("MutableResource", diagnostic.GetMessage());
        Assert.Contains("client", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Theory]
    [InlineData("private readonly MutableResource _client; public Component(MutableResource client) { _client = client; }")]
    [InlineData("private MutableResource _client { get; } public Component(MutableResource client) { _client = client ?? throw new System.ArgumentNullException(nameof(client)); }")]
    public async Task Direct_writes_through_injected_fields_and_properties_require_a_State_marker(string storage)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "using App; [HotfixComponent] public sealed class Component { "
            + storage + " public void Write() { _client.Connection = new object(); } }", MutableResourceReference());
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("private readonly MutableResource _client = client;")]
    [InlineData("private MutableResource _client { get; } = client;")]
    public async Task Primary_constructor_dependency_initializers_are_checked(string storage)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "using App; [HotfixComponent] public sealed class Component(MutableResource client) { "
            + storage + " public void Write() { _client.Text = \"ready\"; } }", MutableResourceReference());
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("resource.Update(); var count = resource.Count;", false)]
    [InlineData("resource.Count = 1;", true)]
    public async Task Stable_App_interfaces_allow_APIs_and_reads_but_require_State_for_direct_writes(string body, bool fails)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "using App; [HotfixComponent] public sealed class Component(IResource resource) { public void Execute() { "
            + body + " } }", MutableResourceReference());
        Assert.Equal(fails, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20062"));
        if (!fails) Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Casting_an_injected_object_to_an_App_carrier_does_not_bypass_the_marker()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component(object client)
            {
                public void Write() { ((MutableResource)client).Count = 1; }
            }
            """, MutableResourceReference());
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("ServerApp", "Hotfix")]
    [InlineData(" serverapp ", " HoTfIx ")]
    public async Task State_write_detection_uses_project_roles_instead_of_names(string appRole, string hotfixRole)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component(MutableResource client)
            {
                public void Write() { client.Count = 1; }
            }
            """, hotfixRole, MutableResourceReference(appRole));
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("Shared", false)]
    [InlineData("", false)]
    [InlineData("ServerApp", true)]
    public async Task Marked_State_and_other_stable_dependency_assemblies_remain_valid(string role, bool marked)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component(MutableResource client)
            {
                public void Write() { client.Count = 1; }
            }
            """, MutableResourceReference(role, marked));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Resource_APIs_reads_DTO_arguments_and_new_object_initialization_remain_valid()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            using System;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            [HotfixComponent] public sealed class Component(MutableResource client, System.Net.Http.HttpClient http)
            {
                public int Read() => client.Count;
                public void Update(MutableResource request)
                {
                    client.Update();
                    http.Timeout = TimeSpan.FromSeconds(10);
                    request.Count = 1;
                    var local = new MutableResource { Count = 2 };
                    local.Text = "ready";
                    ((ResourceSnapshot)client).Count = 3;
                }
                [ComponentConfigureServices]
                public static void Configure(IServiceCollection services, IConfiguration configuration) =>
                    services.AddSingleton(_ => new MutableResource { Connection = new object() });
            }
            """, MutableResourceReference(), Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location));
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("public void Configure(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("private static void Configure(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Configure<T>(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static Task Configure(IServiceCollection services, IConfiguration config) => Task.CompletedTask;")]
    [InlineData("public static async void Configure(IServiceCollection services, IConfiguration config) { await Task.Yield(); }")]
    [InlineData("public static void Configure(IServiceCollection services) {}")]
    [InlineData("public static void Configure(IConfiguration config, IServiceCollection services) {}")]
    [InlineData("public static void Configure(ref IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Configure(IServiceCollection services, IConfiguration config = null) {}")]
    public async Task Component_rejects_invalid_service_configuration(string method)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports
            + "using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Configuration; "
            + "[HotfixComponent] public sealed class Component { [ComponentConfigureServices] " + method + " }", "Hotfix");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("ServerApp", "[ComponentState]", true)]
    [InlineData("ServerApp", "", true)]
    [InlineData("Hotfix", "[HotfixComponent]", false)]
    [InlineData("hotfix", "[HotfixComponent]", false)]
    [InlineData("HoTfIx", "[HotfixComponent]", false)]
    [InlineData(" Hotfix ", "[HotfixComponent]", false)]
    [InlineData("\thotfix\t", "[HotfixComponent]", false)]
    public async Task Service_configuration_requires_hotfix_component(string role, string marker, bool fails)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports
            + "using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Configuration; "
            + marker + " public sealed class State { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) {} }", role);
        Assert.Equal(fails, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20061"));
    }

    [Fact]
    public async Task Component_rejects_duplicate_service_configuration()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            [HotfixComponent] public sealed class Component
            {
                [ComponentConfigureServices] public static void First(IServiceCollection services, IConfiguration config) {}
                [ComponentConfigureServices] public static void Second(IServiceCollection services, IConfiguration config) {}
            }
            """, "Hotfix");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("ServerApp")]
    [InlineData("serverapp")]
    [InlineData("SeRvErApP")]
    [InlineData(" ServerApp ")]
    [InlineData("\tserverapp\t")]
    public async Task Stable_project_cannot_declare_components_regardless_of_role_spelling(string role)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(
            Imports + "[HotfixComponent] public sealed class Component {}", role);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("public Task Start(CancellationToken token) => Task.CompletedTask;")]
    [InlineData("public ValueTask Start() => default;")]
    [InlineData("public static ValueTask Start(CancellationToken token) => default;")]
    [InlineData("public ValueTask Start(CancellationToken token = default) => default;")]
    [InlineData("public ValueTask Start(ref CancellationToken token) => default;")]
    [InlineData("public ValueTask Start<T>(CancellationToken token) => default;")]
    [InlineData("private ValueTask Start(CancellationToken token) => default;")]
    [InlineData("[ComponentStop] public ValueTask Start(CancellationToken token) => default;")]
    public async Task Rejects_invalid_lifecycle_signature(string method)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "[HotfixComponent] public sealed class C { [ComponentStart] " + method + " }");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("")]
    [InlineData("[ComponentStart] public ValueTask Begin(CancellationToken token) => default;")]
    [InlineData("[ComponentStop] public ValueTask End(CancellationToken token) => default;")]
    public async Task Allows_optional_hooks_and_no_state(string hook)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "[HotfixComponent] public sealed class C { " + hook + " }");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("LAKONA2006", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Hotfix", "public sealed class State { }", true)]
    [InlineData("ServerApp", "public class State { }", true)]
    [InlineData("ServerApp", "public sealed class State<T> { }", true)]
    [InlineData("ServerApp", "public sealed class State { public State(int value) {} }", true)]
    [InlineData("ServerApp", "public sealed class State { }", false)]
    [InlineData("serverapp", "public sealed class State { }", false)]
    [InlineData("SeRvErApP", "public sealed class State { }", false)]
    [InlineData(" ServerApp ", "public sealed class State { }", false)]
    [InlineData("\tserverapp\t", "public sealed class State { }", false)]
    public async Task State_requires_stable_project_and_simple_construction(string role, string declaration, bool fails)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] " + declaration, role);
        Assert.Equal(fails, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20062"));
    }

    [Theory]
    [InlineData("public System.Action Callback;")]
    [InlineData("public Task Pending;")]
    [InlineData("public System.IServiceProvider Services;")]
    public async Task State_rejects_obvious_execution_references(string member)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State { " + member + " }", "ServerApp");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Fact]
    public async Task State_allows_mechanical_async_disposal()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State : System.IAsyncDisposable { public ValueTask DisposeAsync() => default; }", "ServerApp");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Fact]
    public async Task State_rejects_multiple_owners_foreign_access_and_manual_creation()
    {
        var reference = AnalyzerTestHost.CreateReference("StableApp", Imports + "[ComponentState] public sealed class State { public int Value; }");
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            [HotfixComponent] public sealed class A(State state) { public int Read() => state.Value; }
            [HotfixComponent] public sealed class B(State state) { }
            public sealed class Stranger
            {
                public int Read(State state) => state.Value;
                public void Create() { _ = new State(); }
            }
            """, reference);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "LAKONA20061"));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "LAKONA20063"));
    }
}
