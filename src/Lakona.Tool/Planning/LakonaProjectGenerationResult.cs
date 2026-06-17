using Lakona.Tool.Execution;

namespace Lakona.Tool.Planning;

internal sealed record LakonaProjectGenerationResult(
    string RootPath,
    GitInitializationResult Git);
