# Lakona.Game.LoadTesting

`Lakona.Game.LoadTesting` provides engine-neutral load-test primitives for
headless Lakona.Game clients.

The package owns virtual user scheduling, operation timing, failure grouping,
and summary formatting. Game-specific login, matchmaking, room, chat, or other
business flows belong in the application or generated Console client that
references this package.

Latency percentiles cover the entire run with fixed memory per operation name.
See the [latency summary contract](https://github.com/bruce48x/Lakona/blob/main/docs/performance.md#loadtesting-latency-summaries)
for precision, memory bounds, and concurrent recording behavior.
