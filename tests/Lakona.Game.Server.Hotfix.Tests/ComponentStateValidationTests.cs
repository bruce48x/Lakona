using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ComponentStateValidationTests
{
    [Theory]
    [InlineData("public void Initialize() { }")]
    [InlineData("private Task LoadAsync() => Task.CompletedTask;")]
    [InlineData("public static void Refresh() { }")]
    [InlineData("public void Dispose() { }")]
    [InlineData("public override string ToString() => \"state\";")]
    [InlineData("public static State operator +(State left, State right) => left;")]
    [InlineData("public int Value => 1;")]
    [InlineData("public int this[int index] { get => index; set { } }")]
    public void Runtime_rejects_executable_State_members_without_analyzers(string member)
    {
        var assembly = Compile("[ComponentState] public sealed class State { " + member + " }");
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() => ComponentStateRegistration.Register(services, [assembly]));
        Assert.Contains("Component state", error.Message);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("IDisposable", "public void Dispose() { }")]
    [InlineData("IDisposable", "void IDisposable.Dispose() { }")]
    [InlineData("IAsyncDisposable", "public async ValueTask DisposeAsync() { await Task.Yield(); }")]
    [InlineData("IAsyncDisposable", "ValueTask IAsyncDisposable.DisposeAsync() => default;")]
    public void Runtime_accepts_data_and_actual_disposal_contracts(string contract, string method)
    {
        var assembly = Compile("[ComponentState] public sealed class State : " + contract
            + " { public State() { Count = 1; } public int Count { get; private set; } " + method + " }");
        var services = new ServiceCollection();
        ComponentStateRegistration.Register(services, [assembly]);
        var descriptor = Assert.Single(services, item => item.ServiceType.Name == "State");
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void Runtime_rejects_inherited_business_methods()
    {
        var assembly = Compile("public class Base { public void Refresh() { } } [ComponentState] public sealed class State : Base { }");
        Assert.Throws<InvalidOperationException>(() => ComponentStateRegistration.Register(new ServiceCollection(), [assembly]));
    }

    private static Assembly Compile(string declaration)
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using System.Reflection;
            using Lakona.Game.Server.Hotfix.Abstractions;
            [assembly: AssemblyMetadata("LakonaProjectRole", "ServerApp")]
            """ + declaration;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ComponentStateRegistration).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("StateValidation" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
