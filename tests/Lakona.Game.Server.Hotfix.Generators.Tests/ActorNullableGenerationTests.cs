using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class ActorNullableGenerationTests
{
    [Theory]
    [InlineData(NullableContextOptions.Disable)]
    [InlineData(NullableContextOptions.Enable)]
    public void Actor_output_compiles_without_warnings_regardless_of_project_nullable_context(NullableContextOptions context)
    {
        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            """
            using Lakona.Game.Server.Actors;
            namespace Game.Server;
            public sealed class UserActor : Actor<long> { }
            public sealed class Request { }
            public sealed class Reply { }
            """,
            """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;
            namespace Game.Hotfix;
            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<Reply> CallAsync(UserActor self, Request request) => new(new Reply());
                public ValueTask NotifyAsync(UserActor self, Request request) => default;
            }
            """,
            appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix",
            nullableContextOptions: context);

        Assert.NotEmpty(result.Hotfix.GeneratedSource);
        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.DoesNotContain(result.Hotfix.CompilationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
    }
}
