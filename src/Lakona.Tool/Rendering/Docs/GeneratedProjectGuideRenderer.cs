using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Docs;

internal sealed class GeneratedProjectGuideRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("README.md", RenderReadme(spec), FileWriteMode.Replace, GeneratedFileKind.Markdown);
        builder.AddFile("AGENTS.md", RenderAgents(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
        builder.AddFile("CLAUDE.md", RenderClaude(), FileWriteMode.Replace, GeneratedFileKind.Markdown);
    }

    private static string RenderReadme(LakonaProjectSpec spec)
    {
        return $$"""
        # {{spec.Name}}

        ## Project Overview

        This is a generated Lakona.Game project with server host, shared contracts,
        {{EngineDescription(spec.ClientEngine)}}, local cluster defaults, hotfixable rules,
        reliable business push, and a generated login and chat vertical slice.

        ## Generated Options

        | Option | Value |
        | --- | --- |
        | Client engine | {{ToolEnumText.ToCliValue(spec.ClientEngine)}} |
        | Transport | {{ToolEnumText.ToCliValue(spec.Transport)}} |
        | Serializer | {{ToolEnumText.ToCliValue(spec.Serializer)}} |
        | Persistence | {{ToolEnumText.ToCliValue(spec.Persistence)}} |
        | NuGet for Unity source | {{ToolEnumText.ToCliValue(spec.NuGetForUnitySource)}} |
        | Deploy profile | {{ToolEnumText.ToCliValue(spec.DeploymentProfile)}} |

        ## Build And Run

        Use the check command first to build the server and print the derived Lakona runtime
        state. After the check succeeds, run the server normally.

        Check the generated server:

        ```powershell
        dotnet run --project "Server/App/Server.App.csproj" -- --lakona-game-check
        ```

        Run the server:

        ```powershell
        dotnet run --project "Server/App/Server.App.csproj" --no-build
        ```

        {{ListenerSentence(spec.Transport)}}

        {{ClientStartupSentence(spec.ClientEngine)}}

        ## Project Structure

        ```txt
        Shared/        Contracts, DTOs, RPC service interfaces, callback contracts
        Server/App/    Stable server host, actor state shells, configuration
        Server/Hotfix/ Reloadable services, actor behaviors, lifecycle reactions, feature declarations
        Client/        Generated client for the selected engine
        {{(spec.DeploymentProfile == DeploymentProfile.Compose ? "ops/           Deployment support files for the compose profile" : "")}}
        ```

        ## Where To Edit

        - Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push
          DTOs, and named contract ids.
        - Edit `Server/App/` for stable actor state shells, host metadata, and local
          configuration.
        - Edit `Server/Hotfix/` for replaceable services, actor behaviors, lifecycle
          reactions, and feature declarations.
        - Edit `Client/` for the selected client UI and client-side session flow.

        ## Runtime Model

        The generated project demonstrates one vertical slice:

        ```txt
        client connect
          -> framework handshake
          -> client login RPC
          -> generated hotfix-backed service binding
          -> current Server/Hotfix service implementation
          -> actor-backed chat room state
          -> reliable callback or notification
        ```

        The Hotfix feature declaration ensures the fixed local ChatRoomActor exists
        before business RPC asks it for work.

        Cluster, hotfix, and reliable push are part of the generated default model.

        `Server/App/appsettings.json` intentionally contains only compact source values.

        Derived runtime state is shown through the `--lakona-game-check` command.

        ## Actor Call Model

        `call.Actors` is the node-local actor runtime for the process currently
        executing the hotfix service. The generated chat vertical slice is a
        single-node starter, so it names this dependency `starterNodeLocalActors`.
        This is not a remote actor call. RPC services that target actors whose
        placement may change should use generated typed actor selectors instead.

        ```csharp
        var starterNodeLocalActors = call.Actors;
        var reply = await starterNodeLocalActors.AskAsync<ChatRoomActor, LoginReply>(
            roomId,
            (room, ct) => room.LoginAsync(connectionId, playerName, callback));
        ```

        Use generated typed actor selectors when business code should express
        placement:

        ```csharp
        await rooms.Get(roomId).JoinAsync(request, ct);            // Local first, then route through ActorDirectory
        await rooms.Local(roomId).JoinAsync(request, ct);          // Current node only
        await rooms.Remote(nodeId, roomId).JoinAsync(request, ct); // Specific remote node
        ```

        ## Client Notes

        {{ClientNotes(spec.ClientEngine)}}

        ## Configuration

        Server runtime configuration lives in `Server/App/appsettings.json`.

        Deployment-specific production configuration should be kept outside the generated
        defaults.

        ## Tooling

        `lakona-tool hotfix pack` can package the hotfix project after the server and hotfix
        projects build.
        {{(spec.DeploymentProfile == DeploymentProfile.Compose
            ? "\nCompose deployment files were generated under `ops/` and should be reviewed before production use."
            : "")}}
        """;
    }

    private static string EngineDescription(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity => "Unity 2022.3 client using UI Toolkit",
        ClientEngine.UnityCn => "Unity 2022.3 China-friendly client using UI Toolkit",
        ClientEngine.Tuanjie => "Tuanjie client using UI Toolkit",
        ClientEngine.Godot => "Godot C# client",
        ClientEngine.Console => ".NET console client for smoke/load flows",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    private static string ListenerSentence(TransportKind transport) => transport switch
    {
        TransportKind.Kcp => "The server listens on kcp://127.0.0.1:20000 by default.",
        TransportKind.Tcp => "The server listens on tcp://127.0.0.1:20000 by default.",
        TransportKind.WebSocket => "The server listens on ws://127.0.0.1:20000/ws by default.",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string ClientStartupSentence(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity or ClientEngine.UnityCn or ClientEngine.Tuanjie =>
            "Open the Unity-compatible project at Client/ and run the generated login scene.",
        ClientEngine.Godot =>
            "Open the Godot project at Client/ and run the generated login scene.",
        ClientEngine.Console =>
            "Run the console client from Client/Client.csproj after the server is running.",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    private static string ClientNotes(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity or ClientEngine.UnityCn or ClientEngine.Tuanjie =>
            "The generated client uses UI Toolkit for UI. NuGetForUnity manages package " +
            "dependencies. Unity-generated package folders and editor caches are ignored by Git.",
        ClientEngine.Godot =>
            "Scenes and theme resources are file-backed. The generated Godot project uses " +
            "SDK-style package references.",
        ClientEngine.Console =>
            "The console client is intended for smoke/load flows and generates no game-engine assets.",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    private static string RenderAgents()
    {
        return """
        # Agent Instructions

        Before doing anything else, read [README.md](README.md) -- it is the single authority for this generated project.
        """;
    }

    private static string RenderClaude()
    {
        return """
        # Claude Instructions

        Before doing anything else, read [README.md](README.md) -- it is the single authority for this generated project.
        """;
    }
}
