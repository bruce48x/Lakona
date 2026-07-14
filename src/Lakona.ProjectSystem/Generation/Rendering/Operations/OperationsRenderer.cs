using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Operations;

internal sealed class OperationsRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        if (spec.DeploymentProfile != DeploymentProfile.Compose)
        {
            return;
        }

        builder.AddFile("Server/Dockerfile", RenderDockerfile(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("docker-compose.cluster.yml", RenderCompose(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".env.cluster.example", RenderEnv(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("ops/CLUSTER_OPERATIONS.md", RenderOperationsGuide(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
    }

    private static string RenderDockerfile()
    {
        return """
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src
        COPY . .
        RUN dotnet publish Server/App/Server.App.csproj -c Release -o /app

        FROM mcr.microsoft.com/dotnet/aspnet:10.0
        WORKDIR /app
        COPY --from=build /app .
        ENTRYPOINT ["dotnet", "Server.App.dll"]
        """;
    }

    private static string RenderCompose()
    {
        return """
        services:
          server:
            build:
              context: .
              dockerfile: Server/Dockerfile
            env_file:
              - .env.cluster.example
            ports:
              - "20000:20000"
        """;
    }

    private static string RenderEnv(LakonaProjectSpec spec)
    {
        var endpointScheme = spec.Transport == TransportKind.WebSocket ? "ws" : ToolEnumText.ToCliValue(spec.Transport);
        var path = spec.Transport == TransportKind.WebSocket ? "/ws" : "";
        return $$"""
        LAKONA_CLUSTER_NAME=local-dev
        LAKONA_NODE_ID=dev-1
        LAKONA_CLUSTER_ADVERTISED_ENDPOINTS_CLIENT={{endpointScheme}}://server:20000{{path}}
        """;
    }

    private static string RenderOperationsGuide()
    {
        return """
        # Operations

        This generated compose profile is for local development. Production topology, node discovery, persistence, and deployment policy belong in your own operations workflow.
        """;
    }
}
