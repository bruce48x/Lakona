#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;
using NUnitAssert = NUnit.Framework.Assert;

namespace SampleClient.Gameplay.Tests
{
    public sealed class DotArenaTwentyClientLifecyclePlayModeTests
    {
        private const int ClientCount = 20;
        private const int ExpectedRoomCount = 2;
        private const int ExpectedPlayersPerRoom = 10;
        private static string _phase = "not started";

        [UnityTest]
        public IEnumerator TwentyClientsCompleteMatchBattleSettlementAndLeaderboard()
        {
            var task = RunLifecycleAsync();
            var startedAt = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - startedAt < 300f)
            {
                yield return null;
            }

            NUnitAssert.That(task.IsCompleted, Is.True, $"20-client lifecycle test timed out after 300 seconds during phase '{_phase}'.");
            NUnitAssert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().ToString());
            NUnitAssert.That(task.IsCanceled, Is.False, "20-client lifecycle test was canceled.");
        }

        private static async Task RunLifecycleAsync()
        {
            var endpoint = EndpointOptions.FromCommandLine();
            var runId = $"e2e20-{Guid.NewGuid():N}";
            var password = $"password-{runId}";
            var clients = Enumerable.Range(0, ClientCount)
                .Select(index => new AgarE2EClient($"{runId}-player-{index:00}", password))
                .ToArray();
            var verificationClients = new List<AgarE2EClient>();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(270));
            try
            {
                SetPhase("login 20 control clients");
                var loginReplies = await Task.WhenAll(clients.Select(client =>
                    client.ConnectAndLoginAsync(endpoint.Host, endpoint.Port, endpoint.Path, timeout.Token))).ConfigureAwait(false);
                NUnitAssert.That(loginReplies, Has.All.Matches<LoginReply>(reply => reply.Code == LoginResultCodes.Ok));
                NUnitAssert.That(loginReplies.Select(reply => reply.PlayerId).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ClientCount));

                SetPhase("verify empty leaderboard baseline");
                var baseline = await clients[0].GetLeaderboardAsync(ClientCount).ConfigureAwait(false);
                NUnitAssert.That(baseline.Code, Is.EqualTo(0), baseline.Message);
                NUnitAssert.That(baseline.Entries, Is.Empty, "The isolated E2E database must start with an empty leaderboard.");

                SetPhase("matchmaking 20 clients");
                await Task.WhenAll(clients.Select(client => client.StartMatchmakingAsync())).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => clients.All(client => client.RealtimeEndpoint != null),
                    TimeSpan.FromSeconds(45),
                    "Not every client received a matched realtime endpoint.",
                    timeout.Token).ConfigureAwait(false);

                var rooms = clients
                    .GroupBy(client => client.RealtimeEndpoint!.RoomId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                NUnitAssert.That(rooms.Count, Is.EqualTo(ExpectedRoomCount), FormatRooms(rooms));
                NUnitAssert.That(
                    rooms.Values.All(roomClients => roomClients.Length == ExpectedPlayersPerRoom),
                    Is.True,
                    FormatRooms(rooms));
                NUnitAssert.That(clients, Has.None.Matches<AgarE2EClient>(client => client.PlayerId.StartsWith(VictoryPointAwards.BotPrefix, StringComparison.Ordinal)));

                SetPhase("attach 20 KCP clients");
                await Task.WhenAll(clients.Select(client => client.AttachRealtimeAsync(timeout.Token))).ConfigureAwait(false);
                NUnitAssert.That(clients, Has.All.Matches<AgarE2EClient>(client => client.IsRealtimeConnected));

                SetPhase("drive two real battles to MatchEnd");
                await DriveBattleUntilMatchEndAsync(clients, timeout.Token).ConfigureAwait(false);
                NUnitAssert.That(clients, Has.All.Matches<AgarE2EClient>(client => client.WorldStateCount >= 100),
                    "Every client must receive a sustained stream of real battle world states.");

                var expectedProfiles = BuildExpectedProfiles(rooms);
                var expectedLeaderboard = BuildExpectedLeaderboard(expectedProfiles);

                SetPhase("wait for settlement and leaderboard convergence");
                var actualLeaderboard = await WaitForLeaderboardAsync(
                    clients[0],
                    expectedLeaderboard,
                    TimeSpan.FromSeconds(20),
                    timeout.Token).ConfigureAwait(false);

                NUnitAssert.That(expectedProfiles.Values.Sum(profile => profile.VictoryPoints), Is.EqualTo(52));
                NUnitAssert.That(expectedProfiles.Values.Count(profile => profile.WinCount == 1), Is.EqualTo(ExpectedRoomCount));
                AssertLeaderboard(expectedLeaderboard, actualLeaderboard);

                SetPhase("logout original clients");
                await DisposeAllAsync(clients, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                foreach (var expected in expectedProfiles.Values.OrderBy(profile => profile.PlayerId, StringComparer.Ordinal))
                {
                    var verifier = new AgarE2EClient(expected.PlayerId, password);
                    verificationClients.Add(verifier);
                }

                SetPhase("relogin and verify 20 persisted profiles");
                var persistedProfiles = await Task.WhenAll(verificationClients.Select(client =>
                    client.ConnectAndLoginAsync(endpoint.Host, endpoint.Port, endpoint.Path, timeout.Token))).ConfigureAwait(false);
                foreach (var reply in persistedProfiles)
                {
                    var expected = expectedProfiles[reply.PlayerId];
                    NUnitAssert.That(reply.VictoryPoints, Is.EqualTo(expected.VictoryPoints), $"Victory points mismatch for {reply.PlayerId}.");
                    NUnitAssert.That(reply.WinCount, Is.EqualTo(expected.WinCount), $"Win count mismatch for {reply.PlayerId}.");
                }

                SetPhase("write lifecycle report");
                WriteReport(endpoint.ReportPath, runId, rooms, expectedProfiles, actualLeaderboard, persistedProfiles);
                SetPhase("completed");
            }
            finally
            {
                await DisposeAllAsync(clients, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await DisposeAllAsync(verificationClients, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        private static async Task DriveBattleUntilMatchEndAsync(IReadOnlyList<AgarE2EClient> clients, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(150);
            while (DateTime.UtcNow < deadline && clients.Any(client => client.MatchEnd == null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var submissions = clients
                    .Where(client => client.MatchEnd == null && client.IsRealtimeConnected)
                    .Select(client => client.SubmitInputAsync(client.BuildInput()))
                    .ToArray();
                await Task.WhenAll(submissions).ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            NUnitAssert.That(clients, Has.All.Matches<AgarE2EClient>(client => client.MatchEnd != null),
                "Not every client received MatchEnd within 150 seconds.");
        }

        private static Dictionary<string, ExpectedProfile> BuildExpectedProfiles(
            IReadOnlyDictionary<string, AgarE2EClient[]> rooms)
        {
            var expected = new Dictionary<string, ExpectedProfile>(StringComparer.Ordinal);
            foreach (var room in rooms.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var world = room.Value
                    .Select(client => client.LastWorldState)
                    .Where(state => state != null)
                    .OrderByDescending(state => state!.Tick)
                    .FirstOrDefault();
                NUnitAssert.That(world, Is.Not.Null, $"Room {room.Key} did not publish a final world state.");
                NUnitAssert.That(world!.Players.Count, Is.EqualTo(ExpectedPlayersPerRoom), $"Room {room.Key} final player count mismatch.");

                var ranking = world.Players
                    .OrderByDescending(player => player.Mass)
                    .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
                    .ToArray();
                var clientIds = room.Value.Select(client => client.PlayerId).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                NUnitAssert.That(ranking.Select(player => player.PlayerId).OrderBy(value => value, StringComparer.Ordinal), Is.EqualTo(clientIds));

                for (var index = 0; index < ranking.Length; index++)
                {
                    var rank = index + 1;
                    var player = ranking[index];
                    expected.Add(player.PlayerId, new ExpectedProfile
                    {
                        PlayerId = player.PlayerId,
                        RoomId = room.Key,
                        Rank = rank,
                        FinalMass = player.Mass,
                        VictoryPoints = VictoryPointAwards.GetPointsForRank(rank),
                        WinCount = rank == 1 ? 1 : 0
                    });
                }

                var expectedWinner = ranking[0].PlayerId;
                NUnitAssert.That(room.Value, Has.All.Matches<AgarE2EClient>(client =>
                    string.Equals(client.MatchEnd!.WinnerPlayerId, expectedWinner, StringComparison.Ordinal)),
                    $"Room {room.Key} clients disagreed with the final-world ranking winner.");
            }

            return expected;
        }

        private static ExpectedProfile[] BuildExpectedLeaderboard(IReadOnlyDictionary<string, ExpectedProfile> profiles)
        {
            return profiles.Values
                .Where(profile => profile.VictoryPoints > 0)
                .OrderByDescending(profile => profile.VictoryPoints)
                .ThenByDescending(profile => profile.WinCount)
                .ThenBy(profile => profile.PlayerId, StringComparer.Ordinal)
                .ToArray();
        }

        private static async Task<LeaderboardReply> WaitForLeaderboardAsync(
            AgarE2EClient client,
            IReadOnlyList<ExpectedProfile> expected,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            LeaderboardReply? last = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                last = await client.GetLeaderboardAsync(ClientCount).ConfigureAwait(false);
                if (LeaderboardMatches(expected, last))
                {
                    return last;
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            NUnitAssert.Fail($"Leaderboard did not converge after settlement. Last value: {FormatLeaderboard(last)}");
            return last!;
        }

        private static bool LeaderboardMatches(IReadOnlyList<ExpectedProfile> expected, LeaderboardReply actual)
        {
            if (actual.Code != 0 || actual.Entries.Count != expected.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                var expectedEntry = expected[index];
                var actualEntry = actual.Entries[index];
                if (!string.Equals(expectedEntry.PlayerId, actualEntry.PlayerId, StringComparison.Ordinal) ||
                    expectedEntry.VictoryPoints != actualEntry.VictoryPoints ||
                    expectedEntry.WinCount != actualEntry.WinCount ||
                    actualEntry.Rank != index + 1)
                {
                    return false;
                }
            }

            return !string.IsNullOrWhiteSpace(actual.PeriodStartUtc) && actual.SecondsUntilReset > 0;
        }

        private static void AssertLeaderboard(IReadOnlyList<ExpectedProfile> expected, LeaderboardReply actual)
        {
            NUnitAssert.That(LeaderboardMatches(expected, actual), Is.True, FormatLeaderboard(actual));
            NUnitAssert.That(actual.Entries.Sum(entry => entry.VictoryPoints), Is.EqualTo(52));
        }

        private static async Task WaitUntilAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            string failure,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            NUnitAssert.Fail(failure);
        }

        private static async Task DisposeAllAsync(IEnumerable<AgarE2EClient> clients, TimeSpan? timeout = null)
        {
            var dispose = Task.WhenAll(clients.Select(client => client.DisposeAsync().AsTask()));
            if (timeout == null)
            {
                await dispose.ConfigureAwait(false);
                return;
            }

            await Task.WhenAny(dispose, Task.Delay(timeout.Value)).ConfigureAwait(false);
        }

        private static void SetPhase(string phase)
        {
            _phase = phase;
            Console.WriteLine($"[AgarE2E20] {phase}");
        }

        private static string FormatRooms(IReadOnlyDictionary<string, AgarE2EClient[]> rooms)
        {
            return string.Join(", ", rooms.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value.Length}"));
        }

        private static string FormatLeaderboard(LeaderboardReply? reply)
        {
            return reply == null
                ? "<null>"
                : $"code={reply.Code}, period={reply.PeriodStartUtc}, reset={reply.SecondsUntilReset}, entries=[{string.Join(", ", reply.Entries.Select(entry => $"{entry.Rank}:{entry.PlayerId}:{entry.VictoryPoints}:{entry.WinCount}"))}]";
        }

        private static void WriteReport(
            string reportPath,
            string runId,
            IReadOnlyDictionary<string, AgarE2EClient[]> rooms,
            IReadOnlyDictionary<string, ExpectedProfile> expectedProfiles,
            LeaderboardReply leaderboard,
            IReadOnlyList<LoginReply> persistedProfiles)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var persisted = persistedProfiles.ToDictionary(reply => reply.PlayerId, StringComparer.Ordinal);
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"runId\": \"{Escape(runId)}\",");
            json.AppendLine($"  \"roomCount\": {rooms.Count},");
            json.AppendLine($"  \"totalVictoryPoints\": {leaderboard.Entries.Sum(entry => entry.VictoryPoints)},");
            json.AppendLine("  \"players\": [");
            var players = expectedProfiles.Values.OrderBy(profile => profile.PlayerId, StringComparer.Ordinal).ToArray();
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                var actual = persisted[player.PlayerId];
                json.Append("    {")
                    .Append($"\"playerId\":\"{Escape(player.PlayerId)}\",")
                    .Append($"\"roomId\":\"{Escape(player.RoomId)}\",")
                    .Append($"\"rank\":{player.Rank},")
                    .Append($"\"finalMass\":{player.FinalMass.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},")
                    .Append($"\"expectedVictoryPoints\":{player.VictoryPoints},")
                    .Append($"\"actualVictoryPoints\":{actual.VictoryPoints},")
                    .Append($"\"expectedWinCount\":{player.WinCount},")
                    .Append($"\"actualWinCount\":{actual.WinCount}")
                    .Append(index + 1 == players.Length ? "}" : "},")
                    .AppendLine();
            }

            json.AppendLine("  ],");
            json.AppendLine("  \"leaderboard\": [");
            for (var index = 0; index < leaderboard.Entries.Count; index++)
            {
                var entry = leaderboard.Entries[index];
                json.Append("    {")
                    .Append($"\"rank\":{entry.Rank},")
                    .Append($"\"playerId\":\"{Escape(entry.PlayerId)}\",")
                    .Append($"\"victoryPoints\":{entry.VictoryPoints},")
                    .Append($"\"winCount\":{entry.WinCount}")
                    .Append(index + 1 == leaderboard.Entries.Count ? "}" : "},")
                    .AppendLine();
            }

            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(reportPath, json.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private sealed class ExpectedProfile
        {
            public string PlayerId { get; set; } = string.Empty;
            public string RoomId { get; set; } = string.Empty;
            public int Rank { get; set; }
            public float FinalMass { get; set; }
            public int VictoryPoints { get; set; }
            public int WinCount { get; set; }
        }

        private sealed class EndpointOptions
        {
            public string Host { get; private set; } = "127.0.0.1";
            public int Port { get; private set; } = 20000;
            public string Path { get; private set; } = "/ws";
            public string ReportPath { get; private set; } = string.Empty;

            public static EndpointOptions FromCommandLine()
            {
                var result = new EndpointOptions();
                var args = Environment.GetCommandLineArgs();
                for (var index = 0; index < args.Length; index++)
                {
                    switch (args[index])
                    {
                        case "--host" when index + 1 < args.Length:
                            result.Host = args[++index];
                            break;
                        case "--port" when index + 1 < args.Length:
                            if (int.TryParse(args[++index], out var port) && port > 0)
                            {
                                result.Port = port;
                            }

                            break;
                        case "--path" when index + 1 < args.Length:
                            result.Path = args[++index];
                            break;
                        case "--lifecycle-report" when index + 1 < args.Length:
                            result.ReportPath = args[++index];
                            break;
                    }
                }

                return result;
            }
        }
    }
}
