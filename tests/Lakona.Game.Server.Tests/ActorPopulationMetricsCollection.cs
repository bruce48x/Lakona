using Xunit;

namespace Lakona.Game.Server.Tests;

internal static class ActorPopulationMetricsCollectionNames
{
    public const string Diagnostics = "Actor population metrics";
}

[CollectionDefinition(ActorPopulationMetricsCollectionNames.Diagnostics, DisableParallelization = true)]
public sealed class ActorPopulationMetricsCollection;
