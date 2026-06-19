# Hotfix Actor Behavior Enforcement Plan Clarifications

Status: resolved. The implementation plan has been updated to include these
decisions. This file records the answers so implementers do not reopen the same
questions.

## Decisions

1. `MatchmakingActor` keeps the current `MatchmakingState` shape.

   Do not redesign matchmaking state into separate `Tickets` and
   `NextTicketSequence` fields. The stable actor exposes:

   ```txt
   internal const int DefaultRoomSize = 10;
   internal readonly IPlayerSessionStateStore Sessions;
   internal readonly IRoomStateStore Rooms;
   internal readonly BattleRuntimeGatewayResolver RuntimeGateways;
   internal bool RecordExists;
   internal MatchmakingState State = new();
   ```

   `MatchmakingState.QueueId`, `DefaultRoomSize`, `PendingTickets`,
   `LastMatchId`, `LastRoomId`, and `LastUpdatedAtUtc` remain the source of
   truth.

2. Business logic tests may compile against `Server.Hotfix`.

   Change the `Server.Hotfix` project reference in
   `samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`
   from `ReferenceOutputAssembly="false"` to a normal project reference.
   Policy tests then import policy namespaces from `Server.Hotfix.State.*`.

3. `MatchmakingQueuePolicyTests.cs` is in scope.

   Update it to import `Server.Hotfix.State.Matchmaking` after moving
   `MatchmakingQueuePolicy` into the hotfix project.

4. `Server.App` may reference framework hotfix infrastructure.

   Allowed references:

   - `Lakona.Game.Server.Hotfix`
   - `Lakona.Game.Server.Hotfix.Abstractions`
   - `Lakona.Game.Server.Hotfix.Generators`
   - `Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch`

   Forbidden references:

   - the reloadable sample project `Server/Hotfix/Server.Hotfix.csproj`
   - the reloadable sample assembly `Server.Hotfix`
   - direct calls to Behavior extension methods from `Server.App`

5. Existing stable hotfix fallback paths are in scope.

   `samples/Game.Unity.Agar/Server/App/Realtime/RoomRuntime.cs` must not catch
   `HotfixMethodNotLoadedException` and continue with duplicate stable
   simulation or settlement rules. The runtime should call hotfix dispatch and
   fail fast if required hotfix behavior is not loaded.

6. `LeaderboardActor.OnActivateAsync` should be removed.

   Leaderboard period initialization moves into `LeaderboardBehavior` and runs
   lazily from `GetLeaderboardAsync`, `ResetWeeklyIfNeededAsync`, and
   `RecordVictoryPointsAsync`. Do not bridge `OnActivateAsync` through hotfix
   dispatch.

7. The Agar sample BuildTag stays `dev`.

   No concrete BuildTag update is required for this sample migration. The
   general BuildTag rule still applies to versioned generated or production
   projects.

8. `RoomBehavior.JoinAsync` and `RoomBehavior.SetReadyAsync` remain behavior
   methods without store methods.

   Do not add them to `IRoomStateStore` unless a current call site needs those
   bridge methods.

9. Extract nested leaderboard policies explicitly.

   `LeaderboardRankingPolicy` and `LeaderboardPeriodPolicy` are currently in
   `LeaderboardActor.cs`. Extract them into separate files under
   `samples/Game.Unity.Agar/Server/Hotfix/State/Leaderboard`.

10. The analyzer has no opt-out.

    Any project that consumes `Lakona.Game.Server.Hotfix.Generators` as an
    analyzer opts into the mandatory hotfix authoring model. Do not add path,
    assembly, or attribute filters for this rule.
