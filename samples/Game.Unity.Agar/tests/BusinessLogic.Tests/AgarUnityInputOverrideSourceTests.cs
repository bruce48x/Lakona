using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarUnityInputOverrideSourceTests
{
    [Fact]
    public void Multiplayer_test_input_override_is_available_in_test_player_compilation()
    {
        var gameplay = Path.Combine(
            TestHotfix.FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Client",
            "Assets",
            "Scripts",
            "Gameplay");
        var game = Normalize(File.ReadAllText(Path.Combine(gameplay, "DotArenaGame.cs")));
        var input = Normalize(File.ReadAllText(Path.Combine(gameplay, "DotArenaGame.SinglePlayer.cs")));
        var testing = Normalize(File.ReadAllText(Path.Combine(gameplay, "DotArenaGame.Testing.cs")));

        Assert.Contains("#if UNITY_EDITOR || UNITY_INCLUDE_TESTS", game, StringComparison.Ordinal);
        Assert.Contains("#if UNITY_EDITOR || UNITY_INCLUDE_TESTS", input, StringComparison.Ordinal);
        Assert.DoesNotContain("#if UNITY_EDITOR\n", testing, StringComparison.Ordinal);
    }

    private static string Normalize(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
}
