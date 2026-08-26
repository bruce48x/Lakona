# Testing

Tests must protect runtime contracts rather than mirror implementation details.

| Area | Required coverage when changed |
| --- | --- |
| Actor messaging | Dispatch, responses, timeout, response validation, dead letters |
| Actor mailbox | Ordering, non-concurrency, backpressure, stop drain, metrics |
| Actor lifecycle | Startup, graceful stop, rollback, disposal |
| Actor tooling | Generated extensions, clients, source shape, diagnostics |
| RPC runtime | Encoding, dispatch, cleanup, admission, protocol limits |
| Transports | Cancellation, disconnect, backpressure, framing, security |
| Serializers | Roundtrips, compatibility, failure behavior |
| Starter/tooling | CLI, dependency planning, layout, template output |
| Game sessions | Resume, cleanup, callbacks, token validation, reliable push |
| Cluster | Lookup, expiry, dispatch, stale registration, node restart |
| Hotfix | Dispatch, reload, unload, watching, accessors, fallback |
| Unity samples | EditMode or PlayMode coverage for runtime behavior and shape |

## Multi-node integration tests

Use `Lakona.Game.Testing` when one test needs several independent Lakona hosts
and the real Membership, Actor Directory, activation catalog, routing, or node
lifecycle. The package replaces only the infrastructure boundary which would
otherwise require processes and sockets: all nodes share one in-memory
Membership Table and use a programmable in-memory cluster network.

The test fixture, not `Lakona.Game.Testing`, owns application databases and
caches. Start PostgreSQL, MySQL, Redis, or another dependency once per fixture
and pass its connection details through `ConfigureNodes`. Role checks should
keep data-only resources out of gateway and battle nodes, just as production
configuration does.

Keep these layers distinct:

| Test layer | What it proves |
| --- | --- |
| Unit test | One component's rules and edge cases. |
| `Lakona.Game.Testing` | Several real Lakona hosts coordinate correctly in one process. |
| Provider contract | The real PostgreSQL or other provider obeys its storage contract. |
| Process/container E2E | Real sockets, process death, deployment wiring, and application traffic work together. |

An in-process `KillNodeAsync` cancels host shutdown and skips the graceful
Membership path. It models abrupt framework lifecycle interruption, but it is
not an operating-system process kill.

The scheduled and manually dispatchable `Cluster Nightly` workflow runs seeded
multi-node Membership restart scenarios, the Actor Catalog/Directory consistency
suites, and the complete Membership Table contract against a required real
PostgreSQL service. The PostgreSQL job must provide
`LAKONA_TEST_POSTGRES_CONNECTION`; a skipped provider contract is not a passing
nightly result.

The local Agar three-node E2E keeps a Unity client in an active match while
`data-1` is restarted. After the topology change, the client must recover both
connections, remain in the match, and receive at least ten newer world ticks.
The test then restarts `gateway-1` separately to verify graceful shutdown and
exact-incarnation replacement.

Unity tests use NUnit and Unity Test Framework. Use `[UnityTest]` with
`IEnumerator` for asynchronous Unity tests and alias assertions with
`using NUnitAssert = NUnit.Framework.Assert;`.

Source-scan tests that read `src/**` must be updated when source files move or
are renamed.

For solution runs that exceed local tool timeouts, execute test projects
sequentially with the same isolated artifacts root used by `scripts/test.ps1`:

```powershell
$repositoryRoot = git rev-parse --show-toplevel
$artifactsPath = Join-Path $repositoryRoot 'artifacts/test'
$projects = Get-ChildItem tests -Recurse -Filter '*.csproj' | Sort-Object FullName
foreach ($project in $projects) {
  dotnet test $project.FullName --artifacts-path $artifactsPath
  if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($project.FullName)" }
}
```
