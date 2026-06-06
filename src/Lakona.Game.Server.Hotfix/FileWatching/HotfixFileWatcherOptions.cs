namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixFileWatcherOptions
{
    public string Directory { get; set; } = "hotfix/current";

    public string Filter { get; set; } = "*.dll";

    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(1);
}
