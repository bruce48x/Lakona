using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ComponentServiceRegistrationTests
{
    [Theory]
    [InlineData("data", "A", "Z")]
    [InlineData("gateway", "A", null)]
    public void Configuration_obeys_component_roles_and_name_order_without_constructing_components(string role, string first, string? last)
    {
        var assembly = Compile("""
            [HotfixComponent] public sealed class A
            {
                public A() => throw new Exception("Component constructed during registration");
                [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) =>
                    services.AddSingleton(_ => new RegistrationProbe(config["value"] + ":A"));
            }
            [HotfixComponent, NodeRole("data")] public sealed class Z
            {
                [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) =>
                    services.AddSingleton(_ => new RegistrationProbe(config["value"] + ":Z"));
            }
            """);
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["value"] = "configured" }).Build();
        using var registration = HotfixComponentRegistration.Configure(services, assembly, config, [role]);
        RegistrationProbe[] resources;
        using (var root = services.BuildServiceProvider())
        {
            registration.Initialize(root);
            registration.Initialize(root);
            resources = root.GetServices<RegistrationProbe>().ToArray();
            string[] expected = last is null ? ["configured:" + first] : ["configured:" + first, "configured:" + last];
            Assert.Equal(expected, resources.Select(resource => resource.Name));
            Assert.Same(resources[^1], root.GetRequiredService<RegistrationProbe>());
            Assert.Null(root.GetService(assembly.GetType("A")!));
        }
        Assert.All(resources, resource => Assert.Equal(1, resource.Disposals));
    }

    [Theory]
    [InlineData("public void Register(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("private static void Register(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static int Register(IServiceCollection services, IConfiguration config) => 0;")]
    [InlineData("public static void Register<T>(IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Register(IServiceCollection services) {}")]
    [InlineData("public static void Register(IConfiguration config, IServiceCollection services) {}")]
    [InlineData("public static void Register(ref IServiceCollection services, IConfiguration config) {}")]
    [InlineData("public static void Register(IServiceCollection services, IConfiguration config = null) {}")]
    [InlineData("public static async void Register(IServiceCollection services, IConfiguration config) { await Task.Yield(); }")]
    public void Invalid_hook_is_rejected_before_configuration_even_when_its_role_is_disabled(string method)
    {
        var assembly = Compile("""
            [HotfixComponent] public sealed class A
            {
                [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) => throw new Exception("USER_CODE_EXECUTED");
            }
            """ + "[HotfixComponent, NodeRole(\"data\")] public sealed class Z { [ComponentConfigureServices] " + method + " }");
        var error = Assert.Throws<InvalidOperationException>(() => HotfixComponentRegistration.Configure(
            new ServiceCollection(), assembly, new ConfigurationBuilder().Build(), ["gateway"]));
        Assert.Contains("public static", error.Message);
        Assert.DoesNotContain("USER_CODE_EXECUTED", error.Message);
    }

    [Theory]
    [InlineData("public sealed class A { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) {} }")]
    [InlineData("[ComponentState] public sealed class A { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) {} }")]
    [InlineData("[HotfixComponent] public sealed class A { [ComponentConfigureServices] public static void First(IServiceCollection services, IConfiguration config) {} [ComponentConfigureServices] public static void Second(IServiceCollection services, IConfiguration config) {} }")]
    public void Configuration_requires_one_method_on_a_hotfix_component(string source)
    {
        var error = Assert.Throws<InvalidOperationException>(() => HotfixComponentRegistration.Configure(
            new ServiceCollection(), Compile(source), new ConfigurationBuilder().Build(), []));
        Assert.Contains("HotfixComponent", error.Message);
    }

    [Theory]
    [InlineData("services.AddTransient<RegistrationProbe>();")]
    [InlineData("services.AddScoped<RegistrationProbe>();")]
    [InlineData("services.AddSingleton(\"instance\");")]
    [InlineData("services.AddSingleton<A>();")]
    [InlineData("services.AddSingleton<ComponentProbeState>();")]
    [InlineData("services.AddSingleton(typeof(System.Collections.Generic.List<>), typeof(System.Collections.Generic.List<>));")]
    [InlineData("services.AddKeyedSingleton<RegistrationProbe>(\"key\", (_, _) => new RegistrationProbe(\"keyed\"));")]
    public void Root_registration_rejects_unsupported_lifetimes_types_and_instances(string body)
    {
        var assembly = Compile("[HotfixComponent] public sealed class A { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) { " + body + " } }");
        var error = Assert.Throws<InvalidOperationException>(() => HotfixComponentRegistration.Configure(
            new ServiceCollection(), assembly, new ConfigurationBuilder().Build(), []));
        Assert.Contains("unkeyed singleton of closed stable types", error.Message);
    }

    [Theory]
    [InlineData("services.AddSingleton<object>(_ => new A());")]
    [InlineData("services.AddSingleton<Func<string>>(_ => () => \"retained Hotfix callback\");")]
    public void Factory_must_return_a_stable_resource(string body)
    {
        var assembly = Compile("[HotfixComponent] public sealed class A { [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config) { " + body + " } }");
        var services = new ServiceCollection();
        using var registration = HotfixComponentRegistration.Configure(services, assembly, new ConfigurationBuilder().Build(), []);
        using var root = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() => registration.Initialize(root));
        Assert.Contains("stable resource", error.Message);
    }

    [Fact]
    public void Failed_factory_preserves_error_and_root_disposes_previously_created_resources()
    {
        var assembly = Compile("""
            [HotfixComponent] public sealed class A
            {
                [ComponentConfigureServices] public static void Register(IServiceCollection services, IConfiguration config)
                {
                    services.AddSingleton(_ => new RegistrationProbe("created"));
                    services.AddSingleton<object>(_ => throw new ArgumentException("broken root resource"));
                }
            }
            """);
        var services = new ServiceCollection();
        using var registration = HotfixComponentRegistration.Configure(services, assembly, new ConfigurationBuilder().Build(), []);
        var root = services.BuildServiceProvider();
        var error = Assert.Throws<ArgumentException>(() => registration.Initialize(root));
        Assert.Equal("broken root resource", error.Message);
        var created = root.GetRequiredService<RegistrationProbe>();
        root.Dispose();
        Assert.Equal(1, created.Disposals);
    }

    [Fact]
    public async Task First_assembly_transfers_to_manager_and_root_factory_does_not_pin_retired_generation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LakonaComponentRegistration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Hotfix.dll");
        var source = new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll");
        var names = new[] { typeof(RegistrationProbe).Assembly.GetName().Name! };
        var services = new ServiceCollection();
        var counter = new RegistrationCounter();
        services.AddSingleton(counter);
        File.WriteAllBytes(path, CompileBytes(Reloadable("v1")));
        using var registration = await HotfixComponentRegistration.PrepareAsync(services, new ConfigurationBuilder().Build(), [], source, names);
        services.AddSingleton(_ => registration);
        await using var root = services.BuildServiceProvider();
        _ = root.GetRequiredService<HotfixComponentRegistration>();
        registration.Initialize(root);
        var resource = root.GetRequiredService<RegistrationProbe>();
        try
        {
            await using var manager = new HotfixManager(source, names, rootServices: root);
            // Changing the file before initial activation must not replace the prepared assembly.
            File.WriteAllBytes(path, CompileBytes(Reloadable("v2")));
            Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.Equal("v1", resource.Name);
            Assert.Equal("v1", ReadVersion(manager));
            var oldContext = RetiredContext(manager);
            Assert.True((await manager.ValidateAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.True((await manager.ReloadAsync(TestContext.Current.CancellationToken)).Succeeded);
            Assert.Equal("v2", ReadVersion(manager));
            Assert.Same(resource, ((IHotfixRuntimeAccessor)manager).Current.Services.GetRequiredService<RegistrationProbe>());
            Assert.Equal(1, counter.FactoryCalls);
            for (var attempt = 0; attempt < 30 && oldContext.IsAlive; attempt++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.False(oldContext.IsAlive);
            await manager.DisposeAsync();
            Assert.Equal(0, resource.Disposals);
            await root.DisposeAsync();
            Assert.Equal(1, resource.Disposals);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetiredContext(HotfixManager manager) =>
        new(((IHotfixRuntimeAccessor)manager).Current.LoadContext!);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadVersion(HotfixManager manager)
    {
        var runtime = ((IHotfixRuntimeAccessor)manager).Current;
        var type = runtime.MainAssembly!.GetType("A")!;
        return (string)type.GetProperty("Version")!.GetValue(runtime.Services.GetRequiredService(type))!;
    }

    private static string Reloadable(string version) => $$"""
        [HotfixComponent] public sealed class A
        {
            public string Version => "{{version}}";
            [ComponentConfigureServices] public static void Configure(IServiceCollection services, IConfiguration config) =>
                services.AddSingleton(provider => { provider.GetRequiredService<RegistrationCounter>().FactoryCalls++; return new RegistrationProbe("{{version}}"); });
        }
        public sealed class GeneratedRegistration : IHotfixGeneratedServiceRegistration
        { public void Register(IServiceCollection services) => services.AddSingleton<A>(); }
        """;

    private static Assembly Compile(string declarations) => AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(CompileBytes(declarations)));

    private static byte[] CompileBytes(string declarations)
    {
        var source = """
            using System;
            using System.Reflection;
            using System.Threading.Tasks;
            using Lakona.Game.Server;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Tests;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            [assembly: AssemblyMetadata("LakonaProjectRole", "Hotfix")]
            """ + declarations;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(HotfixComponentRegistration).Assembly.Location).Append(typeof(RegistrationProbe).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("ComponentRegistration" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}

public sealed class RegistrationCounter { public int FactoryCalls; }

public sealed class RegistrationProbe(string name) : IDisposable
{
    public string Name { get; } = name;
    public int Disposals { get; private set; }
    public void Dispose() => Disposals++;
}
