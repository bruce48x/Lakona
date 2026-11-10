using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Operations;

internal sealed class OperationsRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        if (spec.DeploymentProfile != DeploymentProfile.Compose)
        {
            return;
        }

        builder.AddFile("Server/Dockerfile", RenderDockerfile(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("docker-compose.cluster.yml", RenderCompose(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".env.cluster.example", RenderEnv(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile(".dockerignore", "**/bin\n**/obj\n.git\nClient\n", FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("ops/CLUSTER_OPERATIONS.md", RenderOperationsGuide(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
    }

    private static string RenderDockerfile()
    {
        return """
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src
        COPY . .
        RUN dotnet publish Server/App/Server.App.csproj -c Release -o /app --disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false
        RUN dotnet publish Server/Hotfix/Server.Hotfix.csproj -c Release -o /app/hotfix --disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false

        FROM mcr.microsoft.com/dotnet/aspnet:10.0
        WORKDIR /app
        COPY --from=build /app .
        ENTRYPOINT ["dotnet", "Server.App.dll"]
        """;
    }

    private static string RenderCompose(LakonaProjectSpec spec)
    {
        var protocol = spec.Transport == TransportKind.Kcp ? "udp" : "tcp";
        return $$"""
        services:
          server:
            build:
              context: .
              dockerfile: Server/Dockerfile
            env_file:
              - .env.cluster.example
            environment:
              Lakona__Endpoints__0__Host: "0.0.0.0"
              Lakona__Endpoints__0__Port: "${LAKONA_CLIENT_PORT:-20000}"
            ports:
              - "127.0.0.1:${LAKONA_CLIENT_PORT:-20000}:${LAKONA_CLIENT_PORT:-20000}/{{protocol}}"
        """;
    }

    private static string RenderEnv()
    {
        return """
        Lakona__Node__Id=dev-1
        Lakona__Endpoints__0__AdvertisedHost=127.0.0.1
        """;
    }

    private static string RenderOperationsGuide()
    {
        return """
        # Operations

        This generated compose profile is for local development. Production topology, node discovery, persistence, and deployment policy belong in your own operations workflow.

        From the generated project root:

        ```sh
        docker compose -f docker-compose.cluster.yml up --build -d
        docker compose -f docker-compose.cluster.yml logs -f server
        docker compose -f docker-compose.cluster.yml down
        ```

        Connect the client to 127.0.0.1:20000 (WebSocket uses /ws). KCP publishes
        UDP; TCP and WebSocket publish TCP. Set LAKONA_CLIENT_PORT in the shell
        or a Compose --env-file to change both host and container ports together.
        KCP handshake responses carry the listener port, so keep both ports equal.
        Only the local machine can reach the published business port.
        Management HTTP remains on container loopback and is not published.

        The image publishes App and Hotfix separately, including Hotfix runtime
        dependencies, and uses the generated development hotfix directory mode.
        Host bin/obj output is excluded from the build context. Rebuild the image
        after code changes; this profile does not mount live source or implement
        production versioned Hotfix deployment.

        .env.cluster.example uses .NET configuration keys for node identity and
        the advertised client host. Binding is overridden only by Compose; local
        dotnet runs retain their loopback defaults. The default in-memory Membership
        needs no external services. If an external provider was selected, configure
        its connection string for a reachable database before starting; this profile
        does not provision a database or a multi-node cluster.
        """;
    }
}
