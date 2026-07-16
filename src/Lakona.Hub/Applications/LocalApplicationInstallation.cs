namespace Lakona.Hub.Applications;

public enum LocalApplicationKind
{
    Rider,
    VisualStudio,
    VisualStudioCode,
    UnityHub,
    Unity,
    Godot,
    Other
}

public sealed record LocalApplicationInstallation(
    LocalApplicationKind Kind,
    string DisplayName,
    string ExecutablePath,
    string? Version = null)
{
    public override string ToString() => DisplayName;
}

internal sealed record ManualApplicationRegistration(
    LocalApplicationKind Kind,
    string DisplayName,
    string ExecutablePath);

internal static class LocalApplicationKinds
{
    public static IReadOnlyList<LocalApplicationKind> AutomaticallyDetectedKinds { get; } =
    [
        LocalApplicationKind.Rider,
        LocalApplicationKind.VisualStudio,
        LocalApplicationKind.VisualStudioCode,
        LocalApplicationKind.UnityHub,
        LocalApplicationKind.Unity,
        LocalApplicationKind.Godot
    ];

    public static int Order(LocalApplicationKind kind) => kind switch
    {
        LocalApplicationKind.Rider => 0,
        LocalApplicationKind.VisualStudio => 1,
        LocalApplicationKind.VisualStudioCode => 2,
        LocalApplicationKind.UnityHub => 3,
        LocalApplicationKind.Unity => 4,
        LocalApplicationKind.Godot => 5,
        LocalApplicationKind.Other => 6,
        _ => int.MaxValue
    };

    public static string DisplayName(LocalApplicationKind kind) => kind switch
    {
        LocalApplicationKind.Rider => "Rider",
        LocalApplicationKind.VisualStudio => "Visual Studio",
        LocalApplicationKind.VisualStudioCode => "VS Code",
        LocalApplicationKind.UnityHub => "Unity Hub",
        LocalApplicationKind.Unity => "Unity",
        LocalApplicationKind.Godot => "Godot",
        LocalApplicationKind.Other => "IDE",
        _ => kind.ToString()
    };

    public static bool IsServerEditor(LocalApplicationKind kind) => kind is
        LocalApplicationKind.Rider or
        LocalApplicationKind.VisualStudio or
        LocalApplicationKind.VisualStudioCode or
        LocalApplicationKind.Other;
}
