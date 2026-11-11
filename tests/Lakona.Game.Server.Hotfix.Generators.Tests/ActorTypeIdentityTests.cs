using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class ActorTypeIdentityTests
{
    [Theory]
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("int?", "int?")]
    [InlineData("IdentityOuter<int>", "IdentityOuter<int>")]
    [InlineData("IdentityOuter<int>.Leaf", "IdentityOuter<int>.Leaf")]
    [InlineData("int[]", "int")]
    [InlineData("int", "int[]")]
    [InlineData("int[,]", "int[,]")]
    [InlineData("int[,,]", "int[,,]")]
    [InlineData("int[][]", "int[][]")]
    [InlineData("int[][,]", "int[][,]")]
    [InlineData("string[]", "string[]")]
    [InlineData("IdentityOuter<int>[]", "IdentityOuter<int>[]")]
    [InlineData("List<int[]>", "List<int[]>")]
    [InlineData("Dictionary<string, int[,]>", "Dictionary<string, int[,]>")]
    [InlineData("IdentityOuter<int>.Inner<string>", "IdentityOuter<int>.Inner<string>")]
    [InlineData("IdentityOuter<int>.Inner<string>.Deep<long[]>", "IdentityOuter<int>.Inner<string>.Deep<long[]>")]
    [InlineData("IdentityOuter<int[]>.Inner<string[,]>.Leaf[]", "IdentityOuter<int[]>.Inner<string[,]>.Leaf[]")]
    [InlineData("int[]", null)]
    public void Generated_method_key_and_id_resolve_the_runtime_descriptor(string request, string? result)
    {
        var returnType = result is null ? "ValueTask" : $"ValueTask<{result}>";
        var source = $$"""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Generators.Tests;
            [HotfixBehaviorOf(typeof(TypeIdentityActor))]
            public sealed partial class IdentityBehavior
            {
                public {{returnType}} EchoAsync(TypeIdentityActor self, {{request}} request) => default;
            }
            """;
        AssertGeneratedMethodsResolve(source, 1);
    }

    [Fact]
    public void Array_rank_overloads_keep_distinct_resolvable_ids()
    {
        AssertGeneratedMethodsResolve("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Generators.Tests;
            [HotfixBehaviorOf(typeof(TypeIdentityActor))]
            public sealed partial class IdentityBehavior
            {
                public ValueTask<int> EchoAsync(TypeIdentityActor self, int[] request) => default;
                public ValueTask<int> EchoAsync(TypeIdentityActor self, int[,] request) => default;
                public ValueTask<int> EchoAsync(TypeIdentityActor self, int[,,] request) => default;
            }
            """, 3);
    }

    private static void AssertGeneratedMethodsResolve(string source, int expectedCount)
    {
        var generated = GeneratorTestHost.RunAndEmit(source);
        Assert.Empty(generated.Result.ErrorDiagnostics);
        var scan = HotfixBehaviorScanner.Scan(generated.Assembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        Assert.Equal(expectedCount, scan.ActorMethods.Count);
        using var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods);
        var metadata = generated.Assembly.GetTypes().Single(type => type.Name == "TypeIdentityActorApiMetadata");
        var fields = metadata.GetFields(BindingFlags.Public | BindingFlags.Static);
        var ids = fields.Where(field => field.Name.EndsWith("_MethodId", StringComparison.Ordinal))
            .Select(field => (ulong)field.GetRawConstantValue()!).ToArray();
        var keys = fields.Where(field => field.Name.EndsWith("_MethodKey", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!).ToArray();
        Assert.Equal(expectedCount, ids.Length);
        Assert.Equal(expectedCount, ids.Distinct().Count());
        Assert.Equal(expectedCount, keys.Length);
        foreach (var id in ids)
        {
            Assert.True(table.TryResolveActorMethod(id, out var descriptor), $"Generated method ID {id} was not registered.");
            Assert.Contains(descriptor!.MethodKey, keys);
        }
    }
}

// Stable App-side types: only the generated Behavior is loaded from the emitted assembly.
public sealed class TypeIdentityActor : Actor<string>;
public class IdentityOuter<T>
{
    public class Leaf;
    public class Inner<U>
    {
        public class Deep<V>;
        public class Leaf;
    }
}
