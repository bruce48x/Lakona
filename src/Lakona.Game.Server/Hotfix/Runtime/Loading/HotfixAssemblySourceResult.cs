namespace Lakona.Game.Server.Hotfix.Loading;

public sealed record HotfixAssemblySourceResult(
    string? Version,
    string AssemblyPath,
    string BaseDirectory);
