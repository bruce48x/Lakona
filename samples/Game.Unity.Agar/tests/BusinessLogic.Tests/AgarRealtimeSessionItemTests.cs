using System.Text.RegularExpressions;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarRealtimeSessionItemTests
{
    [Fact]
    public void Battle_input_path_uses_session_items_instead_of_user_actor_snapshot()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Hotfix",
            "Rooms",
            "BattleService.cs"));
        var method = ExtractMethod(source, "public async ValueTask SubmitInputAsync");

        Assert.Contains("call.CurrentSessionItems.GetString(RoomIdSessionItemKey)", method, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSessionItems.GetString(RealtimeSessionIdSessionItemKey)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RealtimeSessionGeneration", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSnapshotAsync(new PlayerSessionSnapshotRequest())", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_input_contract_carries_realtime_session_identity()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            "Rooms",
            "RoomContracts.cs"));
        var contract = ExtractClass(source, "RoomInputSubmitRequest");

        Assert.Contains("public string RealtimeSessionId { get; set; } = \"\";", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionGeneration", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_input_path_rejects_stale_realtime_session_identity()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Hotfix",
            "Rooms",
            "RoomBehavior.cs"));
        var method = ExtractMethod(source, "public ValueTask SubmitInputAsync");

        Assert.Contains("!string.Equals(player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionGeneration", method, StringComparison.Ordinal);
        Assert.Contains("return default;", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_attach_ready_failure_clears_user_realtime_identity()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Hotfix",
            "Rooms",
            "BattleService.cs"));
        var method = ExtractMethod(source, "public async ValueTask<RealtimeAttachReply> AttachRealtimeAsync");
        var readyFailure = ExtractBlockStartingAt(method, method.IndexOf("if (!ready.Succeeded)", StringComparison.Ordinal));

        Assert.Contains("static behavior => behavior.ClearRealtimeAsync", readyFailure, StringComparison.Ordinal);
        Assert.Contains("new PlayerRealtimeClearRequest", readyFailure, StringComparison.Ordinal);
        Assert.Contains("RealtimeSessionId = realtimeSession.SessionId", readyFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionGeneration", readyFailure, StringComparison.Ordinal);
        Assert.Contains(".TerminateSessionAsync(", readyFailure, StringComparison.Ordinal);
    }

    private static string ExtractClass(string source, string className)
    {
        var match = Regex.Match(source, $@"\bclass\s+{Regex.Escape(className)}\b");
        Assert.True(match.Success, $"Could not find class '{className}'.");
        return ExtractBlockStartingAt(source, match.Index);
    }

    private static string ExtractMethod(string source, string signaturePrefix)
    {
        var index = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Could not find method signature starting with '{signaturePrefix}'.");
        return ExtractBlockStartingAt(source, index);
    }

    private static string ExtractBlockStartingAt(string source, int declarationIndex)
    {
        var blockStart = source.IndexOf('{', declarationIndex);
        Assert.True(blockStart >= 0, "Could not find block start.");

        var depth = 0;
        for (var index = blockStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth += 1;
            }
            else if (source[index] == '}')
            {
                depth -= 1;
                if (depth == 0)
                {
                    return source.Substring(declarationIndex, index - declarationIndex + 1);
                }
            }
        }

        throw new InvalidOperationException("Could not find block end.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
