# Hotfix Actor Behavior Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce the mandatory Lakona.Game rule that stable `Server.App` actors store state only and game behavior lives in `Server.Hotfix`.

**Architecture:** Keep the actor runtime general, but make the hotfix authoring model strict through documentation, sample shape, and a compile-time analyzer in `Lakona.Game.Server.Hotfix.Generators`. Refactor the Agar sample so stable state stores are only dispatch bridges and all actor business methods become hotfix Behavior extension methods.

**Tech Stack:** C# 13, .NET 10, Roslyn analyzers/source generators, xUnit, Lakona.Game actor runtime, Lakona.Game hotfix dispatch.

---

## Required Reading

Read these files before editing code:

- `CONTRIBUTING.md`
- `docs/game/hotfix-architecture.md`
- `docs/game/hotfix-actor-behavior-boundary.md`
- `docs/game/actor-kernel-boundary.md`
- `docs/game/remote-actor-messaging.md`

The non-negotiable rule is: hotfix is mandatory for Lakona.Game server projects. Do not add "hotfix disabled" conditions or fallback paths.

## File Structure

Modify these framework files:

- `src/Lakona.Game.Server.Hotfix.Generators/HotfixGeneratorDiagnostics.cs` adds `ULGHOTFIX011`.
- `src/Lakona.Game.Server.Hotfix.Generators/HotfixActorBoundaryAnalyzer.cs` adds the analyzer.
- `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixActorBoundaryAnalyzerTests.cs` tests analyzer behavior.
- `tests/Lakona.Game.Server.Hotfix.Generators.Tests/AnalyzerTestHost.cs` runs the analyzer in tests.

Do not modify `src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj` for this work. It already references `Microsoft.CodeAnalysis.CSharp` and packs the output as an analyzer.

Modify these Agar sample files:

