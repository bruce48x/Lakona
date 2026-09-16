using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixStartupValidationTests
{
    [Theory]
    [InlineData("public static int Configure(IServiceCollection services) => 1;")]
    [InlineData("public static async void Configure(IServiceCollection services) { await Task.Yield(); }")]
    [InlineData("public static void Configure<T>(IServiceCollection services) {}")]
    [InlineData("public static void Configure(ref IServiceCollection services) {}")]
    [InlineData("public static void Configure(in IServiceCollection services) {}")]
    [InlineData("private static void Configure(IServiceCollection services) {}")]
    [InlineData("public static void Configure() {}")]
    [InlineData("[HotfixConfigureActors] public static void Configure(IServiceCollection services) {}")]
    [InlineData("public static void Configure(IServiceCollection services) {} [HotfixConfigureServices] public static void Second(IServiceCollection services) {}")]
    public void Invalid_services_entry_prevents_both_configuration_methods_from_running(string method)
    {
        Scan("""
            [HotfixStartup] public static class Startup
            {
                [HotfixConfigureActors] public static void Actors(ActorHostBuilder actors) => throw new Exception("CONFIGURATION_EXECUTED");
                [HotfixConfigureServices]
            """ + method + "}");
    }

    [Fact]
    public void Invalid_actors_entry_prevents_services_configuration_from_running()
    {
        Scan("""
            [HotfixStartup] public static class Startup
            {
                [HotfixConfigureActors] public static int Actors(ActorHostBuilder actors) => 1;
                [HotfixConfigureServices] public static void Services(IServiceCollection services) => throw new Exception("CONFIGURATION_EXECUTED");
            }
            """);
    }

    [Theory]
    [InlineData("[HotfixStartup] public static class Startup<T> {}")]
    [InlineData("public class Container<T> { [HotfixStartup] public static class Startup {} }")]
    public void Rejects_open_generic_roots(string declaration) => Scan(declaration);

    private static void Scan(string declaration)
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Microsoft.Extensions.DependencyInjection;
            """ + "\n" + declaration;
        var compilation = CSharpCompilation.Create("StartupValidation" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            HotfixTestMetadataReferences.CreateDefaultReferences(typeof(HotfixStartupAttribute), typeof(IServiceCollection)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("startup-validation", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var result = HotfixBehaviorScanner.Scan(assembly);
            Assert.False(result.Succeeded);
            Assert.Empty(result.ActorStartups);
            Assert.Empty(result.StartupServices);
            Assert.NotEmpty(result.Diagnostics);
            Assert.DoesNotContain(result.Diagnostics, message => message.Contains("CONFIGURATION_EXECUTED", StringComparison.Ordinal));
        }
        finally
        {
            context.Unload();
        }
    }
}
