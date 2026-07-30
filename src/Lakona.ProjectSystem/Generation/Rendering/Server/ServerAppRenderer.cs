using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;

namespace Lakona.ProjectSystem.Generation.Rendering.Server;

internal sealed class ServerAppRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Server.slnx", RenderSolution(), FileWriteMode.Replace, GeneratedFileKind.Solution);
        builder.AddFile("Server/BuildTag.props", RenderBuildTagProps(), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Server.App.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Program.cs", RenderProgram(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/appsettings.json", RenderAppSettings(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Server/App/Game/GameWorldActor.cs", RenderGameWorldActor(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Game/GameWorldMessages.cs", RenderGameWorldMessages(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderSolution()
    {
        return """
        <Solution>
          <Project Path="../Shared/Shared.csproj" />
          <Project Path="App/Server.App.csproj" />
          <Project Path="Hotfix/Server.Hotfix.csproj" />
        </Solution>
        """;
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ServerApp, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="..\BuildTag.props" />

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Server.App</RootNamespace>
            <AssemblyName>Server.App</AssemblyName>
            <BuildInParallel>false</BuildInParallel>
            <RestoreBuildInParallel>false</RestoreBuildInParallel>
            <LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
            <LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
            <LakonaHotfixGenerateStableRpcServices>true</LakonaHotfixGenerateStableRpcServices>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>

          <ItemGroup>
            <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>

          <ItemGroup>
            <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
              <_Parameter1>LakonaHotfixBuildTag</_Parameter1>
              <_Parameter2>$(LakonaHotfixBuildTag)</_Parameter2>
            </AssemblyAttribute>
            <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
              <_Parameter1>Server.Hotfix</_Parameter1>
            </AssemblyAttribute>
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderBuildTagProps()
    {
        return """
        <Project>
          <PropertyGroup>
            <LakonaHotfixBuildTag>20260711.001</LakonaHotfixBuildTag>
          </PropertyGroup>
        </Project>
        """;
    }

    private static string RenderProgram(LakonaProjectSpec spec)
    {
        var transportUsing = spec.Transport switch
        {
            TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
            TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
            TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Transport, null)
        };
        var serializerUsing = spec.Serializer switch
        {
            SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
            SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Serializer, null)
        };
        var transportRegistration = spec.Transport switch
        {
            TransportKind.Tcp => """
                .RegisterEndpointTransport("tcp", static endpoint =>
                    new TcpConnectionAcceptor(endpoint.Port, endpoint.Host))
            """,
            TransportKind.Kcp => """
                .RegisterEndpointTransport("kcp", static endpoint =>
                    new KcpConnectionAcceptor(endpoint.Port, endpoint.Host))
            """,
            TransportKind.WebSocket => """
                .RegisterEndpointTransport("websocket", static async (endpoint, cancellationToken) =>
                    await WsConnectionAcceptor.CreateAsync(
                        endpoint.Port,
                        string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
                        endpoint.Host,
                        cancellationToken).ConfigureAwait(false))
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Transport, null)
        };
        var serializerName = ProjectOptionText.ToCliValue(spec.Serializer);
        var endpointSerializer = spec.Serializer switch
        {
            SerializerKind.Json => "new JsonRpcSerializer()",
            SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Serializer, null)
        };
        return $$"""
        using Lakona.Game.Server.Hosting;
        {{serializerUsing}}
        {{transportUsing}}

        return await LakonaGameServer.RunAsync(args, static server => server
        {{transportRegistration}}
            .RegisterEndpointSerializer("{{serializerName}}", static () => {{endpointSerializer}}));
        """;
    }

    private static string RenderGameWorldActor()
    {
        return """
        using System;
        using System.Collections.Generic;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions.Timers;
        using Shared.Contracts.Game;

        namespace Server.App.Game
        {
            internal sealed class GameWorldActor : Actor<string>
            {
                internal readonly Dictionary<string, PlayerState> PlayersByName = new(StringComparer.OrdinalIgnoreCase);
                internal readonly Dictionary<string, PlayerState> PlayersByConnection = new(StringComparer.Ordinal);
                internal readonly List<MonsterState> Monsters = new();
                internal readonly List<BulletState> Bullets = new();
                internal long NextPlayerId = 1;
                internal long NextMonsterId = 1;
                internal long NextBulletId = 1;
                internal long Tick;
                internal float SimulationSeconds;
                internal float NextMonsterSpawnSeconds;
                internal TimerId SimulationTimerId;
            }

            internal sealed class PlayerState
            {
                internal long PlayerId;
                internal string Name = "";
                internal string ConnectionId = "";
                internal string SessionOwnerKey = "";
                internal string SessionId = "";
                internal float X;
                internal float Y;
                internal float DirectionX = 1f;
                internal float DirectionY;
                internal float InputX;
                internal float InputY;
                internal int Health = GameRules.PlayerMaxHealth;
                internal int Score;
                internal bool IsAlive = true;
                internal bool IsOnline;
                internal float NextShotSeconds;
                internal float RespawnAtSeconds;
            }

            internal sealed class MonsterState
            {
                internal long MonsterId;
                internal float X;
                internal float Y;
                internal int Health = GameRules.MonsterMaxHealth;
                internal float NextAttackSeconds;
            }

            internal sealed class BulletState
            {
                internal long BulletId;
                internal long OwnerPlayerId;
                internal float X;
                internal float Y;
                internal float DirectionX;
                internal float DirectionY;
                internal float RemainingSeconds = GameRules.BulletLifetimeSeconds;
            }
        }
        """;
    }

    private static string RenderGameWorldMessages()
    {
        return """
        using System.Collections.Generic;
        using MemoryPack;
        using Shared.Contracts.Game;

        namespace Server.App.Game
        {
            public static class GameWorldIds
            {
                public const string Global = "game-world/global";
            }

            public static class GameRules
            {
                public const float WorldWidth = 32f;
                public const float WorldHeight = 18f;
                public const float SimulationStepSeconds = 0.05f;
                public const int PlayerMaxHealth = 100;
                public const int MonsterMaxHealth = 50;
                public const int BulletDamage = 20;
                public const int MonsterContactDamage = 10;
                public const int MonsterKillScore = 10;
                public const float PlayerSpeed = 5f;
                public const float MonsterSpeed = 1.25f;
                public const float BulletSpeed = 12f;
                public const float BulletLifetimeSeconds = 2.5f;
                public const float ShotIntervalSeconds = 0.5f;
                public const float MonsterSpawnIntervalSeconds = 3f;
                public const float MonsterAttackIntervalSeconds = 1f;
                public const float RespawnDelaySeconds = 5f;
                public const int MaxMonsters = 50;
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameLoginRequest
            {
                [MemoryPackOrder(0)]
                public string ConnectionId { get; set; } = "";

                [MemoryPackOrder(1)]
                public string PlayerName { get; set; } = "";
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameAttachSessionRequest
            {
                [MemoryPackOrder(0)]
                public string ConnectionId { get; set; } = "";

                [MemoryPackOrder(1)]
                public string OwnerKey { get; set; } = "";

                [MemoryPackOrder(2)]
                public string SessionId { get; set; } = "";
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameInputRequest
            {
                [MemoryPackOrder(0)]
                public string ConnectionId { get; set; } = "";

                [MemoryPackOrder(1)]
                public float DirectionX { get; set; }

                [MemoryPackOrder(2)]
                public float DirectionY { get; set; }
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameDisconnectRequest
            {
                [MemoryPackOrder(0)]
                public string ConnectionId { get; set; } = "";
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameTickRequest
            {
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameWorldUpdate
            {
                [MemoryPackOrder(0)]
                public WorldSnapshot Snapshot { get; set; } = new();

                [MemoryPackOrder(1)]
                public List<GameWorldRecipient> Recipients { get; set; } = new();
            }

            [MemoryPackable(GenerateType.VersionTolerant)]
            public sealed partial class GameWorldRecipient
            {
                [MemoryPackOrder(0)]
                public string OwnerKey { get; set; } = "";

                [MemoryPackOrder(1)]
                public string SessionId { get; set; } = "";
            }
        }
        """;
    }

    private static string RenderAppSettings(LakonaProjectSpec spec)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["Transport"] = ProjectOptionText.ToCliValue(spec.Transport),
            ["Serializer"] = ProjectOptionText.ToCliValue(spec.Serializer),
            ["Host"] = "127.0.0.1",
            ["Port"] = 20000,
            ["ReliablePush"] = true,
            ["RpcServices"] = new[] { "game" }
        };
        if (spec.Transport == TransportKind.WebSocket)
        {
            endpoint["Path"] = "/ws";
        }

        var settings = new Dictionary<string, object?>
        {
            ["Lakona"] = new Dictionary<string, object?>
            {
                ["Node"] = new Dictionary<string, object?>
                {
                    ["Id"] = "dev-1"
                },
                ["ActorHosts"] = new[] { "gameWorld" },
                ["Sessions"] = new Dictionary<string, object?>
                {
                    ["ResumeWindowSeconds"] = 60
                },
                ["Hotfix"] = new Dictionary<string, object?>
                {
                    ["DebugWatcher"] = "On"
                },
                ["Management"] = new Dictionary<string, object?>
                {
                    ["Http"] = new Dictionary<string, object?>
                    {
                        ["Host"] = "127.0.0.1",
                        ["Port"] = 20080
                    }
                },
                ["Health"] = new Dictionary<string, object?>
                {
                    ["Enabled"] = true,
                    ["RequireLoopback"] = true
                },
                ["Observability"] = new Dictionary<string, object?>
                {
                    ["LocalAdmin"] = new Dictionary<string, object?>
                    {
                        ["Enabled"] = true,
                        ["RequireLoopback"] = true
                    },
                    ["Logging"] = new Dictionary<string, object?>
                    {
                        ["Categories"] = new Dictionary<string, object?>
                        {
                            ["Lakona.Game.Hotfix"] = "Information",
                            ["Lakona.Rpc.Server.Request"] = "Debug"
                        }
                    }
                },
                ["Endpoints"] = new[] { endpoint }
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(settings, ServerAppJsonContext.Default.AppSettings);
    }
}