- `samples/Game.Unity.Agar/Server/App/State/Users/UserActor.cs`
- `samples/Game.Unity.Agar/Server/App/State/Sessions/PlayerSessionActor.cs`
- `samples/Game.Unity.Agar/Server/App/State/Matchmaking/MatchmakingActor.cs`
- `samples/Game.Unity.Agar/Server/App/State/Matchmaking/MatchmakingQueuePolicy.cs`
- `samples/Game.Unity.Agar/Server/App/State/Rooms/RoomActor.cs`
- `samples/Game.Unity.Agar/Server/App/State/Leaderboard/LeaderboardActor.cs`
- `samples/Game.Unity.Agar/Server/App/State/StateStores.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Users/UserBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Sessions/PlayerSessionBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardBehavior.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingQueuePolicy.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardRankingPolicy.cs`
- `samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardPeriodPolicy.cs`
- `samples/Game.Unity.Agar/Server/App/Realtime/RoomRuntime.cs`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/LeaderboardGrainTests.cs`
- `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/MatchmakingQueuePolicyTests.cs`

Do not move client-facing DTOs out of `samples/Game.Unity.Agar/Shared`.
Do not redesign Agar state DTOs while doing this migration; move behavior
without changing stable state semantics.

### Task 1: Add Analyzer Diagnostic Metadata

**Files:**
- Modify: `src/Lakona.Game.Server.Hotfix.Generators/HotfixGeneratorDiagnostics.cs`

- [ ] **Step 1: Add the descriptor**

Add this descriptor after `UnsupportedNotificationContract`:

```csharp
public static readonly DiagnosticDescriptor ActorMustNotDeclareBusinessMethod = new DiagnosticDescriptor(
    "ULGHOTFIX011",
    "Stable actor must not declare business methods",
    "Actor '{0}' declares method '{1}' in the stable app; move behavior to a [HotfixBehaviorOf] class in Server.Hotfix",
    "Lakona.Game.Hotfix",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

- [ ] **Step 2: Build the generator project**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet build "src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj"
```

Expected: build succeeds.

### Task 2: Implement Stable Actor Boundary Analyzer

**Files:**
- Create: `src/Lakona.Game.Server.Hotfix.Generators/HotfixActorBoundaryAnalyzer.cs`

- [ ] **Step 1: Create the analyzer**

Create `HotfixActorBoundaryAnalyzer.cs` with this implementation shape:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotfixActorBoundaryAnalyzer : DiagnosticAnalyzer
    {
        private const string ActorMetadataName = "Lakona.Game.Server.Actors.Actor";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (!DerivesFromActor(type))
            {
                return;
            }

            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsAllowedLifecycleOverride(member))
                {
                    continue;
                }

                var location = member.Locations.FirstOrDefault(static item => item.IsInSource);
                if (location is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    HotfixGeneratorDiagnostics.ActorMustNotDeclareBusinessMethod,
                    location,
                    type.ToDisplayString(),
                    member.Name));
            }
        }

        private static bool DerivesFromActor(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                var original = current.OriginalDefinition;
                if (string.Equals(original.ToDisplayString(), ActorMetadataName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedLifecycleOverride(IMethodSymbol method)
        {
            if (!method.IsOverride)
            {
                return false;
            }

            if (method.Name is not "OnActivateAsync" and not "OnDeactivateAsync")
            {
                return false;
            }

            if (method.Parameters.Length != 1)
            {
                return false;
            }

            return string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                "System.Threading.CancellationToken",
                StringComparison.Ordinal)
                && string.Equals(
                    method.ReturnType.ToDisplayString(),
                    "System.Threading.Tasks.ValueTask",
                    StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 2: Build the generator project**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet build "src/Lakona.Game.Server.Hotfix.Generators/Lakona.Game.Server.Hotfix.Generators.csproj"
```

Expected: build succeeds.

### Task 3: Add Analyzer Tests

**Files:**
- Create: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/AnalyzerTestHost.cs`
- Create: `tests/Lakona.Game.Server.Hotfix.Generators.Tests/HotfixActorBoundaryAnalyzerTests.cs`

- [ ] **Step 1: Create the analyzer test host**

Create `AnalyzerTestHost.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

internal static class AnalyzerTestHost
{
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Lakona.Game.Server.Actors.Actor).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new HotfixActorBoundaryAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }
}
```

- [ ] **Step 2: Create analyzer tests**

Create `HotfixActorBoundaryAnalyzerTests.cs`:

```csharp
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixActorBoundaryAnalyzerTests
{
    [Fact]
    public async Task Reports_actor_business_methods()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class UserActor : Actor
            {
                public Task<int> LoginAsync(string password)
                {
                    return Task.FromResult(1);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ULGHOTFIX011", diagnostic.Id);
    }

    [Fact]
    public async Task Allows_state_and_lifecycle_hooks()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class RoomActor : Actor
            {
                internal readonly Dictionary<string, string> Members = new();

                protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }

                protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_private_and_static_helpers_on_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;

            public sealed class MatchmakingActor : Actor
            {
                private static int NormalizeRoomSize(int size)
                {
                    return size <= 0 ? 4 : size;
                }

                private int GetScore()
                {
                    return 10;
                }
            }
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("ULGHOTFIX011", diagnostic.Id));
    }
}
```

- [ ] **Step 3: Run analyzer tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet test "tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj"
```

Expected: all tests pass.

### Task 4: Refactor Agar User Actor

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/App/State/Users/UserActor.cs`
- Create: `samples/Game.Unity.Agar/Server/Hotfix/State/Users/UserBehavior.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/State/StateStores.cs`

- [ ] **Step 1: Replace `UserActor.cs` with stable state only**

Replace `samples/Game.Unity.Agar/Server/App/State/Users/UserActor.cs` with this stable state file:

```csharp
using Agar.Sample.State.Contracts.Users;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Users;

