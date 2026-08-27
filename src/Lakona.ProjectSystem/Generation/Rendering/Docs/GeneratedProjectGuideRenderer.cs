using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Docs;

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
        {{EngineDescription(spec)}}, local cluster defaults, hotfixable rules,
        reliable business push, and a generated server-authoritative arena game.

        ## Generated Options

        {{RenderGeneratedOptions(spec)}}

        ## Build And Run

        Build the generated server first, refresh the development hotfix output,
        then run the server. The generated health endpoint reports live and ready
        status over HTTP.

        Build the generated server:

        ```powershell
        dotnet build "Server/Server.slnx"
        ```

        Refresh the development hotfix output after changing `Shared/` or `Server/Hotfix/`:

        ```powershell
        dotnet build "Server/Hotfix/Server.Hotfix.csproj"
        ```

        Run the server:

        ```powershell
        dotnet run --project "Server/App/Server.App.csproj" --no-build
        ```

        Check readiness from another terminal:

        ```powershell
        Invoke-RestMethod http://127.0.0.1:20080/_lakona/health/ready
        ```

        {{ListenerSentence(spec.Transport)}}

        {{ClientStartupSentence(spec.ClientEngine)}}

        ## Project Structure

        ```txt
        Shared/        Contracts, DTOs, RPC service interfaces, callback contracts
        Server/App/    Stable server host, actor state shells, timer DTOs, configuration
        Server/Hotfix/ Reloadable services, components, actor behaviors, actor startup, timer callbacks
        Client/        Generated client for the selected engine
        {{(spec.DeploymentProfile == DeploymentProfile.Compose ? "ops/           Deployment support files for the compose profile" : "")}}
        ```

        ## Where To Edit

        - Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push
          DTOs, and named contract ids.
        - Edit `Server/App/` for stable actor state shells, timer/request DTOs, host
          metadata, and local configuration.
        - Edit `Server/Hotfix/` for replaceable services, actor behaviors, lifecycle
          reactions, `[HotfixComponent]` helpers, actor startup, and timer callbacks.
          Every class here must declare a Hotfix role; keep data types in
          `Server/App/` or `Shared/`.
        - Edit `Client/` for the selected client UI and client-side session flow.

        ## Agent Skills

        The official Lakona Agent Skills are included under `.agents/skills/`.
        Commit this directory with the project so every developer and CI agent
        uses the guidance that matches the generated Lakona package versions.

        ## Runtime Model

        The generated project demonstrates one vertical slice:

        ```txt
        client connect
          -> framework handshake
          -> client login RPC
          -> generated hotfix-backed service binding
          -> current Server/Hotfix service implementation
          -> actor-owned in-memory game world
          -> periodic authoritative simulation
          -> server-pushed world snapshots resolved through the current game session
        ```

        The hotfix actor startup path ensures the fixed local `GameWorldActor` exists
        before login or input RPC asks it for work. The actor owns all mutable player,
        monster, bullet, health, score, online, and respawn state. No database is used;
        restarting the server clears the world.

        ## Demo Rules

        - Enter a player name. A new name receives a new server-assigned `PlayerId`;
          an offline existing name restores its in-memory state; an online name is rejected.
        - Move with WASD, the arrow keys, or a gamepad left stick. The client sends
          direction only and the server computes movement.
        - Players automatically fire in their last movement direction every 0.5 seconds.
        - A green monster spawns every 3 seconds, chases the nearest living online player,
          and deals contact damage with a cooldown.
        - Killing a monster awards 10 points. Killing a player awards half of the victim's
          pre-death score, rounded up; the victim keeps the same rounded half.
        - Dead players respawn after 5 seconds. Disconnects disappear immediately while
          their state remains available for same-name reconnects.
        - {{ProceduralArtSentence(spec.ClientEngine)}}

        Cluster, hotfix, and reliable push are part of the generated default model.

        `Server/App/appsettings.json` intentionally contains only compact source
        values. Its `Lakona:Hotfix:DebugWatcher=On` setting makes local
        `Server/Hotfix` rebuilds reload through `reload.signal`.

        Liveness is available at `http://127.0.0.1:20080/_lakona/health/live`.
        Readiness is available at `http://127.0.0.1:20080/_lakona/health/ready`
        and includes guardrail diagnostics when startup state is not ready.

        ## Actor Call Model

        `call.Actors` is the node-local actor runtime for the process currently
        executing the hotfix service. RPC services that target actors whose
        placement may change should use generated typed actor selectors instead.

        Public instance methods in sealed partial `[HotfixBehaviorOf]` classes are the actor
        API. Stable `Server/App` code owns actor state and DTOs; replaceable
        `Server/Hotfix` code owns behavior.

        ```csharp
        [HotfixBehaviorOf(typeof(GameWorldActor))]
        internal sealed partial class GameWorldBehavior
        {
            public ValueTask<LoginReply> LoginAsync(
                GameWorldActor self,
                GameLoginRequest request,
                CancellationToken cancellationToken = default)
            {
                _ = self;
                _ = request;
                _ = cancellationToken;
                return new ValueTask<LoginReply>(new LoginReply());
            }
        }
        ```

        ```csharp
        public GameService(ActorAccess actors, ILakonaGameServer gameServer)
        {
            _actors = actors;
            _gameServer = gameServer;
        }

        var reply = await _actors.Startup<GameWorldActor>(GameWorldIds.Global).CallAsync(
            static behavior => behavior.LoginAsync,
            request,
            ct);
        ```

        Use generated typed actor selectors when business code should express
        placement:

        ```csharp
        await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, ct);      // Normal business path
        await actors.Local<RoomActor>(roomId).PostAsync(static behavior => behavior.RunTickAsync, request, ct);   // Current node only after ownership is proven
        ```

        ## Client Notes

        {{ClientNotes(spec.ClientEngine)}}

        ## Configuration

        Server runtime configuration lives in `Server/App/appsettings.json`.

        {{MembershipSetup(spec.MembershipProvider)}}

        Deployment-specific production configuration should be kept outside the generated
        defaults.

        ## Tooling

        {{GitAttributesNotes(spec.ClientEngine)}}

        Create the initial deployable server zip:

        ```powershell
        lakona-tool server pack --runtime linux-x64
        ```

        Create future hotfix zips after the initial server package has shipped:

        ```powershell
        lakona-tool hotfix pack
        ```
        {{(spec.DeploymentProfile == DeploymentProfile.Compose
            ? "\nCompose deployment files were generated under `ops/` and should be reviewed before production use."
            : "")}}
        """;
    }

    private static string RenderGeneratedOptions(LakonaProjectSpec spec)
    {
        var rows = new List<string>
        {
            "| Option | Value |",
            "| --- | --- |",
            $"| Client engine | {ProjectOptionText.ToCliValue(spec.ClientEngine)} |",
            $"| Client engine version | {ClientEngineVersionText(spec)} |",
            $"| Transport | {ProjectOptionText.ToCliValue(spec.Transport)} |",
            $"| Serializer | {ProjectOptionText.ToCliValue(spec.Serializer)} |",
            $"| Cluster membership | {ProjectOptionText.ToCliValue(spec.MembershipProvider)} |"
        };

        if (spec.ClientEngine is ClientEngine.Unity or ClientEngine.Tuanjie)
        {
            rows.Add($"| NuGet for Unity source | {ProjectOptionText.ToCliValue(spec.NuGetForUnitySource)} |");
        }

        rows.Add($"| Deploy profile | {ProjectOptionText.ToCliValue(spec.DeploymentProfile)} |");
        return string.Join(Environment.NewLine, rows);
    }

    private static string MembershipSetup(MembershipProviderKind provider) => provider switch
    {
        MembershipProviderKind.Memory =>
            "This project uses in-memory Membership for local single-node development. " +
            "Choose an external provider before running more than one server node.",
        MembershipProviderKind.Postgres =>
            "This project uses PostgreSQL Membership. Before startup, apply the single repeatable " +
            "`database/postgresql/membership.sql` file shipped by the `Lakona.Game.Clustering.Postgres` package, " +
            "then replace the placeholder `LakonaClusterPostgres` connection string.",
        MembershipProviderKind.Redis =>
            "This project uses Redis Membership. Replace the placeholder `LakonaClusterRedis` connection string. " +
            "For production, enable Redis persistence and configure `maxmemory-policy noeviction`.",
        MembershipProviderKind.MySql =>
            "This project uses MySQL Membership. Before startup, apply the single repeatable " +
            "`database/mysql/membership.sql` file shipped by the `Lakona.Game.Clustering.MySql` package, " +
            "then replace the placeholder `LakonaClusterMySql` connection string.",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static string GitAttributesNotes(ClientEngine engine)
    {
        const string dotNetNotes =
            "The root `.gitattributes` normalizes .NET, C#, MSBuild, documentation, " +
            "and deployment files for cross-platform development.";

        return engine switch
        {
            ClientEngine.Unity or ClientEngine.Tuanjie =>
                dotNetNotes + "\n\n" +
                "It also configures UnityYAMLMerge for scenes and prefabs and routes large game assets " +
                "through Git LFS. Configure Git's local `unityyamlmerge` merge driver to point to the " +
                "editor's UnityYAMLMerge executable before relying on semantic scene or prefab merges. " +
                "Install Git LFS before committing LFS-managed assets:\n\n" +
                "```powershell\n" +
                "git lfs install\n" +
                "```",
            ClientEngine.Godot =>
                dotNetNotes + "\n\n" +
                "It also keeps Godot text resources mergeable and routes large or binary game assets " +
                "through Git LFS. Install Git LFS before committing those assets:\n\n" +
                "```powershell\n" +
                "git lfs install\n" +
                "```",
            ClientEngine.Console => dotNetNotes,
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
        };
    }

    private static string EngineDescription(LakonaProjectSpec spec) => spec.ClientEngine switch
    {
        ClientEngine.Unity => $"Unity {ClientEngineVersionText(spec)} client using UI Toolkit",
        ClientEngine.Tuanjie => "Tuanjie client using UI Toolkit",
        ClientEngine.Godot => "Godot C# client",
        ClientEngine.Console => ".NET console client for smoke/load flows",
        _ => throw new ArgumentOutOfRangeException(nameof(spec.ClientEngine), spec.ClientEngine, null)
    };

    private static string ClientEngineVersionText(LakonaProjectSpec spec) =>
        spec.ClientEngineVersion is { } version
            ? ProjectOptionText.ToCliValue(version)
            : "n/a";

    private static string ListenerSentence(TransportKind transport) => transport switch
    {
        TransportKind.Kcp => "The server listens on kcp://127.0.0.1:20000 by default.",
        TransportKind.Tcp => "The server listens on tcp://127.0.0.1:20000 by default.",
        TransportKind.WebSocket => "The server listens on ws://127.0.0.1:20000/ws by default.",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string ClientStartupSentence(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity or ClientEngine.Tuanjie =>
            "Open the Unity-compatible project at Client/ and run the generated Game scene.",
        ClientEngine.Godot =>
            "Open the Godot project at Client/ and run the generated Game scene.",
        ClientEngine.Console =>
            "Run the console client from Client/Client.csproj after the server is running.",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    private static string ClientNotes(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity or ClientEngine.Tuanjie =>
            "The generated Game scene contains its login and HUD structure in scene/UI Toolkit assets. " +
            "Edit Assets/Input/LakonaInputActions.inputactions to customize movement bindings. " +
            "Runtime code draws gameplay with generated textures and basic shapes. NuGetForUnity manages package dependencies.",
        ClientEngine.Godot =>
            "The Game scene and theme are file-backed. Node2D custom drawing renders gameplay " +
            "without textures, and the project uses SDK-style package references.",
        ClientEngine.Console =>
            "The console client is intended for smoke/load flows and generates no game-engine assets.",
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null)
    };

    private static string ProceduralArtSentence(ClientEngine engine) => engine switch
    {
        ClientEngine.Unity or ClientEngine.Tuanjie =>
            "Unity draws the map, players, monsters, bullets, directions, and health bars from engine primitives. No external art files are included.",
        ClientEngine.Godot =>
            "Godot draws the map, players, monsters, bullets, directions, and health bars from engine primitives. No external art files are included.",
        ClientEngine.Console =>
            "The headless console client submits movement input and prints smoke/load results without game-engine assets.",
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
