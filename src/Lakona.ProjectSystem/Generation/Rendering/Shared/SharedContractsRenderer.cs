using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Shared;

internal sealed class SharedContractsRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Shared/Contracts/RpcContractIds.cs", RenderRpcContractIds(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Shared/Contracts/Game/GameProtocols.cs", RenderGameProtocols(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Shared/Contracts/Game/GameMessages.cs", RenderGameMessages(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderRpcContractIds()
    {
        return """
        namespace Shared.Contracts
        {
            public static class RpcContractIds
            {
                public static class Services
                {
                    public const int Game = 1;
                }

                public static class GameServiceMethods
                {
                    public const int LoginAsync = 1;
                    public const int SubmitInputAsync = 2;
                }

                public static class GameNotifications
                {
                    public const int WorldUpdated = 1;
                }
            }
        }
        """;
    }

    private static string RenderGameProtocols()
    {
        return """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Game
        {
            [RpcService(RpcContractIds.Services.Game, NotificationContract = typeof(IGameCallback))]
            public interface IGameService
            {
                [RpcMethod(RpcContractIds.GameServiceMethods.LoginAsync)]
                ValueTask<LoginReply> LoginAsync(LoginRequest request);

                [RpcMethod(RpcContractIds.GameServiceMethods.SubmitInputAsync)]
                ValueTask SubmitInputAsync(PlayerInput request);
            }

            [RpcNotificationContract]
            public interface IGameCallback
            {
                [RpcNotification(RpcContractIds.GameNotifications.WorldUpdated)]
                void OnWorldUpdated(WorldSnapshot snapshot);
            }
        }
        """;
    }

    private static string RenderGameMessages(LakonaProjectSpec spec)
    {
        _ = spec;
        const string memoryPackUsing = "using MemoryPack;\n";
        const string memoryPackable = "[MemoryPackable(GenerateType.VersionTolerant)]\n    ";
        static string Order(int value) => $"[MemoryPackOrder({value})] ";

        return $$"""
        using System.Collections.Generic;
        {{memoryPackUsing}}
        namespace Shared.Contracts.Game
        {
            {{memoryPackable}}public partial class LoginRequest
            {
                {{Order(0)}}public string PlayerName { get; set; } = "";
            }

            {{memoryPackable}}public partial class LoginReply
            {
                {{Order(0)}}public bool Success { get; set; }
                {{Order(1)}}public string Error { get; set; } = "";
                {{Order(2)}}public long PlayerId { get; set; }
                {{Order(3)}}public WorldSnapshot World { get; set; } = new();
            }

            {{memoryPackable}}public partial class PlayerInput
            {
                {{Order(0)}}public float DirectionX { get; set; }
                {{Order(1)}}public float DirectionY { get; set; }
            }

            {{memoryPackable}}public partial class WorldSnapshot
            {
                {{Order(0)}}public long Tick { get; set; }
                {{Order(1)}}public float Width { get; set; }
                {{Order(2)}}public float Height { get; set; }
                {{Order(3)}}public List<PlayerSnapshot> Players { get; set; } = new();
                {{Order(4)}}public List<MonsterSnapshot> Monsters { get; set; } = new();
                {{Order(5)}}public List<BulletSnapshot> Bullets { get; set; } = new();
            }

            {{memoryPackable}}public partial class PlayerSnapshot
            {
                {{Order(0)}}public long PlayerId { get; set; }
                {{Order(1)}}public string Name { get; set; } = "";
                {{Order(2)}}public float X { get; set; }
                {{Order(3)}}public float Y { get; set; }
                {{Order(4)}}public float DirectionX { get; set; }
                {{Order(5)}}public float DirectionY { get; set; }
                {{Order(6)}}public int Health { get; set; }
                {{Order(7)}}public int MaxHealth { get; set; }
                {{Order(8)}}public int Score { get; set; }
                {{Order(9)}}public bool IsAlive { get; set; }
                {{Order(10)}}public float RespawnSeconds { get; set; }
            }

            {{memoryPackable}}public partial class MonsterSnapshot
            {
                {{Order(0)}}public long MonsterId { get; set; }
                {{Order(1)}}public float X { get; set; }
                {{Order(2)}}public float Y { get; set; }
                {{Order(3)}}public int Health { get; set; }
                {{Order(4)}}public int MaxHealth { get; set; }
            }

            {{memoryPackable}}public partial class BulletSnapshot
            {
                {{Order(0)}}public long BulletId { get; set; }
                {{Order(1)}}public long OwnerPlayerId { get; set; }
                {{Order(2)}}public float X { get; set; }
                {{Order(3)}}public float Y { get; set; }
                {{Order(4)}}public float DirectionX { get; set; }
                {{Order(5)}}public float DirectionY { get; set; }
            }
        }
        """;
    }
}
