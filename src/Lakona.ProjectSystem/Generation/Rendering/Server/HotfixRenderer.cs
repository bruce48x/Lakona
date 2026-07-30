using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;

namespace Lakona.ProjectSystem.Generation.Rendering.Server;

internal sealed class HotfixRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Hotfix/Server.Hotfix.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/Hotfix/Game/GameService.cs", RenderGameService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Game/GameSessionLifecycle.cs", RenderGameSessionLifecycle(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Game/GameWorldBehavior.cs", RenderGameWorldBehavior(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Game/GameWorldTimer.cs", RenderGameWorldTimer(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Game/GameWorldTimerArgs.cs", RenderGameWorldTimerArgs(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/HotfixStartup.cs", RenderHotfixStartup(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ServerHotfix, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="..\BuildTag.props" />

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AssemblyName>Server.Hotfix</AssemblyName>
            <RootNamespace>Server.Hotfix</RootNamespace>
            <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
            <LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>
            <LakonaHotfixProject>true</LakonaHotfixProject>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
            <ProjectReference Include="..\App\Server.App.csproj" />
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>

          <Target Name="CopyHotfixOutput" AfterTargets="Build">
            <PropertyGroup>
              <LakonaHotfixOutputDir>$(ProjectDir)..\App\bin\$(Configuration)\$(TargetFramework)\hotfix\</LakonaHotfixOutputDir>
            </PropertyGroup>
            <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(LakonaHotfixOutputDir)" />
            <Copy SourceFiles="$(TargetDir)$(AssemblyName).pdb" DestinationFolder="$(LakonaHotfixOutputDir)" Condition="Exists('$(TargetDir)$(AssemblyName).pdb')" />
            <Copy SourceFiles="$(ProjectDepsFilePath)" DestinationFolder="$(LakonaHotfixOutputDir)" Condition="Exists('$(ProjectDepsFilePath)')" />
            <WriteLinesToFile
              File="$(LakonaHotfixOutputDir)reload.signal"
              Lines="{ &quot;assembly&quot;: &quot;$(TargetFileName)&quot;, &quot;builtAtUtc&quot;: &quot;$([System.DateTime]::UtcNow.ToString('O'))&quot; }"
              Overwrite="true" />
          </Target>
        </Project>
        """;
    }

    private static string RenderGameService()
    {
        return """
        using Lakona.Game.Server;
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Server.App.Game;
        using Server.App.Generated;
        using Shared.Contracts.Game;

        namespace Server.Hotfix.Game
        {
            [HotfixService(typeof(IGameService))]
            internal sealed class GameService
            {
                private readonly ActorAccess _actors;
                private readonly ILakonaGameServer _gameServer;

                public GameService(ActorAccess actors, ILakonaGameServer gameServer)
                {
                    _actors = actors;
                    _gameServer = gameServer;
                }

                public async ValueTask<LoginReply> LoginAsync(GameServiceCall<LoginRequest> call)
                {
                    var playerName = call.Request.PlayerName?.Trim() ?? "";
                    if (playerName.Length is < 1 or > 20)
                    {
                        return new LoginReply { Success = false, Error = "Name must contain 1 to 20 characters." };
                    }

                    var reply = await _actors
                        .Startup<GameWorldActor>(GameWorldIds.Global)
                        .CallAsync(
                            static behavior => behavior.LoginAsync,
                            new GameLoginRequest
                            {
                                ConnectionId = call.ConnectionId,
                                PlayerName = playerName
                            },
                            CancellationToken.None);
                    if (!reply.Success)
                    {
                        return reply;
                    }

                    try
                    {
                        var session = await _gameServer.StartSessionAsync(playerName, call.ConnectionId);
                        await _actors
                            .Startup<GameWorldActor>(GameWorldIds.Global)
                            .PostAsync(
                                static behavior => behavior.AttachSessionAsync,
                                new GameAttachSessionRequest
                                {
                                    ConnectionId = call.ConnectionId,
                                    OwnerKey = session.OwnerKey,
                                    SessionId = session.SessionId
                                },
                                CancellationToken.None);
                    }
                    catch
                    {
                        await DisconnectAsync(call.ConnectionId);
                        throw;
                    }

                    return reply;
                }

                public ValueTask SubmitInputAsync(GameServiceCall<PlayerInput> call)
                {
                    return _actors
                        .Startup<GameWorldActor>(GameWorldIds.Global)
                        .PostAsync(
                            static behavior => behavior.SubmitInputAsync,
                            new GameInputRequest
                            {
                                ConnectionId = call.ConnectionId,
                                DirectionX = call.Request.DirectionX,
                                DirectionY = call.Request.DirectionY
                            },
                            CancellationToken.None);
                }

                private ValueTask DisconnectAsync(string connectionId)
                {
                    return _actors
                        .Startup<GameWorldActor>(GameWorldIds.Global)
                        .PostAsync(
                            static behavior => behavior.DisconnectAsync,
                            new GameDisconnectRequest { ConnectionId = connectionId },
                            CancellationToken.None);
                }
            }
        }
        """;
    }

    private static string RenderGameSessionLifecycle()
    {
        return """
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Server.App.Game;

        namespace Server.Hotfix.Game
        {
            [HotfixLifecycle(typeof(IGameSessionLifecycle))]
            internal sealed class GameSessionLifecycle
            {
                private readonly ActorAccess _actors;

                public GameSessionLifecycle(ActorAccess actors)
                {
                    _actors = actors;
                }

                public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
                {
                    if (string.IsNullOrWhiteSpace(call.Request.ConnectionId))
                    {
                        return default;
                    }

                    return _actors
                        .Startup<GameWorldActor>(GameWorldIds.Global)
                        .PostAsync(
                            static behavior => behavior.DisconnectAsync,
                            new GameDisconnectRequest { ConnectionId = call.Request.ConnectionId },
                            CancellationToken.None);
                }

                public ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
                {
                    // Player state intentionally remains in this in-memory world for name-based reconnects.
                    return default;
                }
            }
        }
        """;
    }

    private static string RenderGameWorldTimer()
    {
        return """
        using Lakona.Game.Server.Sessions;
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Lakona.Game.Server.Hotfix.Abstractions.Timers;
        using Server.App.Game;
        using Shared.Contracts.Game;

        namespace Server.Hotfix.Game
        {
            [HotfixTimer]
            public sealed partial class GameWorldTimerCallbacks
            {
                private readonly ActorAccess _actors;
                private readonly IClientNotifications _notifications;

                public GameWorldTimerCallbacks(
                    ActorAccess actors,
                    IClientNotifications notifications)
                {
                    _actors = actors;
                    _notifications = notifications;
                }

                public async ValueTask TickAsync(TimerTick<GameWorldTimerArgs> tick)
                {
                    var update = await _actors
                        .Startup<GameWorldActor>(GameWorldIds.Global)
                        .CallAsync(
                            static behavior => behavior.TickAsync,
                            new GameTickRequest(),
                            tick.CancellationToken);

                    foreach (var recipient in update.Recipients)
                    {
                        var session = new GameSessionKey(
                            recipient.OwnerKey,
                            recipient.SessionId);
                        _notifications
                            .ForSession<IGameCallback>(session)
                            .OnWorldUpdated(update.Snapshot);
                    }
                }
            }
        }
        """;
    }

    private static string RenderGameWorldTimerArgs()
    {
        return """
        namespace Server.App.Game
        {
            public sealed class GameWorldTimerArgs
            {
            }
        }
        """;
    }

    private static string RenderHotfixStartup()
    {
        return """
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Server.App.Game;

        namespace Server.Hotfix
        {
            [HotfixStartup]
            public static class HotfixStartup
            {
                [HotfixConfigureActors]
                public static void ConfigureActors(ActorHostBuilder actors)
                {
                    actors.RegisterStartup<GameWorldActor, string>(static context => context.Candidates[0]);
                }
            }
        }
        """;
    }

    private static string RenderGameWorldBehavior()
    {
        return """
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Lakona.Game.Server.Hotfix.Abstractions.Timers;
        using Server.App.Game;
        using Shared.Contracts.Game;

        namespace Server.Hotfix.Game
        {
            [HotfixBehaviorOf(typeof(GameWorldActor))]
            internal sealed partial class GameWorldBehavior
            {
                public async ValueTask<LoginReply> LoginAsync(
                    GameWorldActor self,
                    GameLoginRequest request,
                    CancellationToken cancellationToken = default)
                {
                    await EnsureSimulationTimerAsync(self, cancellationToken);

                    if (self.PlayersByConnection.ContainsKey(request.ConnectionId))
                    {
                        return new LoginReply { Success = false, Error = "This connection is already logged in." };
                    }

                    if (self.PlayersByName.TryGetValue(request.PlayerName, out var player))
                    {
                        if (player.IsOnline)
                        {
                            return new LoginReply { Success = false, Error = "That name is already in use." };
                        }
                    }
                    else
                    {
                        player = CreatePlayer(self, request.PlayerName);
                        self.PlayersByName.Add(player.Name, player);
                    }

                    player.ConnectionId = request.ConnectionId;
                    player.IsOnline = true;
                    player.InputX = 0f;
                    player.InputY = 0f;
                    self.PlayersByConnection[request.ConnectionId] = player;

                    var snapshot = BuildSnapshot(self);
                    return new LoginReply
                    {
                        Success = true,
                        PlayerId = player.PlayerId,
                        World = snapshot
                    };
                }

                public ValueTask AttachSessionAsync(
                    GameWorldActor self,
                    GameAttachSessionRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (self.PlayersByConnection.TryGetValue(request.ConnectionId, out var player) &&
                        player.IsOnline)
                    {
                        player.SessionOwnerKey = request.OwnerKey;
                        player.SessionId = request.SessionId;
                    }

                    return default;
                }

                public ValueTask SubmitInputAsync(
                    GameWorldActor self,
                    GameInputRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (!self.PlayersByConnection.TryGetValue(request.ConnectionId, out var player) ||
                        !player.IsOnline ||
                        !player.IsAlive)
                    {
                        return default;
                    }

                    var length = MathF.Sqrt(request.DirectionX * request.DirectionX + request.DirectionY * request.DirectionY);
                    if (length > 1f)
                    {
                        request.DirectionX /= length;
                        request.DirectionY /= length;
                    }

                    player.InputX = request.DirectionX;
                    player.InputY = request.DirectionY;
                    if (length > 0.001f)
                    {
                        player.DirectionX = request.DirectionX;
                        player.DirectionY = request.DirectionY;
                    }

                    return default;
                }

                public ValueTask DisconnectAsync(
                    GameWorldActor self,
                    GameDisconnectRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (!self.PlayersByConnection.Remove(request.ConnectionId, out var player) ||
                        !string.Equals(player.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
                    {
                        return default;
                    }

                    player.IsOnline = false;
                    player.ConnectionId = "";
                    player.SessionOwnerKey = "";
                    player.SessionId = "";
                    player.InputX = 0f;
                    player.InputY = 0f;
                    return default;
                }

                public ValueTask<GameWorldUpdate> TickAsync(
                    GameWorldActor self,
                    GameTickRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = request;
                    _ = cancellationToken;
                    self.Tick++;
                    self.SimulationSeconds += GameRules.SimulationStepSeconds;

                    UpdatePlayers(self);
                    SpawnMonsterIfDue(self);
                    UpdateMonsters(self);
                    UpdateBullets(self);

                    return new ValueTask<GameWorldUpdate>(new GameWorldUpdate
                    {
                        Snapshot = BuildSnapshot(self),
                        Recipients = self.PlayersByName.Values
                            .Where(static player =>
                                player.IsOnline &&
                                player.SessionId.Length > 0)
                            .Select(static player => new GameWorldRecipient
                            {
                                OwnerKey = player.SessionOwnerKey,
                                SessionId = player.SessionId
                            })
                            .ToList()
                    });
                }

                private static PlayerState CreatePlayer(GameWorldActor self, string name)
                {
                    var id = self.NextPlayerId++;
                    var position = SpawnPosition(id);
                    return new PlayerState
                    {
                        PlayerId = id,
                        Name = name,
                        X = position.X,
                        Y = position.Y,
                        NextShotSeconds = self.SimulationSeconds + GameRules.ShotIntervalSeconds
                    };
                }

                private static void UpdatePlayers(GameWorldActor self)
                {
                    foreach (var player in self.PlayersByName.Values)
                    {
                        if (!player.IsAlive)
                        {
                            if (self.SimulationSeconds >= player.RespawnAtSeconds)
                            {
                                var position = SpawnPosition(player.PlayerId + self.Tick);
                                player.X = position.X;
                                player.Y = position.Y;
                                player.Health = GameRules.PlayerMaxHealth;
                                player.IsAlive = true;
                                player.NextShotSeconds = self.SimulationSeconds + GameRules.ShotIntervalSeconds;
                            }

                            continue;
                        }

                        if (!player.IsOnline)
                        {
                            continue;
                        }

                        player.X = Math.Clamp(
                            player.X + player.InputX * GameRules.PlayerSpeed * GameRules.SimulationStepSeconds,
                            0.5f,
                            GameRules.WorldWidth - 0.5f);
                        player.Y = Math.Clamp(
                            player.Y + player.InputY * GameRules.PlayerSpeed * GameRules.SimulationStepSeconds,
                            0.5f,
                            GameRules.WorldHeight - 0.5f);

                        if (self.SimulationSeconds >= player.NextShotSeconds)
                        {
                            player.NextShotSeconds = self.SimulationSeconds + GameRules.ShotIntervalSeconds;
                            self.Bullets.Add(new BulletState
                            {
                                BulletId = self.NextBulletId++,
                                OwnerPlayerId = player.PlayerId,
                                X = player.X + player.DirectionX * 0.6f,
                                Y = player.Y + player.DirectionY * 0.6f,
                                DirectionX = player.DirectionX,
                                DirectionY = player.DirectionY
                            });
                        }
                    }
                }

                private static void SpawnMonsterIfDue(GameWorldActor self)
                {
                    if (self.SimulationSeconds < self.NextMonsterSpawnSeconds ||
                        self.Monsters.Count >= GameRules.MaxMonsters ||
                        !self.PlayersByName.Values.Any(static player => player.IsOnline && player.IsAlive))
                    {
                        return;
                    }

                    self.NextMonsterSpawnSeconds = self.SimulationSeconds + GameRules.MonsterSpawnIntervalSeconds;
                    var id = self.NextMonsterId++;
                    var edge = (int)(id % 4);
                    var fraction = ((id * 37) % 100) / 100f;
                    var monster = new MonsterState { MonsterId = id };
                    switch (edge)
                    {
                        case 0: monster.X = 0.3f; monster.Y = fraction * GameRules.WorldHeight; break;
                        case 1: monster.X = GameRules.WorldWidth - 0.3f; monster.Y = fraction * GameRules.WorldHeight; break;
                        case 2: monster.X = fraction * GameRules.WorldWidth; monster.Y = 0.3f; break;
                        default: monster.X = fraction * GameRules.WorldWidth; monster.Y = GameRules.WorldHeight - 0.3f; break;
                    }

                    self.Monsters.Add(monster);
                }

                private static void UpdateMonsters(GameWorldActor self)
                {
                    foreach (var monster in self.Monsters)
                    {
                        var target = self.PlayersByName.Values
                            .Where(static player => player.IsOnline && player.IsAlive)
                            .OrderBy(player => DistanceSquared(monster.X, monster.Y, player.X, player.Y))
                            .FirstOrDefault();
                        if (target is null)
                        {
                            continue;
                        }

                        var dx = target.X - monster.X;
                        var dy = target.Y - monster.Y;
                        var distance = MathF.Sqrt(dx * dx + dy * dy);
                        if (distance > 0.001f)
                        {
                            var step = MathF.Min(distance, GameRules.MonsterSpeed * GameRules.SimulationStepSeconds);
                            monster.X += dx / distance * step;
                            monster.Y += dy / distance * step;
                        }

                        if (distance <= 0.85f && self.SimulationSeconds >= monster.NextAttackSeconds)
                        {
                            monster.NextAttackSeconds = self.SimulationSeconds + GameRules.MonsterAttackIntervalSeconds;
                            DamagePlayer(self, target, GameRules.MonsterContactDamage, 0);
                        }
                    }
                }

                private static void UpdateBullets(GameWorldActor self)
                {
                    for (var index = self.Bullets.Count - 1; index >= 0; index--)
                    {
                        var bullet = self.Bullets[index];
                        bullet.X += bullet.DirectionX * GameRules.BulletSpeed * GameRules.SimulationStepSeconds;
                        bullet.Y += bullet.DirectionY * GameRules.BulletSpeed * GameRules.SimulationStepSeconds;
                        bullet.RemainingSeconds -= GameRules.SimulationStepSeconds;
                        if (bullet.RemainingSeconds <= 0f ||
                            bullet.X < 0f || bullet.X > GameRules.WorldWidth ||
                            bullet.Y < 0f || bullet.Y > GameRules.WorldHeight)
                        {
                            self.Bullets.RemoveAt(index);
                            continue;
                        }

                        var hitMonster = self.Monsters.FirstOrDefault(monster =>
                            DistanceSquared(bullet.X, bullet.Y, monster.X, monster.Y) <= 0.35f);
                        if (hitMonster is not null)
                        {
                            hitMonster.Health -= GameRules.BulletDamage;
                            self.Bullets.RemoveAt(index);
                            if (hitMonster.Health <= 0)
                            {
                                self.Monsters.Remove(hitMonster);
                                FindPlayer(self, bullet.OwnerPlayerId)?.Score += GameRules.MonsterKillScore;
                            }

                            continue;
                        }

                        var hitPlayer = self.PlayersByName.Values.FirstOrDefault(player =>
                            player.IsOnline && player.IsAlive && player.PlayerId != bullet.OwnerPlayerId &&
                            DistanceSquared(bullet.X, bullet.Y, player.X, player.Y) <= 0.36f);
                        if (hitPlayer is not null)
                        {
                            DamagePlayer(self, hitPlayer, GameRules.BulletDamage, bullet.OwnerPlayerId);
                            self.Bullets.RemoveAt(index);
                        }
                    }
                }

                private static void DamagePlayer(GameWorldActor self, PlayerState victim, int damage, long attackerPlayerId)
                {
                    victim.Health = Math.Max(0, victim.Health - damage);
                    if (victim.Health > 0)
                    {
                        return;
                    }

                    victim.IsAlive = false;
                    victim.InputX = 0f;
                    victim.InputY = 0f;
                    victim.RespawnAtSeconds = self.SimulationSeconds + GameRules.RespawnDelaySeconds;
                    var halfScore = (victim.Score + 1) / 2;
                    victim.Score = halfScore;
                    if (attackerPlayerId != 0 && FindPlayer(self, attackerPlayerId) is { } attacker)
                    {
                        attacker.Score += halfScore;
                    }
                }

                private static PlayerState? FindPlayer(GameWorldActor self, long playerId)
                {
                    return self.PlayersByName.Values.FirstOrDefault(player => player.PlayerId == playerId);
                }

                private static WorldSnapshot BuildSnapshot(GameWorldActor self)
                {
                    return new WorldSnapshot
                    {
                        Tick = self.Tick,
                        Width = GameRules.WorldWidth,
                        Height = GameRules.WorldHeight,
                        Players = self.PlayersByName.Values
                            .Where(static player => player.IsOnline)
                            .OrderBy(static player => player.PlayerId)
                            .Select(player => new PlayerSnapshot
                            {
                                PlayerId = player.PlayerId,
                                Name = player.Name,
                                X = player.X,
                                Y = player.Y,
                                DirectionX = player.DirectionX,
                                DirectionY = player.DirectionY,
                                Health = player.Health,
                                MaxHealth = GameRules.PlayerMaxHealth,
                                Score = player.Score,
                                IsAlive = player.IsAlive,
                                RespawnSeconds = player.IsAlive ? 0f : MathF.Max(0f, player.RespawnAtSeconds - self.SimulationSeconds)
                            })
                            .ToList(),
                        Monsters = self.Monsters.Select(monster => new MonsterSnapshot
                        {
                            MonsterId = monster.MonsterId,
                            X = monster.X,
                            Y = monster.Y,
                            Health = monster.Health,
                            MaxHealth = GameRules.MonsterMaxHealth
                        }).ToList(),
                        Bullets = self.Bullets.Select(bullet => new BulletSnapshot
                        {
                            BulletId = bullet.BulletId,
                            OwnerPlayerId = bullet.OwnerPlayerId,
                            X = bullet.X,
                            Y = bullet.Y,
                            DirectionX = bullet.DirectionX,
                            DirectionY = bullet.DirectionY
                        }).ToList()
                    };
                }

                private static async ValueTask EnsureSimulationTimerAsync(GameWorldActor self, CancellationToken cancellationToken)
                {
                    if (self.SimulationTimerId.IsValid)
                    {
                        return;
                    }

                    self.NextMonsterSpawnSeconds = self.SimulationSeconds + GameRules.MonsterSpawnIntervalSeconds;
                    self.SimulationTimerId = await LakonaTimer
                        .CreatePeriodicTimerAsync(
                            static (GameWorldTimerCallbacks callbacks) => callbacks.TickAsync,
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(GameRules.SimulationStepSeconds),
                            new GameWorldTimerArgs(),
                            cancellationToken);
                }

                private static (float X, float Y) SpawnPosition(long seed)
                {
                    var x = 2f + ((seed * 7) % 28);
                    var y = 2f + ((seed * 11) % 14);
                    return (x, y);
                }

                private static float DistanceSquared(float ax, float ay, float bx, float by)
                {
                    var dx = ax - bx;
                    var dy = ay - by;
                    return dx * dx + dy * dy;
                }
            }
        }
        """;
    }
}
