using Lakona.Game.Server;
using Lakona.Game.Server.Actors;

namespace FrameworkBenchmark.Lakona.Server;

[NodeRole("worker")]
public sealed class BenchmarkActor : Actor<string>;
