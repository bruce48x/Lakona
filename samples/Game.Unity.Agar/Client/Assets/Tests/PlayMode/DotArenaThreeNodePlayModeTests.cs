#nullable enable

using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using NUnit.Framework;
using SampleClient.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using NUnitAssert = NUnit.Framework.Assert;

namespace SampleClient.Gameplay.Tests
{
    public sealed class DotArenaThreeNodePlayModeTests
    {
        private const string GameplaySceneName = "Gameplay";

        [UnityTest]
        public IEnumerator UnityClientDisplaysProfileAndLeaderboard()
        {
            var endpoint = AgarPlayModeEndpoint.FromCommandLine();
            var load = SceneManager.LoadSceneAsync(GameplaySceneName, LoadSceneMode.Single);
            NUnitAssert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var game = UnityEngine.Object.FindObjectOfType<DotArenaGame>();
            NUnitAssert.That(game, Is.Not.Null, "DotArenaGame was not found in Gameplay.unity.");

            game!.ApplyEndpointForTest(endpoint.Host, endpoint.Port, endpoint.Path);
            game.OnUiMultiplayerSelected();
            yield return null;
            game.OnUiGuestLoginRequested();

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.EntryMenuState == "MultiplayerLobby" &&
                            snapshot.IsControlConnected &&
                            !string.IsNullOrWhiteSpace(snapshot.LocalPlayerId),
                "guest login did not reach multiplayer lobby",
                45f);
            yield return null;

            var login = game.BuildTestSnapshot();
            var lobby = game.transform.Find("SceneUI/LobbyPanel");
            NUnitAssert.That(lobby, Is.Not.Null);
            var profileContent = lobby!.Find("ProfileContent");
            var leaderboardContent = lobby.Find("LeaderboardContent");
            NUnitAssert.That(profileContent, Is.Not.Null);
            NUnitAssert.That(leaderboardContent, Is.Not.Null);
            NUnitAssert.That(profileContent!.gameObject.activeSelf, Is.True);
            NUnitAssert.That(leaderboardContent!.gameObject.activeSelf, Is.False);
            NUnitAssert.That(profileContent.Find("PlayerText").GetComponent<TMP_Text>().text, Is.EqualTo($"Player: {login.LocalPlayerId}"));
            NUnitAssert.That(profileContent.Find("WinsText").GetComponent<TMP_Text>().text, Does.Match(@"^Wins\n\d+$"));
            NUnitAssert.That(profileContent.Find("VictoryPointsText").GetComponent<TMP_Text>().text, Does.Match(@"^Victory Points\n\d+$"));

            lobby.Find("LeaderboardButton").GetComponent<Button>().onClick.Invoke();
            yield return new WaitUntil(() => leaderboardContent.gameObject.activeSelf);
            var periodText = leaderboardContent.Find("PeriodText").GetComponent<TMP_Text>();
            yield return WaitForCondition(
                () => periodText.text.StartsWith("Week of ", StringComparison.Ordinal),
                "leaderboard query did not update the weekly period",
                15f);

            NUnitAssert.That(profileContent.gameObject.activeSelf, Is.False);
            NUnitAssert.That(leaderboardContent.Find("HeaderText").GetComponent<TMP_Text>().text, Does.Contain("Victory Points").Or.Contain("VP"));
            var hasRows = Enumerable.Range(1, 10)
                .Any(index => leaderboardContent.Find($"Row{index}Text").gameObject.activeSelf);
            var showsEmptyState = leaderboardContent.Find("EmptyText").gameObject.activeSelf;
            NUnitAssert.That(hasRows || showsEmptyState, Is.True, "Leaderboard must show rows or an explicit empty state.");

