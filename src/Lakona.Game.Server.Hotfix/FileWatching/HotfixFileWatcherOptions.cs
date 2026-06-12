namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixFileWatcherOptions
{
    public string Directory { get; set; } = "hotfix";

    public string Filter { get; set; } = "reload.signal";

    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(1);
}
