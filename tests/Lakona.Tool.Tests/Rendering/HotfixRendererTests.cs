using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Server;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class HotfixRendererTests
{
    [Fact]
    public void AddFiles_EmitsServerAuthoritativeArenaSlice()
    {
        var plan = Render();

        var service = AssertPath(plan, "Server/Hotfix/Game/GameService.cs").Content;
        Assert.Contains("[HotfixService(typeof(IGameService))]", service, StringComparison.Ordinal);
        Assert.Contains("Name must contain 1 to 20 characters", service, StringComparison.Ordinal);
        Assert.Contains("GameWorldBehavior.Entries.LoginAsync", service, StringComparison.Ordinal);
        Assert.Contains("GameWorldBehavior.Entries.SubmitInputAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetWorldAsync", service, StringComparison.Ordinal);
        Assert.Contains("StartSessionAsync", service, StringComparison.Ordinal);
        Assert.Contains("GameWorldBehavior.Entries.AttachSessionAsync", service, StringComparison.Ordinal);

        var behavior = AssertPath(plan, "Server/Hotfix/Game/GameWorldBehavior.cs").Content;
        Assert.Contains("[HotfixBehaviorOf(typeof(GameWorldActor))]", behavior, StringComparison.Ordinal);
        Assert.Contains("internal sealed partial class GameWorldBehavior", behavior, StringComparison.Ordinal);
        Assert.Contains("That name is already in use.", behavior, StringComparison.Ordinal);
        Assert.Contains("This connection is already logged in.", behavior, StringComparison.Ordinal);
        Assert.Contains("player.IsOnline = false", behavior, StringComparison.Ordinal);
        Assert.Contains("MonsterSpawnIntervalSeconds", behavior, StringComparison.Ordinal);
        Assert.Contains("UpdateMonsters", behavior, StringComparison.Ordinal);
        Assert.Contains("UpdateBullets", behavior, StringComparison.Ordinal);
        Assert.Contains("DamagePlayer", behavior, StringComparison.Ordinal);
        Assert.Contains("var halfScore = (victim.Score + 1) / 2", behavior, StringComparison.Ordinal);
        Assert.Contains("attacker.Score += halfScore", behavior, StringComparison.Ordinal);
        Assert.Contains("RespawnAtSeconds", behavior, StringComparison.Ordinal);
        Assert.Contains("Where(static player => player.IsOnline)", behavior, StringComparison.Ordinal);
        Assert.Contains("GameWorldTimerCallbacks.Entries.TickAsync", behavior, StringComparison.Ordinal);
        Assert.DoesNotContain("self.Tick % 2", behavior, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameCallback? Callback", behavior, StringComparison.Ordinal);

        var timer = AssertPath(plan, "Server/Hotfix/Game/GameWorldTimer.cs").Content;
        Assert.Contains("[HotfixTimer]", timer, StringComparison.Ordinal);
        Assert.Contains("IClientNotifications", timer, StringComparison.Ordinal);
        Assert.Contains("OnWorldUpdated", timer, StringComparison.Ordinal);
        Assert.Contains("ForSession<IGameCallback>(session)", timer, StringComparison.Ordinal);
        Assert.Contains("OnWorldUpdated(update.Snapshot);", timer, StringComparison.Ordinal);
        Assert.DoesNotContain("OnWorldUpdated(update.Snapshot, tick.CancellationToken)", timer, StringComparison.Ordinal);
        Assert.DoesNotContain("class GameWorldTimerArgs", timer, StringComparison.Ordinal);

        var timerArgs = AssertPath(plan, "Server/App/Game/GameWorldTimerArgs.cs").Content;
        Assert.Contains("class GameWorldTimerArgs", timerArgs, StringComparison.Ordinal);
        Assert.Contains("namespace Server.App.Game", timerArgs, StringComparison.Ordinal);

        var lifecycle = AssertPath(plan, "Server/Hotfix/Game/GameSessionLifecycle.cs").Content;
        Assert.Contains("SessionDisconnectedAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("GameWorldBehavior.Entries.DisconnectAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Player state intentionally remains", lifecycle, StringComparison.Ordinal);

        Assert.Contains("TimerTick<GameWorldTimerArgs>", timer, StringComparison.Ordinal);
        Assert.Contains("GameWorldBehavior.Entries.TickAsync", timer, StringComparison.Ordinal);

        var startup = AssertPath(plan, "Server/Hotfix/HotfixStartup.cs").Content;
        Assert.Contains("actors.RegisterStartup<GameWorldActor, string>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixConfigureServices", startup, StringComparison.Ordinal);

        var project = AssertPath(plan, "Server/Hotfix/Server.Hotfix.csproj").Content;
        Assert.Contains("<LakonaHotfixProject>true</LakonaHotfixProject>", project, StringComparison.Ordinal);
        Assert.Contains("<CompilerVisibleProperty Include=\"LakonaHotfixProject\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Chat", string.Join('\n', plan.Files.Select(file => file.RelativePath)), StringComparison.Ordinal);
    }

    private static GenerationPlan Render()
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions("MyGame", ".", ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack, PersistenceKind.None, NuGetForUnitySource.OpenUpm, DeploymentProfile.None));
        var builder = new GenerationPlanBuilder("Root");
        new HotfixRenderer().AddFiles(spec, builder);
        return builder.Build();
    }

    private static GeneratedFile AssertPath(GenerationPlan plan, string relativePath) =>
        Assert.Single(plan.Files, file => file.RelativePath == relativePath);
}
