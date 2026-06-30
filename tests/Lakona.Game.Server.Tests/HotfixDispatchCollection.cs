using Xunit;

namespace Lakona.Game.Server.Tests;

internal static class HotfixDispatchCollectionNames
{
    public const string GlobalState = "Hotfix dispatch global state";
}

[CollectionDefinition(HotfixDispatchCollectionNames.GlobalState, DisableParallelization = true)]
public sealed class HotfixDispatchCollection;
