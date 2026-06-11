using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Docs;

internal sealed class GeneratedProjectDocsRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("docs/GETTING_STARTED.md", RenderGettingStarted(spec), FileWriteMode.Replace, GeneratedFileKind.Markdown);
        builder.AddFile("docs/EDITING_GUIDE.md", RenderEditingGuide(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
        builder.AddFile("docs/OPERATIONS.md", RenderOperations(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
    }

    private static string RenderGettingStarted(LakonaProjectSpec spec)
    {
        return $$"""
        # {{spec.Layout.GeneratedDocsTitle}}

        ## Run The Server

        ```powershell
        dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check
        dotnet run --project "Server/App/Server.App.csproj" --no-build
        ```
        """;
    }

    private static string RenderEditingGuide()
    {
        return """
        # Editing Guide

        Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push DTOs, and named contract ids.

        Edit `Server/App/` for stable orchestration, actor state, host binding, and runtime integration.

        Edit `Server/Hotfix/` for replaceable rules and services.
        """;
    }

    private static string RenderOperations()
    {
        return """
        # Operations

        Keep production configuration outside the generated defaults. The generated `Server/App/appsettings.json` intentionally stays compact.
        """;
    }
}
