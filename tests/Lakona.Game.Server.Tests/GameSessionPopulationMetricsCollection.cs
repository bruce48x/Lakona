using Xunit;

namespace Lakona.Game.Server.Tests;

internal static class GameSessionPopulationMetricsCollectionNames
{
    public const string Diagnostics = "Game session population metrics";
}

[CollectionDefinition(GameSessionPopulationMetricsCollectionNames.Diagnostics, DisableParallelization = true)]
public sealed class GameSessionPopulationMetricsCollection;
