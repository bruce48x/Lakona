using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Lakona.Actor.SourceGenerator.Generators;

namespace Lakona.Actor.Tests;

public sealed class ActorClientGeneratorTests
{
    [Fact]
    public void Generator_reports_invalid_actor_client_shapes()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            [ActorClient]
            internal interface IInternalClient
            {
                ValueTask Send();
            }

            [ActorClient]
            public interface IGenericClient<T>
            {
                ValueTask Send();
            }

            [ActorClient]
            public interface IInvalidClient
            {
                ValueTask Send(int value);

                ValueTask Send(string value);

                Task InvalidReturn();

                ValueTask Generic<T>();

                ValueTask RefArg(ref int value);
            }
            """;

        Diagnostic[] diagnostics = GetGeneratorDiagnostics(source);

        Assert.Equal(
            [
                ActorClientGenerator.NonPublicInterfaceDiagnosticId,
                ActorClientGenerator.GenericInterfaceDiagnosticId,
                ActorClientGenerator.MethodOverloadDiagnosticId,
                ActorClientGenerator.MethodOverloadDiagnosticId,
                ActorClientGenerator.UnsupportedReturnTypeDiagnosticId,
                ActorClientGenerator.GenericMethodDiagnosticId,
                ActorClientGenerator.RefParameterDiagnosticId
            ],
            diagnostics.Select(static diagnostic => diagnostic.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Generator_does_not_report_valid_actor_client()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            [ActorClient]
            public interface IValidClient
            {
                ValueTask Send(int value);

                ValueTask<int> Query();
            }
            """;

        Diagnostic[] diagnostics = GetGeneratorDiagnostics(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Generator_uses_actor_call_options_for_request_response_methods()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            [ActorClient]
            public interface IRoomClient
            {
                ValueTask<int> Count();
            }
            """;

        string generatedSource = GetGeneratedSource(source);

        Assert.Contains("global::Lakona.Actor.ActorCallOptions callOptions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("actor.Call<", generatedSource, StringComparison.Ordinal);
        Assert.Contains("new RoomClientCountRequest(), callOptions)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.TimeSpan callTimeout", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_spawn_generator_emits_async_spawn_extensions()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            public sealed class RoomActor : IActor<RoomMessage>
            {
                public ValueTask OnMessage(ActorContext<RoomMessage> ctx, RoomMessage message)
                {
                    return ValueTask.CompletedTask;
                }
            }

            public sealed record RoomMessage;
            """;

        string generatedSource = GetTypedSpawnGeneratedSource(source);

        Assert.Contains("ValueTask<global::Lakona.Actor.ActorHandle<global::RoomMessage>> SpawnRoomActorAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("return system.SpawnAsync<global::RoomMessage>(actor, options);", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(" SpawnRoomActor(", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_implements_inherited_actor_client_methods()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            public interface IBaseClient
            {
                ValueTask<int> Query(int value);
            }

            [ActorClient]
            public interface IInheritedClient : IBaseClient
            {
                ValueTask Send(string value);
            }
            """;

        (string generatedSource, Diagnostic[] diagnostics) = GetGeneratedSourceAndCompilationDiagnostics(source);

        Assert.Contains("InheritedClientQueryRequest", generatedSource, StringComparison.Ordinal);
        Assert.Contains(" Query(", generatedSource, StringComparison.Ordinal);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Generator_escapes_keyword_method_and_parameter_names()
    {
        const string source = """
            using System.Threading.Tasks;
            using Lakona.Actor;

            [ActorClient]
            public interface IKeywordClient
            {
                ValueTask @class(int @event);
            }
            """;

        (string generatedSource, Diagnostic[] diagnostics) = GetGeneratedSourceAndCompilationDiagnostics(source);

        Assert.Contains(" @class(", generatedSource, StringComparison.Ordinal);
        Assert.Contains(" @event", generatedSource, StringComparison.Ordinal);
        Assert.Empty(diagnostics);
    }

    private static Diagnostic[] GetGeneratorDiagnostics(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ActorClientGeneratorTests",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ActorClientGenerator());
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out ImmutableArray<Diagnostic> diagnostics);

        return diagnostics
            .Where(static diagnostic => diagnostic.Id.StartsWith("ULA", StringComparison.Ordinal))
            .OrderBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetGeneratedSource(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ActorClientGeneratorOutputTests",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ActorClientGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();

        return Assert.Single(result.Results)
            .GeneratedSources
            .Single(static generated => generated.HintName == "LakonaActorClientExtensions.g.cs")
            .SourceText
            .ToString();
    }

    private static string GetTypedSpawnGeneratedSource(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TypedSpawnGeneratorOutputTests",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TypedActorSpawnGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();

        return Assert.Single(result.Results)
            .GeneratedSources
            .Single(static generated => generated.HintName == "LakonaActorTypedActorSpawnExtensions.g.cs")
            .SourceText
            .ToString();
    }

    private static (string GeneratedSource, Diagnostic[] Diagnostics) GetGeneratedSourceAndCompilationDiagnostics(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ActorClientGeneratorCompilationTests",
            [syntaxTree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ActorClientGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updatedCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        string generatedSource = Assert.Single(result.Results)
            .GeneratedSources
            .Single(static generated => generated.HintName == "LakonaActorClientExtensions.g.cs")
            .SourceText
            .ToString();
        Diagnostic[] diagnostics = generatorDiagnostics
            .Concat(updatedCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return (generatedSource, diagnostics);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        string trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            string.Empty;

        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }

        yield return MetadataReference.CreateFromFile(typeof(ActorSystem).Assembly.Location);
    }
}
