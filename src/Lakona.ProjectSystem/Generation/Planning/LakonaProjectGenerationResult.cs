using Lakona.ProjectSystem.Generation.Execution;

namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed record LakonaProjectGenerationResult(
    string RootPath,
    GitInitializationResult Git);
