# Lakona.Game.LoadTesting

`Lakona.Game.LoadTesting` provides engine-neutral load-test primitives for
headless Lakona.Game clients.

The package owns virtual user scheduling, operation timing, failure grouping,
and summary formatting. Game-specific login, matchmaking, room, chat, or other
business flows belong in the application or generated Console client that
references this package.
