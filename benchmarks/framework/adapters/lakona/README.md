# Lakona Framework Benchmark Adapter

Requires PowerShell 7, .NET 10, Git, and a running Docker engine. Preparation
builds repository sources in Release and records the commit plus dirty state.
Redis is pinned by digest; preparation pulls the image only when absent.

Each case starts an isolated Redis container and three ordinary Game servers:
`worker-1`, `worker-2`, and `frontdoor-1`. App state and Hotfix behavior use
generated framework binding. Nodes share Redis Membership with separate TCP
cluster endpoints. The coordinator removes Redis on success, failure, or
cancellation. Abruptly killing the coordinator cannot guarantee cleanup;
containers carry the label `dev.lakona.framework-benchmark=true`.

`frontdoor.echo` uses a generated `LakonaGameClient` over WebSocket with
MemoryPack. Each slot owns one persistent connection; Game handshakes finish
before warmup. Responses preserve request ID and payload and report the actual node.

`cluster.direct` keeps the same client-facing path, then the front door calls
the configured `worker-1` Game service through a generated client over
the production TCP transport. The worker returns its own terminal-node
identity through the front door. The connection is established during component
startup. This is an explicit known-owner RPC path without Actor lookup.

`cluster.routed` calls `ActorAccess.Route<BenchmarkActor>(key).CallAsync(...)`.
The native Actor Directory resolves ownership and invokes Hotfix behavior over
the internal TCP/MemoryPack cluster transport. Before readiness, all three
members must be active and 256 Actors are placed through native placement.
FNV-1a ownership spans both workers; each response's actual owner is checked.
Setup is excluded from timing; the measurement covers steady-state routing.

These workstation `local-dev` results use each framework's own transports and
wire formats. Pinus and Lakona have different internal costs; metadata records
those differences and the report does not assign an overall score.