public sealed class UserState
{
    public string UserId { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public int LoginCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastLoginAtUtc { get; set; }

    public bool IsOnline { get; set; }

    public int WinCount { get; set; }

    public int VictoryPoints { get; set; }
}

public sealed class UserActor : Actor
{
    internal bool RecordExists;
    internal UserState State = new();
}
```

This removes all ordinary methods from the actor. It also changes the state
fields from private to internal so the hotfix Behavior can access them without
runtime reflection.

- [ ] **Step 2: Create `UserBehavior.cs`**

Create `samples/Game.Unity.Agar/Server/Hotfix/State/Users/UserBehavior.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Users;

[HotfixBehaviorOf(typeof(UserActor))]
public static class UserBehavior
{
    public static ValueTask<UserLoginResult> LoginAsync(this UserActor self, string password, bool reconnect)
    {
        var userId = self.Context.Id.Value;
        var passwordHash = ComputePasswordHash(password);
        var now = DateTime.UtcNow;

        if (!self.RecordExists)
        {
            self.State = new UserState
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAtUtc = now
            };
            self.RecordExists = true;
        }
        else if (!string.Equals(self.State.PasswordHash, passwordHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid password.");
        }

        if (!reconnect || string.IsNullOrWhiteSpace(self.State.SessionToken))
        {
            self.State.SessionToken = Guid.NewGuid().ToString("N");
        }

        self.State.LoginCount += 1;
        self.State.LastLoginAtUtc = now;
        self.State.IsOnline = true;

        return new ValueTask<UserLoginResult>(new UserLoginResult
        {
            UserId = self.State.UserId,
            SessionToken = self.State.SessionToken,
            LoginCount = self.State.LoginCount,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints)
        });
    }

    public static ValueTask<UserProfileSnapshot> GetProfileAsync(this UserActor self)
    {
        return new ValueTask<UserProfileSnapshot>(new UserProfileSnapshot
        {
            UserId = self.State.UserId,
            LoginCount = self.State.LoginCount,
            CreatedAtUtc = self.State.CreatedAtUtc,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            IsOnline = self.State.IsOnline,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints)
        });
    }

    public static ValueTask SetOnlineAsync(this UserActor self, bool isOnline)
    {
        if (self.RecordExists)
        {
            self.State.IsOnline = isOnline;
        }

        return default;
    }

    public static ValueTask AddWinAsync(this UserActor self)
    {
        if (self.RecordExists)
        {
            self.State.WinCount = Math.Max(0, self.State.WinCount + 1);
        }

        return default;
    }

    public static ValueTask AddVictoryPointsAsync(this UserActor self, int points)
    {
        if (self.RecordExists && points > 0)
        {
            self.State.VictoryPoints = Math.Max(0, self.State.VictoryPoints + points);
        }

        return default;
    }

    public static ValueTask ResetVictoryPointsAsync(this UserActor self)
    {
        if (self.RecordExists)
        {
            self.State.VictoryPoints = 0;
        }

        return default;
    }

    private static string ComputePasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 3: Change state store dispatch**

Each `ActorUserStateStore` method must call `HotfixDispatch.Invoke` inside the actor turn. Use this pattern:

```csharp
return runtime.AskAsync<UserActor, UserLoginResult>(
    UserId(userId),
    async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask<UserLoginResult>>(
        "LoginAsync",
        actor,
        [typeof(string), typeof(bool)],
        [password, reconnect]).ConfigureAwait(false)).AsTask();
```

For `TellAsync` methods returning `ValueTask`, use:

```csharp
return runtime.TellAsync<UserActor>(
    UserId(userId),
    async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask>(
        "SetOnlineAsync",
        actor,
        [typeof(bool)],
        [isOnline]).ConfigureAwait(false)).AsTask();
```

- [ ] **Step 4: Build the Agar server app**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet build "samples/Game.Unity.Agar/Server/App/Server.App.csproj"
```

Expected: build succeeds after all user state store calls are converted.

### Task 5: Refactor Agar Session, Room, Matchmaking, And Leaderboard Actors

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/App/State/Sessions/PlayerSessionActor.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/State/Rooms/RoomActor.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/State/Matchmaking/MatchmakingActor.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/State/Leaderboard/LeaderboardActor.cs`
- Move: `samples/Game.Unity.Agar/Server/App/State/Matchmaking/MatchmakingQueuePolicy.cs` to `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingQueuePolicy.cs`
- Create: `samples/Game.Unity.Agar/Server/Hotfix/State/Sessions/PlayerSessionBehavior.cs`
- Create: `samples/Game.Unity.Agar/Server/Hotfix/State/Rooms/RoomBehavior.cs`
- Create: `samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs`
- Create: `samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardBehavior.cs`
- Modify: `samples/Game.Unity.Agar/Server/App/State/StateStores.cs`

