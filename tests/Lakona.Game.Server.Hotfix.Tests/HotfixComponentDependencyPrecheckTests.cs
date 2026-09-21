using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Generators;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixComponentDependencyPrecheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Business_exceptions_load_without_DI_and_misapplied_markers_get_actionable_errors(bool marked)
    {
        var directory = Directory.CreateTempSubdirectory("lakona-business-errors-");
        try
        {
            var source = $$"""
                using System;
                using Lakona.Game.Server.Hotfix.Abstractions;
                using Microsoft.Extensions.DependencyInjection;
                internal enum ErrorCode { Failed }
                internal abstract class BusinessError : Exception
                {
                    protected BusinessError(ErrorCode code, Exception inner) : base(code.ToString(), inner) { }
                }
                {{(marked ? "[HotfixComponent]" : "")}}
                internal sealed class SessionError : BusinessError
                {
                    public SessionError(ErrorCode code, Exception inner) : base(code, inner) { }
                }
                {{(marked ? "[HotfixComponent]" : "")}}
                internal sealed class ChatError : Exception
                {
                    public ChatError(ErrorCode code, Exception inner) : base(code.ToString(), inner) { }
                }
                [HotfixStartup] public static class Startup
                {
                    [HotfixConfigureServices] public static void Configure(IServiceCollection services)
                    {
                        var inner = new Exception("original");
                        if (new SessionError(ErrorCode.Failed, inner).InnerException != inner ||
                            new ChatError(ErrorCode.Failed, inner).Message != "Failed")
                            throw new Exception("Business exception construction failed");
                    }
                }
                """;
            var compilation = CSharpCompilation.Create("BusinessErrorsHotfix", [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
                HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixManager), typeof(IServiceCollection)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Compilation candidate = compilation;
            // Mis-marked candidates model older binaries built without the updated compiler checks.
            if (!marked)
            {
                CSharpGeneratorDriver.Create(new HotfixGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out candidate, out var diagnostics, TestContext.Current.CancellationToken);
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                Assert.DoesNotContain(candidate.SyntaxTrees, tree => tree.ToString().Contains("TryAddSingleton"));
            }
            var emit = candidate.Emit(Path.Combine(directory.FullName, "Hotfix.dll"), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            await using var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory.FullName, "Hotfix.dll"), participants: []);
            var result = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(!marked, result.Succeeded);
            if (marked)
            {
                Assert.Contains("remove the attribute", result.ErrorMessage);
                Assert.Contains("SessionError", result.ErrorMessage);
                Assert.Contains("ChatError", result.ErrorMessage);
                Assert.DoesNotContain("service is not registered", result.ErrorMessage);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("missing", false, false)]
    [InlineData("transitive", false, false)]
    [InlineData("enumerable", false, false)]
    [InlineData("scoped", false, false)]
    [InlineData("generic", false, false)]
    [InlineData("optional", true, false)]
    [InlineData("empty", true, false)]
    [InlineData("last", true, false)]
    [InlineData("collectionOverride", true, false)]
    [InlineData("exactGeneric", true, false)]
    [InlineData("instance", true, false)]
    [InlineData("factory", true, true)]
    [InlineData("dependencyFactory", true, true)]
    [InlineData("keyed", true, true)]
    [InlineData("constructors", true, true)]
    [InlineData("constrainedEnumerable", true, false)]
    [InlineData("recursiveGeneric", true, true)]
    [InlineData("root", true, false)]
    [InlineData("opaqueRoot", true, true)]
    [InlineData("genericRoot", false, false)]
    [InlineData("genericOpaqueRoot", false, false)]
    [InlineData("genericEnumerableRoot", false, false)]
    [InlineData("genericOptionalRoot", true, false)]
    [InlineData("genericBridgeRoot", true, false)]
    [InlineData("closedGenericRoot", true, false)]
    public async Task Lazy_component_metadata_is_checked_without_running_user_code(string scenario, bool succeeds, bool warns)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-component-precheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var rootActivations = 0;
        using var root = new ServiceCollection().AddSingleton<Uri>(_ =>
        {
            rootActivations++;
            throw new InvalidOperationException("ROOT_FACTORY_MUST_NOT_RUN");
        }).BuildServiceProvider();
        IServiceProvider? rootProvider = scenario is "opaqueRoot" or "genericOpaqueRoot" ? new OpaqueRoot()
            : scenario == "root" || scenario.EndsWith("Root", StringComparison.Ordinal) ? root : null;
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"), rootServices: rootProvider, participants: []);
        try
        {
            Emit(directory, "valid");
            var initial = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.True(initial.Succeeded, initial.ErrorMessage);
            Emit(directory, scenario);
            var validation = await manager.ValidateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(succeeds, validation.Succeeded);
            Assert.Equal(initial.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
            var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(succeeds, reload.Succeeded);
            Assert.Equal(warns, validation.Status == HotfixReloadStatus.SucceededWithWarnings);
            Assert.Equal(warns, reload.Status == HotfixReloadStatus.SucceededWithWarnings);
            if (!succeeds)
            {
                Assert.Contains("LazyComponent ->", reload.ErrorMessage);
                Assert.Contains(scenario == "scoped" ? "scoped" : "System.Uri", reload.ErrorMessage);
                Assert.Equal(initial.Current.DispatchTableVersion, manager.Current.DispatchTableVersion);
                if (scenario is "genericRoot" or "genericOpaqueRoot" or "genericEnumerableRoot")
                    Assert.Contains("without stable-provider fallback", reload.ErrorMessage);
            }
            if (warns)
            {
                Assert.Contains(reload.Diagnostics, text => text.Contains("coverage incomplete"));
                Assert.Equal(HotfixReloadStatus.SucceededWithWarnings, manager.Current.LastReloadStatus);
            }
            Assert.Equal(0, rootActivations);
            Emit(directory, "valid");
            Assert.Equal(HotfixReloadStatus.Succeeded, (await manager.ReloadAsync(TestContext.Current.CancellationToken)).Status);
        }
        finally
        {
            await manager.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    private sealed class OpaqueRoot : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(Uri)
            ? throw new InvalidOperationException("OPAQUE_ROOT_MUST_NOT_RESOLVE_DEPENDENCIES") : null;
    }

    [Theory]
    [InlineData("genericOptionalRoot", 0)]
    [InlineData("genericBridgeRoot", 1)]
    [InlineData("closedGenericRoot", 1)]
    public async Task Accepted_generic_graphs_resolve_with_the_actual_activation_provider(string scenario, int expectedRootActivations)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-generic-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var rootActivations = 0;
        using var root = new ServiceCollection().AddSingleton<Uri>(_ =>
        {
            rootActivations++;
            return new Uri("https://example.test");
        }).BuildServiceProvider();
        var manager = new HotfixManager(new CurrentDirectoryHotfixAssemblySource(directory, "Hotfix.dll"), rootServices: root, participants: []);
        try
        {
            Emit(directory, scenario, allowActivation: true);
            var validation = await manager.ValidateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HotfixReloadStatus.Succeeded, validation.Status);
            var reload = await manager.ReloadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HotfixReloadStatus.Succeeded, reload.Status);
            Assert.Equal(0, rootActivations);
            using var lease = ((IHotfixRuntimeAccessor)manager).AcquireCurrent();
            var runtime = ((IHotfixRuntimeAccessor)manager).Current;
            Assert.NotNull(runtime.Services.GetRequiredService(runtime.MainAssembly!.GetType("LazyComponent")!));
            Assert.Equal(expectedRootActivations, rootActivations);
        }
        finally
        {
            await manager.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    private static void Emit(string directory, string scenario, bool allowActivation = false)
    {
        var parameter = scenario switch
        {
            "transitive" or "last" or "dependencyFactory" or "scoped" => "IDep value",
            "enumerable" or "collectionOverride" => "IEnumerable<IDep> value",
            "empty" => "IEnumerable<Uri> values",
            "optional" => "Uri? value = null",
            "keyed" => "[FromKeyedServices(\"special\")] Uri value",
            "generic" or "recursiveGeneric" or "exactGeneric" or "genericRoot" or "genericOpaqueRoot" or
                "genericOptionalRoot" or "genericBridgeRoot" or "closedGenericRoot" => "IRepo<int> value",
            "genericEnumerableRoot" => "IEnumerable<IRepo<int>> value",
            "constrainedEnumerable" => "IEnumerable<IRepo<string>> value",
            "valid" => "",
            _ => "Uri value"
        };
        var registration = scenario switch
        {
            "transitive" or "enumerable" => "services.AddSingleton<IDep, BadDep>();",
            "last" => "services.AddSingleton<IDep, BadDep>(); services.AddSingleton<IDep, GoodDep>();",
            "collectionOverride" => "services.AddSingleton<IDep, BadDep>(); services.AddSingleton<IEnumerable<IDep>>(Array.Empty<IDep>());",
            "exactGeneric" => "services.AddSingleton<IRepo<int>, GoodRepo<int>>(); services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));",
            "closedGenericRoot" => "services.AddSingleton<IRepo<int>, Repo<int>>();",
            "genericBridgeRoot" => "services.AddSingleton(typeof(IRepo<>), typeof(Repo<>)); services.AddSingleton<IDep, BadDep>();",
            "scoped" => "services.AddScoped<IDep, GoodDep>();",
            "factory" => "services.AddSingleton<LazyComponent>(_ => throw new Exception(\"FACTORY_MUST_NOT_RUN\"));",
            "dependencyFactory" => "services.AddSingleton<IDep>(_ => throw new Exception(\"FACTORY_MUST_NOT_RUN\"));",
            "instance" => "services.AddSingleton(new LazyComponent());",
            "generic" or "constrainedEnumerable" or "recursiveGeneric" or "genericRoot" or "genericOpaqueRoot" or
                "genericOptionalRoot" or "genericEnumerableRoot" => "services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));",
            _ => ""
        };
        var repo = scenario switch
        {
            "constrainedEnumerable" => "public sealed class Repo<T> : IRepo<T> where T : struct { }",
            "recursiveGeneric" => "public sealed class Repo<T> : IRepo<T> { public Repo(IRepo<List<T>> next) {} }",
            "genericBridgeRoot" => "public sealed class Repo<T> : IRepo<T> { public Repo(IDep local) {} }",
            "genericOptionalRoot" => "public sealed class Repo<T> : IRepo<T> { public Repo(Uri? optional = null) { if (optional is not null) throw new Exception(\"Expected native DI default\"); } }",
            _ => "public sealed class Repo<T> : IRepo<T> { public Repo(Uri missing) {} }"
        };
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Http;
            using Microsoft.Extensions.DependencyInjection;
            public interface IDep { }
            public sealed class BadDep : IDep { public BadDep(Uri missing) {} }
            public sealed class GoodDep : IDep { }
            public interface IRepo<T> { }
            public sealed class GoodRepo<T> : IRepo<T> { }
            {{repo}}
            [HotfixComponent] public sealed class LazyComponent
            {
                public LazyComponent({{parameter}}) { {{(allowActivation ? "" : "throw new Exception(\"CONSTRUCTOR_MUST_NOT_RUN\");")}} }
                {{(scenario is "constructors" or "instance" ? "public LazyComponent() {}" : "")}}
            }
            [HotfixStartup] public static class Startup
            {
                [HotfixConfigureServices] public static void Configure(IServiceCollection services) { {{registration}} }
            }
            [LakonaHttpService("probe")] public sealed class ProbeService
            {
                [LakonaHttpEndpoint("GET", "/probe")] public ValueTask<LakonaHttpResponse> Get(LakonaHttpCall call) => default;
            }
            """;
        var compilation = CSharpCompilation.Create("LazyComponentHotfix", [CSharpSyntaxTree.ParseText(source)],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixManager), typeof(IServiceCollection), typeof(ServiceCollectionServiceExtensions)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver.Create(new HotfixGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        var emit = generated.Emit(Path.Combine(directory, "Hotfix.dll"));
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }
}
