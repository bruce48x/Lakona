namespace Lakona.Hub.Applications;

public enum LocalApplicationKind
{
    Rider,
    VisualStudio,
    VisualStudioCode,
    Unity,
    Godot
}

public sealed record LocalApplicationInstallation(
    LocalApplicationKind Kind,
    string DisplayName,
    string ExecutablePath,
    string? Version = null)
{
    public override string ToString() => DisplayName;
}