- [ ] **Step 1: Expose stable state fields to hotfix behaviors**

Convert private actor state fields that behaviors need into internal fields.
Use these exact names so the Behavior files read consistently:

```txt
PlayerSessionActor: internal bool RecordExists; internal PlayerSessionState State = new();
RoomActor: internal bool RecordExists; internal RoomState State = new();
MatchmakingActor: internal const int DefaultRoomSize = 10; internal readonly IPlayerSessionStateStore Sessions; internal readonly IRoomStateStore Rooms; internal readonly BattleRuntimeGatewayResolver RuntimeGateways; internal bool RecordExists; internal MatchmakingState State = new();
LeaderboardActor: internal readonly TimeZoneInfo LeaderboardTimeZone = TimeZoneInfo.Local; internal LeaderboardState State = new();
```

Preserve constructor injection in `MatchmakingActor`, but only assign the
injected services to internal readonly fields. The constructor must not contain
matchmaking decisions.

Keep the current `MatchmakingState` shape. Do not replace it with separate
`Tickets` or `NextTicketSequence` fields. `QueueId`, `DefaultRoomSize`,
`PendingTickets`, `LastMatchId`, `LastRoomId`, and `LastUpdatedAtUtc` remain on
`MatchmakingState`.

- [ ] **Step 2: Move each actor method to the matching Behavior**

For each actor, move every listed method body into a same-named Behavior
extension method. Preserve method names and parameter lists so stable bridges
can dispatch by method name.

Use this namespace mapping:

```txt
Agar.Sample.State.Sessions.PlayerSessionActor -> Server.Hotfix.State.Sessions.PlayerSessionBehavior
Agar.Sample.State.Rooms.RoomActor -> Server.Hotfix.State.Rooms.RoomBehavior
Agar.Sample.State.Matchmaking.MatchmakingActor -> Server.Hotfix.State.Matchmaking.MatchmakingBehavior
Agar.Sample.State.Leaderboard.LeaderboardActor -> Server.Hotfix.State.Leaderboard.LeaderboardBehavior
```

Use this method mapping:

