namespace Lakona.Tool.Server;

internal sealed record ServerPackOptions(
    string ProjectPath,
    string HotfixProjectPath,
    string OutputDirectory,
    string RuntimeIdentifier,
    string Configuration,
    string Version);
