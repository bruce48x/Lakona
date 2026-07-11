using System.Text.Json;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Server;

internal sealed class ServerAppRenderer : IPlanContributor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Server.slnx", RenderSolution(), FileWriteMode.Replace, GeneratedFileKind.Solution);
        builder.AddFile("Server/App/BuildTag.props", RenderBuildTagProps(), FileWriteMode.Replace, GeneratedFileKind.Project);
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
          <Import Project="BuildTag.props" />

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
            <CompilerVisibleProperty Include="LakonaRpcGenerateServer" />
            <CompilerVisibleProperty Include="LakonaRpcServerGeneratedNamespace" />
            <CompilerVisibleProperty Include="LakonaHotfixGenerateStableRpcServices" />
          </ItemGroup>

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
        _ = spec;
        return """
        using Lakona.Game.Server.Hosting;

        return await LakonaGameServer.RunAsync(args);
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
                internal IGameCallback? Callback;
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
                public const float MonsterSpeed = 2f;
                public const float BulletSpeed = 12f;
                public const float BulletLifetimeSeconds = 2.5f;
                public const float ShotIntervalSeconds = 0.5f;
                public const float MonsterSpawnIntervalSeconds = 3f;
                public const float MonsterAttackIntervalSeconds = 1f;
                public const float RespawnDelaySeconds = 5f;
                public const int MaxMonsters = 50;
            }

            public sealed class GameLoginRequest
            {
                public string ConnectionId { get; set; } = "";
                public string PlayerName { get; set; } = "";
                public IGameCallback Callback { get; set; } = null!;
            }

            public sealed class GameInputRequest
            {
                public string ConnectionId { get; set; } = "";
                public float DirectionX { get; set; }
                public float DirectionY { get; set; }
            }

            public sealed class GameDisconnectRequest
            {
                public string ConnectionId { get; set; } = "";
            }

            public sealed class GameSnapshotRequest
            {
            }

            public sealed class GameTickRequest
            {
            }
        }
        """;
    }

    private static string RenderAppSettings(LakonaProjectSpec spec)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["Transport"] = ToolEnumText.ToCliValue(spec.Transport),
            ["Serializer"] = ToolEnumText.ToCliValue(spec.Serializer),
            ["Host"] = "127.0.0.1",
            ["Port"] = 20000,
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
                    ["Cleanup"] = new Dictionary<string, object?>
                    {
                        ["DisconnectedRetentionSeconds"] = 30
                    }
                },
                ["Hotfix"] = new Dictionary<string, object?>
                {
                    ["DebugWatcher"] = "On"
                },
                ["Health"] = new Dictionary<string, object?>
                {
                    ["Http"] = new Dictionary<string, object?>
                    {
                        ["Enabled"] = true,
                        ["Host"] = "127.0.0.1",
                        ["Port"] = 20080,
                        ["RequireLoopback"] = true
                    }
                },
                ["Observability"] = new Dictionary<string, object?>
                {
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

        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