```txt
PlayerSessionBehavior:
  AttachAsync(this PlayerSessionActor self, PlayerSessionAttachRequest request) -> ValueTask<PlayerSessionSnapshot>
  ReconnectAsync(this PlayerSessionActor self, PlayerSessionReconnectRequest request) -> ValueTask<PlayerSessionSnapshot>
  MarkQueuedAsync(this PlayerSessionActor self, PlayerSessionQueueRequest request) -> ValueTask<PlayerSessionSnapshot>
  ClearQueueAsync(this PlayerSessionActor self, PlayerSessionQueueClearRequest request) -> ValueTask<PlayerSessionSnapshot>
  AssignRoomAsync(this PlayerSessionActor self, PlayerRoomAssignment request) -> ValueTask<PlayerSessionSnapshot>
  ClearRoomAsync(this PlayerSessionActor self, PlayerRoomClearRequest request) -> ValueTask<PlayerSessionSnapshot>
  MarkDisconnectedAsync(this PlayerSessionActor self, PlayerSessionDisconnectRequest request) -> ValueTask<PlayerSessionSnapshot>
  HeartbeatAsync(this PlayerSessionActor self, PlayerSessionHeartbeatRequest request) -> ValueTask<PlayerSessionSnapshot>
  GetSnapshotAsync(this PlayerSessionActor self) -> ValueTask<PlayerSessionSnapshot>
  EnsureState, BuildSnapshot, NormalizeUserId, NormalizeUtc, EnsureReconnectToken, CloneGateway as private static helpers

RoomBehavior:
  CreateAsync(this RoomActor self, RoomCreateRequest request) -> ValueTask<RoomSettlementResult>
  JoinAsync(this RoomActor self, PlayerRoomAssignment request) -> ValueTask<RoomSettlementResult>
  LeaveAsync(this RoomActor self, RoomPlayerLeaveRequest request) -> ValueTask<RoomSettlementResult>
  SetReadyAsync(this RoomActor self, RoomPlayerReadyRequest request) -> ValueTask<RoomSettlementResult>
  StartAsync(this RoomActor self, RoomStartRequest request) -> ValueTask<RoomSettlementResult>
  CompleteAsync(this RoomActor self, RoomMatchCompletion request) -> ValueTask<RoomSettlementResult>
  GetSnapshotAsync(this RoomActor self) -> ValueTask<RoomSnapshot>
  EnsureState, EnsureInitialized, UpsertPlayer, FindPlayer, FindOrCreatePlayer, BuildSnapshot, BuildFailure, BuildSuccess, NormalizeRoomId, NormalizeRoomSize, NormalizeUtc, CloneGateway as private static helpers

MatchmakingBehavior:
  EnqueueAsync(this MatchmakingActor self, MatchmakingEnqueueRequest request) -> ValueTask<MatchmakingEnqueueResult>
  CancelAsync(this MatchmakingActor self, MatchmakingCancelRequest request) -> ValueTask<MatchmakingCancelResult>
  GetStatusAsync(this MatchmakingActor self) -> ValueTask<MatchmakingStatusSnapshot>
  TickAsync(this MatchmakingActor self, MatchmakingTickRequest request) -> ValueTask
  TryMatchAsync(this MatchmakingActor self, DateTime nowUtc, bool allowExpiredPartialBatch) -> ValueTask<Dictionary<string, RoomAssignment>>
  ResolveRuntimeGatewayAsync(this MatchmakingActor self, IReadOnlyList<MatchmakingQueueTicket> batch) -> ValueTask<GatewayEndpointDescriptor?>
  All queue filtering, bot fill, room creation, session assignment, and gateway resolution helpers as private static helpers

LeaderboardBehavior:
  GetLeaderboardAsync(this LeaderboardActor self, int topN) -> ValueTask<LeaderboardSnapshot>
  ResetWeeklyIfNeededAsync(this LeaderboardActor self) -> ValueTask
  RecordVictoryPointsAsync(this LeaderboardActor self, string playerId, int victoryPoints, int winCount) -> ValueTask
  GetRankedEntries, EnsurePeriodInitialized as private static helpers
```

Remove `LeaderboardActor.OnActivateAsync`. Leaderboard period state is
initialized lazily by `LeaderboardBehavior.GetLeaderboardAsync`,
`LeaderboardBehavior.ResetWeeklyIfNeededAsync`, and
`LeaderboardBehavior.RecordVictoryPointsAsync` through the moved
`EnsurePeriodInitialized` helper.

- [ ] **Step 3: Convert return types**

Behavior methods must return `ValueTask` or `ValueTask<T>`. Convert
`Task.FromResult(value)` to `new ValueTask<T>(value)` and
`Task.CompletedTask` to `default`.

- [ ] **Step 4: Move policy helpers**

Move `MatchmakingQueuePolicy` to
`samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingQueuePolicy.cs`.
Extract `LeaderboardRankingPolicy` and `LeaderboardPeriodPolicy` from
`LeaderboardActor.cs` into
`samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardRankingPolicy.cs`
and
`samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard/LeaderboardPeriodPolicy.cs`.
Update namespaces and using directives so only hotfix code and tests reference
these policy types.

- [ ] **Step 5: Convert all state store actor calls**

Every `ActorPlayerSessionStateStore`, `ActorMatchmakingStateStore`, `ActorRoomStateStore`, and `ActorLeaderboardStateStore` call must use `HotfixDispatch.Invoke` inside `runtime.AskAsync` or `runtime.TellAsync`.

Example for a one-argument result call:

```csharp
return runtime.AskAsync<RoomActor, RoomSettlementResult>(
    ActorId.From(roomId),
    async (actor, _) => await HotfixDispatch.Invoke<RoomActor, ValueTask<RoomSettlementResult>>(
        "CreateAsync",
        actor,
        [typeof(RoomCreateRequest)],
        [request]).ConfigureAwait(false)).AsTask();
```

Example for a zero-argument result call:

