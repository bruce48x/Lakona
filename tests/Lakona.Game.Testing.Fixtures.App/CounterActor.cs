using System.Runtime.CompilerServices;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using MemoryPack;

[assembly: InternalsVisibleTo("Lakona.Game.Testing.Fixtures.Hotfix")]

namespace Lakona.Game.Testing.Fixtures.App;

public readonly record struct CounterId(string Value);

[NodeRole("battle")]
public sealed class CounterActor : Actor<CounterId>
{
    internal int Value;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class AddCounterRequest
{
    [MemoryPackOrder(0)]
    public int Delta { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class CounterReply
{
    [MemoryPackOrder(0)]
    public int Value { get; set; }
}
