namespace Lakona.Tool.Server;

internal sealed record ServerPackageWriteRequest(
    string PublishedAppDirectory,
    string HotfixPackagePath,
    string OutputDirectory,
    string EntryAssembly,
    string RuntimeIdentifier,
    string Configuration,
    string Version,
    string BuildTag,
    DateTimeOffset BuiltAtUtc);
