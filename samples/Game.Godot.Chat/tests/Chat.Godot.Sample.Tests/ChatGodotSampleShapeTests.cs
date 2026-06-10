using System.Text.Json;
using Xunit;

namespace Chat.Godot.Sample.Tests;

public sealed class ChatGodotSampleShapeTests
{
    private static readonly string SampleRoot = FindSampleRoot();

    [Fact]
    public void SampleIsSingleEndpointChatNotRealtimeAgar()
    {
        string allText = ReadAllSampleText();
        string config = File.ReadAllText(Path.Combine(SampleRoot, "lakona-game.tool.json"));

        using JsonDocument document = JsonDocument.Parse(config);
        Assert.Equal("websocket", document.RootElement.GetProperty("transport").GetString());

        if (document.RootElement.TryGetProperty("networkProfile", out JsonElement networkProfile))
        {
            Assert.Equal("single", networkProfile.GetString());
        }

        Assert.DoesNotContain("AddRpcEndpoint", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("Kcp", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AttachRealtime", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("Matchmaking", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArenaSimulation", allText, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldState", allText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSendPathUsesActorAndHotfix()
    {
        string service = File.ReadAllText(Path.Combine(SampleRoot, "Server", "Server", "Chat", "ChatService.cs"));
        string actor = File.ReadAllText(Path.Combine(SampleRoot, "Server", "Server", "Chat", "ChatRoomActor.cs"));
        string rules = File.ReadAllText(Path.Combine(SampleRoot, "Server", "Server", "Chat", "ChatRules.cs"));
        string hotfix = File.ReadAllText(Path.Combine(SampleRoot, "Server", "Hotfix", "Chat", "ChatRulesSystem.cs"));

        Assert.Contains("IActorRuntime", service, StringComparison.Ordinal);
        Assert.Contains("AskAsync<ChatRoomActor", service, StringComparison.Ordinal);
        Assert.Contains("FilterMessage", actor, StringComparison.Ordinal);
        Assert.Contains("HotfixDispatch.Invoke", rules, StringComparison.Ordinal);
        Assert.Contains("[HotfixSystemOf(typeof(ChatRuleState))]", hotfix, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly ChatRoom", service + actor, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentQueue", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("lock", actor, StringComparison.Ordinal);
    }

    private static string ReadAllSampleText()
    {
        string[] extensions = [".cs", ".csproj", ".json", ".tscn", ".gd", ".slnx"];
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(SampleRoot, "*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    private static string FindSampleRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "samples", "Game.Godot.Chat");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find samples/Game.Godot.Chat from test output directory.");
    }
}
