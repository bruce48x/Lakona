# Testing

Tests must protect runtime contracts rather than mirror implementation details.

## Validation Scope And Completion

Select validation from the behavior changed, credible failure risks, and existing
coverage. Reuse relevant tests first; add or update tests only where a changed
contract or reproduced defect lacks meaningful coverage. A change need not
modify test files when existing tests already cover the affected behavior.
For reversible, low-impact edits that do not change behavior, do not add tests
that merely restate the implementation.

| Change | Default validation | Reason to expand |
| --- | --- | --- |
| Text, links, or skill instructions | Relevant document, metadata, link, and diff checks | Changed distribution or rendered output needs the corresponding checks |
| Local behavior | Build the affected dependency graph and run relevant behavioral tests | Missing coverage, a failure, or unresolved integration risk |
| IDs, DTOs, serializers, or generated APIs | Relevant generator, compatibility, and affected consumer checks | Additional engine, transport, or language compatibility risk |
| Resource startup, connectivity, or readiness | Relevant failure-path tests and real dependency startup checks | Changed node roles or topology |
| Process wiring or recovery | The relevant process/container E2E | Explicit full-topology coverage or an applicable gate |

Treat the coverage table below as a map of contracts to assess, not a demand to
run every listed scenario whenever any file in that area changes. Preserve
required evidence for the affected contracts: compilation alone does not prove
runtime behavior, and in-process tests do not prove process or socket behavior.

A test command may supply the required build when it covers the same dependency
graph, targets, and generator configuration. Avoid a separate duplicate build.
Use `--no-restore` or `--no-build` only when the required inputs and outputs are
known current. Complete applicable commit, push, CI, and release gates at their
required stage; local focused validation does not replace those gates.

Once relevant checks and current-stage gates pass, stop verification. Broaden or
repeat it only after new changes, failures, or a concrete unresolved risk, and
identify that reason. Report what was checked and any blocked or unverified
outcomes; do not count a skipped check as a pass.

## Coverage By Contract

The repository test script prints individual test results and writes TRX and
hang sequences beneath `artifacts/test/<configuration>/results/<project>`.
Five minutes without test progress aborts the test host, so stalled tests can
be identified before the CI job timeout. Real dependency-packaging tests also
cancel their build and run operations after three minutes per case.

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

The scheduled and manually dispatchable `Daily Validation` workflow runs seeded
multi-node Membership restart scenarios, the Actor Catalog/Directory consistency
suites, and the complete Membership Table contract against required real
in-memory, PostgreSQL, Redis, and MySQL providers alongside the Godot generated-project E2E matrix. Its jobs
remain independent so a failure in one suite does not hide results from the
other. The provider job supplies `LAKONA_TEST_POSTGRES_CONNECTION`,
`LAKONA_TEST_REDIS_CONNECTION`, and `LAKONA_TEST_MYSQL_CONNECTION`; a skipped
provider contract is not a passing daily result.

Each provider has an explicit workflow step and runs the shared
`MembershipTableContractTests` behavior suite. A Repository Guard keeps all
four concrete test classes, categories, service dependencies, connection
variables, and workflow filters connected. External-provider tests skip when
their service is absent unless the workflow step sets
`LAKONA_REQUIRE_MEMBERSHIP_PROVIDER_TESTS=true`. Mandatory provider steps use
that switch so a missing connection variable fails instead of silently reducing
Daily Validation coverage.

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