```csharp
return runtime.AskAsync<RoomActor, RoomSnapshot>(
    ActorId.From(roomId),
    async (actor, _) => await HotfixDispatch.Invoke<RoomActor, ValueTask<RoomSnapshot>>(
        "GetSnapshotAsync",
        actor,
        [],
        []).ConfigureAwait(false)).AsTask();
```

- [ ] **Step 6: Build both Agar server projects**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet build "samples/Game.Unity.Agar/Server/App/Server.App.csproj"
dotnet build "samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj"
```

Expected: both builds succeed.

### Task 6: Remove Stable Hotfix Fallbacks From Agar Room Runtime

**Files:**
- Modify: `samples/Game.Unity.Agar/Server/App/Realtime/RoomRuntime.cs`

- [ ] **Step 1: Remove fallback state**

Delete the `_hotfixFallbackLogged` field.

- [ ] **Step 2: Make tick fail fast when hotfix dispatch is missing**

Replace `TickSimulation` with:

```csharp
private ArenaStepResult TickSimulation(float deltaTime)
{
    return _simulation.TickWithHotfix(deltaTime);
}
```

- [ ] **Step 3: Make settlement fail fast when hotfix dispatch is missing**

Replace `SettleMatch` with:

```csharp
private MatchSettlementResult SettleMatch(WorldState worldState)
{
    return _simulation.SettleMatch(worldState);
}
```

- [ ] **Step 4: Delete stable duplicate settlement logic**

Delete `CreateStableSettlement`, `LogHotfixFallback`, and
`NormalizeRankingMass` from `RoomRuntime.cs`. Remove any unused `using`
directives after the deletion.

- [ ] **Step 5: Build the Agar server app**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet build "samples/Game.Unity.Agar/Server/App/Server.App.csproj"
```

Expected: build succeeds.

### Task 7: Update Agar Tests

**Files:**
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/LeaderboardGrainTests.cs`
- Modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/MatchmakingQueuePolicyTests.cs`

- [ ] **Step 1: Let tests compile against `Server.Hotfix`**

In `BusinessLogic.Tests.csproj`, replace:

```xml
<ProjectReference Include="../../Server/Hotfix/Server.Hotfix.csproj" ReferenceOutputAssembly="false" />
```

with:

```xml
<ProjectReference Include="../../Server/Hotfix/Server.Hotfix.csproj" />
```

Keep the analyzer reference to
`src/Lakona.Game.Server.Hotfix.Generators` unchanged.

- [ ] **Step 2: Update leaderboard policy tests**

`LeaderboardRankingPolicy` and `LeaderboardPeriodPolicy` move to
`Server.Hotfix.State.Leaderboard`. Update the test using directives:

```csharp
using Agar.Sample.State.Leaderboard;
using Server.Hotfix.State.Leaderboard;
using Xunit;
```

Keep the tests focused on the same policy methods:

```csharp
var ranked = LeaderboardRankingPolicy.GetRankedEntries(players);
var periodStart = LeaderboardPeriodPolicy.GetCurrentPeriodStartLocalDate(utcNow, ChinaTimeZone);
var nextResetUtc = LeaderboardPeriodPolicy.GetNextPeriodStartUtc(utcNow, ChinaTimeZone);
var archived = LeaderboardPeriodPolicy.ResetWeeklyIfNeeded(state, utcNow, ChinaTimeZone);
```

Do not move `LeaderboardState`, `LeaderboardPlayerState`, or
`WeeklyLeaderboardSnapshot` out of `Agar.Sample.State.Leaderboard`.

- [ ] **Step 3: Update matchmaking policy tests**

`MatchmakingQueuePolicy` moves to `Server.Hotfix.State.Matchmaking`. Update the
test using directives:

```csharp
using Agar.Sample.State.Contracts.Matchmaking;
using Server.Hotfix.State.Matchmaking;
using Xunit;
```

Keep all existing test method bodies unchanged.

