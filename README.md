# Lakona

Lakona is a C# game server framework monorepo that combines typed RPC, process-local actor execution, and higher-level game server infrastructure.

The first migration pass keeps the existing architecture but renames the public surface to Lakona:

- `Lakona.Rpc.*` for RPC runtime, transports, serializers, analyzers, and starter tooling.
- `Lakona.Actor` for process-local actor/mailbox execution.
- `Lakona.Game.*` for game server hosting, cluster routing, hotfix runtime, client helpers, generators, and the Lakona tool.