            lobby.Find("ProfileButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            NUnitAssert.That(profileContent.gameObject.activeSelf, Is.True);
            NUnitAssert.That(leaderboardContent.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator UnityClientCompletesThreeNodeMultiplayerSmoke()
        {
            var endpoint = AgarPlayModeEndpoint.FromCommandLine();
            var load = SceneManager.LoadSceneAsync(GameplaySceneName, LoadSceneMode.Single);
            NUnitAssert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var game = UnityEngine.Object.FindObjectOfType<DotArenaGame>();
            NUnitAssert.That(game, Is.Not.Null, "DotArenaGame was not found in Gameplay.unity.");

            game!.ApplyEndpointForTest(endpoint.Host, endpoint.Port, endpoint.Path);
            game.OnUiMultiplayerSelected();
            yield return null;
            game.OnUiGuestLoginRequested();

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.EntryMenuState == "MultiplayerLobby" &&
                            snapshot.SessionMode == "Multiplayer" &&
                            snapshot.IsControlConnected &&
                            !string.IsNullOrWhiteSpace(snapshot.LocalPlayerId),
                "guest login did not reach multiplayer lobby",
                45f);

            game.RequestMultiplayerMatchmakingForTest();

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.LastRealtimeTransport == "Kcp" &&
                            snapshot.LastRealtimePort > 0 &&
                            !string.IsNullOrWhiteSpace(snapshot.LastRealtimeRoomId) &&
                            !string.IsNullOrWhiteSpace(snapshot.LastRealtimeMatchId),
                "matchmaking did not provide a KCP realtime endpoint",
                60f);

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.IsRealtimeConnected,
                "KCP realtime connection did not attach",
                45f);

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.FlowState == "InMatch",
                "client did not enter match flow",
                45f);

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.LastWorldTick >= 0 && snapshot.ViewCount > 0,
                "world state was not received",
                45f);

            var beforeCheat = game.BuildTestSnapshot();
            yield return WaitForTask(
                game.AddCheatMassForTest(),
                "cheat mass input submission did not complete",
                10f);
            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.LocalPlayerMass >= beforeCheat.LocalPlayerMass + 99.5f,
                "local player did not receive the server-authoritative cheat mass gain",
                10f);

            var beforeMove = game.BuildTestSnapshot();
            var move = beforeMove.LocalPlayerX >= 0f ? Vector2.left : Vector2.right;
            var submitInput = game.SetEditorMoveOverrideForTest(move);
            yield return WaitForTask(submitInput, "inward input submission did not complete", 10f);
            yield return WaitForSnapshot(
                game,
                snapshot => move.x < 0f
                    ? snapshot.LocalPlayerX < beforeMove.LocalPlayerX - 0.25f
                    : snapshot.LocalPlayerX > beforeMove.LocalPlayerX + 0.25f,
                "local player did not move toward the arena center after submitting input",
                10f);
            game.ClearEditorMoveOverrideForTest();

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.MatchProgressRevisions.Length >= 2,
                "control match progress callbacks were not received",
                15f);

            yield return ExerciseTopologyChangeWindow(game, endpoint);

            var beforeOffline = game.BuildTestSnapshot();
            NUnitAssert.That(beforeOffline.ControlReliablePushEnabled, Is.True,
                "WebSocket control handshake must advertise reliable push enabled");
            NUnitAssert.That(beforeOffline.RealtimeReliablePushEnabled, Is.False,
                "KCP realtime handshake must advertise reliable push disabled");
            var offlineStart = DateTime.UtcNow;
            yield return WaitForTask(
                game.SetNetworkGateForTestAsync(false),
                "network gate did not close",
                10f);
            yield return new WaitForSecondsRealtime(3f);
            var offlineEnd = DateTime.UtcNow;
            yield return WaitForTask(
                game.SetNetworkGateForTestAsync(true),
                "network gate did not open",
                10f);

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.IsControlConnected &&
                            snapshot.IsRealtimeConnected &&
                            snapshot.FlowState == "InMatch" &&
                            snapshot.LastWorldTick > beforeOffline.LastWorldTick,
                "dual-channel recovery did not restore live gameplay",
                60f);

            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.MatchProgressRevisions.Length >=
                            beforeOffline.MatchProgressRevisions.Length + 2,
                "replayed control progress callbacks were not applied on the Unity main thread",
                25f);

            var recovered = game.BuildTestSnapshot();
            NUnitAssert.That(recovered.ControlSessionId, Is.EqualTo(beforeOffline.ControlSessionId));
            NUnitAssert.That(recovered.RealtimeSessionId, Is.EqualTo(beforeOffline.RealtimeSessionId));
            NUnitAssert.That(recovered.ControlRpcSerial, Is.EqualTo(beforeOffline.ControlRpcSerial),
                "framework recovery must preserve the control client facade");
            NUnitAssert.That(recovered.RealtimeRpcSerial, Is.EqualTo(beforeOffline.RealtimeRpcSerial),
                "framework recovery must preserve the realtime client facade");
            NUnitAssert.That(recovered.LocalPlayerId, Is.EqualTo(beforeOffline.LocalPlayerId));
            NUnitAssert.That(recovered.LastRealtimeRoomId, Is.EqualTo(beforeOffline.LastRealtimeRoomId));
            NUnitAssert.That(recovered.LastRealtimeMatchId, Is.EqualTo(beforeOffline.LastRealtimeMatchId));
            NUnitAssert.That(recovered.ControlLastReliableSequence,
                Is.GreaterThan(beforeOffline.ControlLastReliableSequence),
                "reliable sequence must advance after offline replay");

            var recoveredMove = recovered.LocalPlayerX >= 0f ? Vector2.left : Vector2.right;
            yield return WaitForTask(
                game.SetEditorMoveOverrideForTest(recoveredMove),
                "post-recovery input submission did not complete",
                10f);
            yield return WaitForSnapshot(
                game,
                snapshot => recoveredMove.x < 0f
                    ? snapshot.LocalPlayerX < recovered.LocalPlayerX - 0.25f
                    : snapshot.LocalPlayerX > recovered.LocalPlayerX + 0.25f,
                "local player did not respond to input after dual-channel recovery",
                10f);
            game.ClearEditorMoveOverrideForTest();

            var offlineProgress = recovered.MatchProgressPublishedAtUtc
                .Zip(recovered.MatchProgressRevisions, (published, revision) => new { published, revision })
                .Where(item => item.published >= offlineStart.AddMilliseconds(-250) &&
                               item.published <= offlineEnd.AddMilliseconds(250))
                .ToArray();
            NUnitAssert.That(offlineProgress.Length, Is.GreaterThanOrEqualTo(2),
                $"at least two control progress callbacks published offline must replay; " +
                $"offline=[{offlineStart:O},{offlineEnd:O}], " +
                $"received=[{string.Join(",", recovered.MatchProgressPublishedAtUtc.Select(static value => value.ToString("O")))}]");
            NUnitAssert.That(
                recovered.MatchProgressRevisions.Distinct().Count(),
                Is.EqualTo(recovered.MatchProgressRevisions.Length),
                "progress callbacks must be applied at most once");
            for (var index = 1; index < recovered.MatchProgressRevisions.Length; index++)
            {
                NUnitAssert.That(
                    recovered.MatchProgressRevisions[index],
                    Is.EqualTo(recovered.MatchProgressRevisions[index - 1] + 1),
                    "progress revisions must remain contiguous across replay");
            }
        }

        private static IEnumerator WaitForSnapshot(
            DotArenaGame game,
            Func<DotArenaGameTestSnapshot, bool> predicate,
            string failure,
            float timeoutSeconds)
        {
            var start = Time.realtimeSinceStartup;
            DotArenaGameTestSnapshot? last = null;

            while (Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                last = game.BuildTestSnapshot();
                if (predicate(last))
                {
                    yield break;
                }

                yield return null;
            }

            NUnitAssert.Fail($"{failure}. Last snapshot: {FormatSnapshot(last)}");
        }

        private static IEnumerator ExerciseTopologyChangeWindow(
            DotArenaGame game,
            AgarPlayModeEndpoint endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint.TopologyReadyPath) ||
                string.IsNullOrWhiteSpace(endpoint.TopologyReleasePath))
            {
                yield break;
            }

            var before = game.BuildTestSnapshot();
            File.WriteAllText(endpoint.TopologyReadyPath, before.LastWorldTick.ToString());
            var deadline = Time.realtimeSinceStartup + 120f;
            while (!File.Exists(endpoint.TopologyReleasePath) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            NUnitAssert.That(File.Exists(endpoint.TopologyReleasePath), Is.True,
                "the E2E controller did not complete its topology change");
            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.IsControlConnected &&
                            snapshot.IsRealtimeConnected &&
                            snapshot.FlowState == "InMatch" &&
                            snapshot.LastWorldTick >= before.LastWorldTick + 10,
                "game traffic did not continue across the topology change",
                60f);
        }

        private static IEnumerator WaitForTask(Task task, string failure, float timeoutSeconds)
        {
            var start = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                yield return null;
            }

            NUnitAssert.That(task.IsCompleted, Is.True, failure);
            NUnitAssert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        }

        private static IEnumerator WaitForCondition(Func<bool> predicate, string failure, float timeoutSeconds)
        {
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return null;
            }

            NUnitAssert.Fail(failure);
        }

        private static string FormatSnapshot(DotArenaGameTestSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return "<none>";
            }

            return string.Join(
                ", ",
                $"flow={snapshot.FlowState}",
                $"entry={snapshot.EntryMenuState}",
                $"mode={snapshot.SessionMode}",
                $"status={snapshot.Status}",
                $"player={snapshot.LocalPlayerId}",
                $"control={snapshot.IsControlConnected}",
                $"realtime={snapshot.IsRealtimeConnected}",
                $"connecting={snapshot.IsConnecting}",
                $"tick={snapshot.LastWorldTick}",
                $"views={snapshot.ViewCount}",
                $"rt={snapshot.LastRealtimeTransport}",
                $"rtHost={snapshot.LastRealtimeHost}",
                $"rtPort={snapshot.LastRealtimePort}",
                $"room={snapshot.LastRealtimeRoomId}",
                $"match={snapshot.LastRealtimeMatchId}",
                $"controlSession={snapshot.ControlSessionId}",
                $"realtimeSession={snapshot.RealtimeSessionId}",
                $"rpcSerials={snapshot.ControlRpcSerial}/{snapshot.RealtimeRpcSerial}",
                $"reliablePolicy={snapshot.ControlReliablePushEnabled}/{snapshot.RealtimeReliablePushEnabled}",
                $"reliableSequence={snapshot.ControlLastReliableSequence}",
                $"progressRevisions=[{string.Join(",", snapshot.MatchProgressRevisions)}]",
                $"progressTimes=[{string.Join(",", snapshot.MatchProgressPublishedAtUtc.Select(static value => value.ToString("O")))}]",
                $"local=({snapshot.LocalPlayerX:0.00},{snapshot.LocalPlayerY:0.00})",
                $"mass={snapshot.LocalPlayerMass:0.0}");
        }

        private sealed class AgarPlayModeEndpoint
        {
            private AgarPlayModeEndpoint(
                string host,
                int port,
                string path,
                string topologyReadyPath,
                string topologyReleasePath)
            {
                Host = host;
                Port = port;
                Path = path;
                TopologyReadyPath = topologyReadyPath;
                TopologyReleasePath = topologyReleasePath;
            }

            public string Host { get; }
            public int Port { get; }
            public string Path { get; }
            public string TopologyReadyPath { get; }
            public string TopologyReleasePath { get; }

            public static AgarPlayModeEndpoint FromCommandLine()
            {
                var host = "127.0.0.1";
                var port = 20000;
                var path = "/ws";
                var topologyReadyPath = string.Empty;
                var topologyReleasePath = string.Empty;
                var args = Environment.GetCommandLineArgs();
                for (var index = 0; index < args.Length; index++)
                {
                    switch (args[index])
                    {
                        case "--host" when index + 1 < args.Length:
                            host = args[++index];
                            break;
                        case "--port" when index + 1 < args.Length:
                            if (int.TryParse(args[++index], out var parsedPort) && parsedPort > 0)
                            {
                                port = parsedPort;
                            }

                            break;
                        case "--path" when index + 1 < args.Length:
                            path = args[++index];
                            break;
                        case "--topology-ready" when index + 1 < args.Length:
                            topologyReadyPath = args[++index];
                            break;
                        case "--topology-release" when index + 1 < args.Length:
                            topologyReleasePath = args[++index];
                            break;
                    }
                }

                return new AgarPlayModeEndpoint(
                    host,
                    port,
                    path,
                    topologyReadyPath,
                    topologyReleasePath);
            }
        }
    }
}
