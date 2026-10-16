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
            """, StateReference(), MutableResourceReference(""),
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

    private static Microsoft.CodeAnalysis.MetadataReference MutableResourceReference(string role = "ServerApp") =>
        AnalyzerTestHost.CreateReference("StableResources", $$"""
            using System;
            using System.Reflection;
            [assembly: AssemblyMetadata("LakonaProjectRole", "{{role}}")]
            namespace App;
            public interface IResource { int Count { get; set; } void Update(); }
            public sealed class MutableResource
            {
                public int Count;
                public string Text { get; set; }
                public object Connection;
                public void Update() { Count++; }
            }
            public readonly struct ResourceId { }
            public enum ResourceMode { Default }
            public sealed class ResourceSnapshot { public int Count; }
            """);

    [Theory]
    [InlineData("")]
    [InlineData("var count = client.Count;")]
    [InlineData("client.Update();")]
    public async Task Unmarked_App_dependencies_are_rejected_even_without_direct_writes(string body)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            [HotfixComponent] public sealed class Component(MutableResource client)
            {
                [ComponentConfigureServices]
                public static void Configure(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<MutableResource>();
                public void Execute() {
            """ + body + "} }", MutableResourceReference());
        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20061"));
        Assert.Contains("[ComponentState]", diagnostic.GetMessage());
        Assert.Equal("client", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData("IResource")]
    [InlineData("ResourceId")]
    [InlineData("ResourceMode")]
    [InlineData("MutableResource[]")]
    [InlineData("System.Collections.Generic.IEnumerable<IResource>")]
    [InlineData("System.Func<MutableResource>")]
    [InlineData("System.Lazy<IResource[]>")]
    [InlineData("System.Collections.Generic.Dictionary<string, System.Func<MutableResource>>")]
    public async Task App_interfaces_values_and_wrappers_cannot_hide_custom_dependencies(string dependency)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "using App; [HotfixComponent] public sealed class Component("
            + dependency + " resource) { }", MutableResourceReference());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061" && diagnostic.GetMessage().Contains("Custom ServerApp"));
    }

    [Theory]
    [InlineData("object")]
    [InlineData("dynamic")]
    [InlineData("System.Func<object>")]
    public async Task Opaque_dependency_types_cannot_hide_App_state(string dependency)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + "[HotfixComponent] public sealed class Component("
            + dependency + " resource) { }");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("ServerApp", "Hotfix")]
    [InlineData(" serverapp ", " HoTfIx ")]
    public async Task App_dependency_detection_uses_project_roles_instead_of_names(string appRole, string hotfixRole)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component(MutableResource client) { }
            """, hotfixRole, MutableResourceReference(appRole));
        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "LAKONA20061"));
    }

    [Theory]
    [InlineData("Shared")]
    [InlineData("")]
    public async Task External_stable_resources_are_not_classified_by_their_names_or_mutability(string role)
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component(MutableResource client) { public void Update() => client.Update(); }
            """, MutableResourceReference(role));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task App_DTO_method_arguments_and_new_objects_remain_valid()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync(Imports + """
            using App;
            [HotfixComponent] public sealed class Component
            {
                public void Update(MutableResource request)
                {
                    request.Count = 1;
                    var local = new MutableResource { Count = 2 };
                    local.Update();
                }
            }
            """, MutableResourceReference());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Generated_ActorAccess_is_allowed_without_exempting_an_entire_namespace()
    {
        var reference = AnalyzerTestHost.CreateReference("StableGenerated", """
            [assembly: System.Reflection.AssemblyMetadata("LakonaProjectRole", "ServerApp")]
            namespace Lakona.Game.Server.Hotfix;
            public sealed class ActorAccess { }
            public sealed class CustomDependency { }
            """);
        var allowed = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(Lakona.Game.Server.Hotfix.ActorAccess actors) { }", reference);
        Assert.Empty(allowed);
        var rejected = await AnalyzerTestHost.RunHotfixProjectAsync(Imports
            + "[HotfixComponent] public sealed class Component(Lakona.Game.Server.Hotfix.CustomDependency custom) { }", reference);
        Assert.Contains(rejected, diagnostic => diagnostic.Id == "LAKONA20061");
    }

    [Theory]
    [InlineData("public void Initialize() { }")]
    [InlineData("private Task LoadAsync() => Task.CompletedTask;")]
    [InlineData("public static void Refresh() { }")]
    [InlineData("public void Dispose() { }")]
    [InlineData("public ValueTask DisposeAsync() => default;")]
    [InlineData("public override string ToString() => \"state\";")]
    [InlineData("public static State operator +(State left, State right) => left;")]
    [InlineData("public int Value => 1;")]
    [InlineData("public int this[int index] { get => index; set { } }")]
    public async Task State_rejects_methods_operators_computed_properties_and_fake_disposal(string member)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State { " + member + " }", "ServerApp");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Theory]
    [InlineData("System.IDisposable", "public void Dispose() { }")]
    [InlineData("System.IDisposable", "void System.IDisposable.Dispose() { }")]
    [InlineData("System.IAsyncDisposable", "public async ValueTask DisposeAsync() { await Task.Yield(); }")]
    [InlineData("System.IAsyncDisposable", "ValueTask System.IAsyncDisposable.DisposeAsync() => default;")]
    public async Task State_allows_data_initialization_and_real_disposal_implementations(string contract, string method)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + "[ComponentState] public sealed class State : " + contract
            + " { public State() { Count = 1; } public int Count { get; private set; } " + method + " }", "ServerApp");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inherited_business_methods_are_not_a_State_escape()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports + """
            public class Base { public void Refresh() { } }
            [ComponentState] public sealed class State : Base { }
            """, "ServerApp");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "LAKONA20062");
    }

    [Theory]
    [InlineData("public int Count { get; set; }", false)]
    [InlineData("public int Count => 1;", true)]
    public async Task Inherited_metadata_properties_follow_the_same_data_only_rules(string member, bool rejected)
    {
        var reference = AnalyzerTestHost.CreateReference("StableBase", "public class Base { " + member + " }");
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(Imports
            + "[ComponentState] public sealed class State : Base { }", "ServerApp", reference);
        Assert.Equal(rejected, diagnostics.Any(diagnostic => diagnostic.Id == "LAKONA20062"));
        if (!rejected) Assert.Empty(diagnostics);
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
