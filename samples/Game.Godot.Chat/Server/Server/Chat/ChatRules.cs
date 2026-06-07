using Lakona.Game.Server.Hotfix.Dispatch;

namespace Server.Chat;

public sealed class ChatRuleState
{
    public string Replacement { get; set; } = "***";
}

public static class ChatRules
{
    private static readonly ChatRuleState State = new();

    public static string FilterMessage(string message)
    {
        return HotfixDispatch.Invoke<ChatRuleState, string, string>(
            nameof(FilterMessage),
            State,
            message);
    }
}
