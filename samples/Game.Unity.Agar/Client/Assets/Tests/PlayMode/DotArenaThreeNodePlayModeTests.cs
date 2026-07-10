#nullable enable

using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using SampleClient.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NUnitAssert = NUnit.Framework.Assert;

namespace SampleClient.Gameplay.Tests
{
    public sealed class DotArenaThreeNodePlayModeTests
    {
        private const string GameplaySceneName = "Gameplay";

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

            var beforeMove = game.BuildTestSnapshot();
            var submitInput = game.SetEditorMoveOverrideForTest(Vector2.right);
            yield return WaitForTask(submitInput, "rightward input submission did not complete", 10f);
            yield return WaitForSnapshot(
                game,
                snapshot => snapshot.LocalPlayerX > beforeMove.LocalPlayerX + 0.25f,
                "local player did not move after submitting rightward input",
                10f);
            game.ClearEditorMoveOverrideForTest();
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
                $"local=({snapshot.LocalPlayerX:0.00},{snapshot.LocalPlayerY:0.00})");
        }

        private sealed class AgarPlayModeEndpoint
        {
            private AgarPlayModeEndpoint(string host, int port, string path)
            {
                Host = host;
                Port = port;
                Path = path;
            }

            public string Host { get; }
            public int Port { get; }
            public string Path { get; }

            public static AgarPlayModeEndpoint FromCommandLine()
            {
                var host = "127.0.0.1";
                var port = 20000;
                var path = "/ws";
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
                    }
                }

                return new AgarPlayModeEndpoint(host, port, path);
            }
        }
    }
}
