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
        IServiceProvider? rootProvider = scenario == "root" ? root : scenario == "opaqueRoot" ? new OpaqueRoot() : null;
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

    private static void Emit(string directory, string scenario)
    {
        var parameter = scenario switch
        {
            "transitive" or "last" or "dependencyFactory" or "scoped" => "IDep value",
            "enumerable" or "collectionOverride" => "IEnumerable<IDep> value",
            "empty" => "IEnumerable<Uri> values",
            "optional" => "Uri? value = null",
            "keyed" => "[FromKeyedServices(\"special\")] Uri value",
            "generic" or "recursiveGeneric" or "exactGeneric" => "IRepo<int> value",
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
            "scoped" => "services.AddScoped<IDep, GoodDep>();",
            "factory" => "services.AddSingleton<LazyComponent>(_ => throw new Exception(\"FACTORY_MUST_NOT_RUN\"));",
            "dependencyFactory" => "services.AddSingleton<IDep>(_ => throw new Exception(\"FACTORY_MUST_NOT_RUN\"));",
            "instance" => "services.AddSingleton(new LazyComponent());",
            "generic" or "constrainedEnumerable" or "recursiveGeneric" => "services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));",
            _ => ""
        };
        var repo = scenario switch
        {
            "constrainedEnumerable" => "public sealed class Repo<T> : IRepo<T> where T : struct { }",
            "recursiveGeneric" => "public sealed class Repo<T> : IRepo<T> { public Repo(IRepo<List<T>> next) {} }",
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
                public LazyComponent({{parameter}}) { throw new Exception("CONSTRUCTOR_MUST_NOT_RUN"); }
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
