namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixAdminOptions
{
    public string HotfixRoot { get; set; } = "hotfix";

    public string BuildTag { get; set; } = "";

    public string DebugWatcher { get; set; } = "Off";
}
