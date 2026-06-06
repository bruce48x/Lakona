using Server.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat;

[HotfixSystemOf(typeof(ChatRuleState))]
public static class ChatRulesSystem
{
    private static readonly string[] BannedWords =
    [
        "foo",
        "bar",
        "baz"
    ];

    public static string FilterMessage(this ChatRuleState state, string message)
    {
        string result = message;
        foreach (string bannedWord in BannedWords)
        {
            result = result.Replace(bannedWord, state.Replacement, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