- [ ] **Step 4: Run Agar tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet test "samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj"
```

Expected: all Agar business logic tests pass.

### Task 8: Add Source-Scan Tests For Agar Shape

**Files:**
- Create or modify: `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixBoundaryTests.cs`

- [ ] **Step 1: Add a source scan test**

Create `AgarHotfixBoundaryTests.cs` with this test:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarHotfixBoundaryTests
{
    [Fact]
    public void Stable_state_actors_do_not_declare_business_methods()
    {
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/StateStores.cs")
            .DirectoryName!;
        var actorFiles = Directory.GetFiles(stateRoot, "*Actor.cs", SearchOption.AllDirectories);
        var forbidden = new Regex(
            @"^\s*(public|internal|private|protected)\s+(static\s+|override\s+|async\s+)*(Task|ValueTask|[\w<>]+)\s+\w+\s*\(",
            RegexOptions.Multiline);

        foreach (var file in actorFiles)
        {
            var text = File.ReadAllText(file);
            var matches = forbidden.Matches(text)
                .Where(match => !match.Value.Contains("OnActivateAsync", StringComparison.Ordinal))
                .Where(match => !match.Value.Contains("OnDeactivateAsync", StringComparison.Ordinal))
                .ToArray();

            Assert.True(matches.Length == 0, $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)} declares stable actor methods: {string.Join(", ", matches.Select(match => match.Value.Trim()))}");
        }
    }

    private static FileInfo FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return new FileInfo(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
```

- [ ] **Step 2: Run the Agar tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet test "samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj"
```

Expected: all tests pass, including the new source scan.

### Task 9: Final Verification

**Files:**
- No new files

- [ ] **Step 1: Run focused builds and tests**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet test "tests/Lakona.Game.Server.Hotfix.Generators.Tests/Lakona.Game.Server.Hotfix.Generators.Tests.csproj"
dotnet test "samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj"
dotnet build "samples/Game.Unity.Agar/Server/App/Server.App.csproj"
dotnet build "samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj"
```

Expected: all commands pass.

- [ ] **Step 2: Inspect the diff**

Run:

```powershell
git diff --check
git diff --stat
```

Expected: `git diff --check` produces no whitespace errors. The stat should show only analyzer, tests, docs, and Agar sample files related to actor/behavior separation.

- [ ] **Step 3: Manual acceptance checks**

Confirm these conditions in the final diff:

- `samples/Game.Unity.Agar/Server/App/State/**/*Actor.cs` contains no ordinary business methods.
- `samples/Game.Unity.Agar/Server/Hotfix/State/**/*Behavior.cs` contains the moved actor behavior.
- `samples/Game.Unity.Agar/Server/App/State/StateStores.cs` calls `HotfixDispatch.Invoke` for actor behavior.
- `samples/Game.Unity.Agar/Server/App/Realtime/RoomRuntime.cs` does not catch
  `HotfixMethodNotLoadedException` and does not contain stable duplicate
  settlement rules.
- `Server.App` references to framework packages under `Lakona.Game.Server.Hotfix*` are allowed.
- `Server.App` must not reference the reloadable sample project, assembly, or namespace `Server.Hotfix`.
- No hotfix code owns timers, threads, static event subscriptions, or cached callbacks.
- Analyzer diagnostic `ULGHOTFIX011` is an error.

## Notes For The Implementing Agent

- Do not change the internal actor kernel to enforce this rule at runtime.
- Do not add a configuration switch that disables hotfix.
- Do not use `ValueTask.CompletedTask` or `ValueTask.FromResult`.
- Do not move client/server DTOs out of `Shared`.
- Do not add analyzer opt-outs or path filters. A project that consumes
  `Lakona.Game.Server.Hotfix.Generators` as an analyzer is opting into the
  mandatory hotfix authoring rules.
- Do not add `JoinAsync` or `SetReadyAsync` to `IRoomStateStore` unless a
  current call site needs those bridge methods. They should remain dispatchable
  Behavior methods without store methods in this migration.
- Do not change the Agar sample `LakonaHotfixBuildTag` value. It is currently
  `dev`, and this sample migration does not require a concrete version tag
  update.
- Keep `BuildTag` rules from `docs/game/hotfix-architecture.md`: changing actor fields or hotfix-visible stable contracts requires a BuildTag update in versioned generated or production projects; moving pure logic into hotfix does not require a tag update by itself.
