using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixDependencyIdentityTests
{
    [Fact]
    public void Incompatible_host_version_does_not_fall_back_to_private_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(HotfixDependencyIdentityTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hostDirectory = Directory.CreateDirectory(Path.Combine(root, "host")).FullName;
        try
        {
            var name = "VersionedHost_" + Guid.NewGuid().ToString("N");
            var host = Compile(hostDirectory, name, "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] public sealed class Service { }");
            using (var stream = File.OpenRead(host))
                AssemblyLoadContext.Default.LoadFromStream(stream);
            var dependency = Compile(root, name, "[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")] public sealed class Service { }");
            var hotfix = Compile(root, "Hotfix_" + Guid.NewGuid().ToString("N"), "public sealed class Entry(Service service) { }", dependency);
            var context = new HotfixAssemblyLoadContext(hotfix, []);
            try
            {
                var failure = Record.Exception(() => context.LoadFromAssemblyName(new AssemblyName(name + ", Version=2.0.0.0, Culture=neutral, PublicKeyToken=null")));
                Assert.NotNull(failure);
                Assert.DoesNotContain(context.Assemblies, assembly => assembly.GetName().Name == name);
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Published_dependency_copy_reuses_host_service_type_and_instance()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(HotfixDependencyIdentityTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var dependency = Compile(root, "RedisContract_" + suffix, """
                namespace StackExchange.Redis;
                public interface IDatabase { }
                public sealed class Database : IDatabase { }
                """);
            using var stream = File.OpenRead(dependency);
            var hostAssembly = AssemblyLoadContext.Default.LoadFromStream(stream);
            var contract = hostAssembly.GetType("StackExchange.Redis.IDatabase")!;
            var database = Activator.CreateInstance(hostAssembly.GetType("StackExchange.Redis.Database")!)!;
            using var services = new ServiceCollection().AddSingleton(contract, database).BuildServiceProvider();
            var hotfix = Compile(root, "Hotfix_" + suffix, """
                public sealed class LoginStore(StackExchange.Redis.IDatabase database)
                {
                    public object Database => database;
                }
                """, dependency);
            var context = new HotfixAssemblyLoadContext(hotfix, []);
            try
            {
                var type = context.LoadMainAssemblyFromBytes(hotfix).GetType("LoginStore")!;
                var store = ActivatorUtilities.CreateInstance(services, type);
                Assert.Same(database, type.GetProperty("Database")!.GetValue(store));
                Assert.Same(contract, type.GetConstructors().Single().GetParameters().Single().ParameterType);
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Compile(string root, string name, string source, params string[] dependencies)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(dependencies).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(root, name + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
