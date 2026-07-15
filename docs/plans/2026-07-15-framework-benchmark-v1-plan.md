# Framework Benchmark Version 1 Implementation Plan

**Status:** Active
**Date:** 2026-07-15
**Authority:** [Cross-Framework Game Server Benchmarking](../framework-benchmarking.md)
**Branch:** `codex/framework-benchmark-v1`
**Lifecycle:** Delete after version 1 decisions are absorbed into durable docs.

## Progress

- [x] Slice 1: neutral contracts, coordinator, fake process integration, and
  validation script (`2026-07-15`).
- [x] Slice 2: Lakona and Pinus `frontdoor.echo` (`2026-07-15`).
- [x] Slice 3: `cluster.direct` (`2026-07-15`).
- [ ] Slice 4: `cluster.routed`.
- [ ] Slice 5: complete version 1 user experience.

## Outcome

Deliver a local, native-mode comparison of Lakona and Pinus that a contributor
can run from a clean checkout with one command:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1
```

The command checks prerequisites, prepares both adapters, starts and stops all
processes, runs a smoke suite, validates every response, and writes a console
summary plus a report bundle. A full version 1 matrix is available through the
same entry point:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1 -Suite v1
```

Version 1 succeeds when a contributor can distinguish a valid run, an invalid
workload result, and a tool or environment failure without reading source code
or manually managing a process.

## Scope

Version 1 includes:

- Lakona and Pinus, each pinned to the exact source/package inputs used;
- the local development topology only;
- native framework transports, serializers, client libraries, dispatch, and
  cluster routing;
- `frontdoor.echo`, `cluster.direct`, and `cluster.routed`;
- 32-byte and 256-byte payloads;
- fixed outstanding concurrency of 1, 16, 64, and 256;
- warm-up followed by a fixed steady measurement window;
- achieved throughput, p50, p95, p99, maximum latency, and complete outcome
  accounting; and
- deterministic Markdown and JSON output with logs and mergeable histograms.

Version 1 does not include controlled mode, open-loop offered-rate runs,
coordinated-omission correction, LAN orchestration, containers, CPU or network
counters, historical trend storage, CI performance gates, Pomelo as a second
Node.js adapter, or a public NuGet/npm package.

The report must call every version 1 run a local development result. It must
not produce an aggregate framework score, claim a network-cluster result, or
present the largest measured point as a universal capacity limit.

### Complexity guardrails

Version 1 has exactly two built-in adapter locations and does not implement
plugin discovery, a public adapter SDK, remote agents, a daemon, a database, a
web dashboard, or HTML rendering. The report is Markdown plus JSON. Add an
abstraction only when both Lakona and Pinus use it in the current slice; do not
generalize for hypothetical third frameworks. Stop development when the five
version 1 acceptance conditions pass and move additional ideas to the durable
document's publishable profile.

## Implementation Decisions

### One entry point, multiple isolated processes

`run.ps1` is the only user-facing entry point. It forwards validated options to
a neutral .NET coordinator. The coordinator starts framework servers and one
native load-driver process per case, captures stdout and stderr, waits for
structured readiness, enforces timeouts, and kills the complete process tree
on cancellation or failure.

The user experiences one tool even though the measured topology contains
multiple processes. No server, driver, or adapter is installed globally.

### Neutral coordinator, framework-native drivers

Native mode cannot use one framework-independent data-plane client: the
Lakona driver needs the supported Lakona client stack and the Pinus driver
needs the Pomelo-compatible Pinus client protocol. Per-request IPC through the
coordinator would add a different measured hop and is therefore forbidden.

Instead:

1. The neutral coordinator writes a versioned case command as JSON.
2. The selected adapter starts its native driver as a separate process.
3. The driver opens native persistent connections before warm-up.
4. The driver owns request timestamps and the fixed-concurrency loop.
5. The driver writes one versioned result and histogram file after drain.
6. The coordinator validates the files, assembles the run bundle, and renders
   the comparison.

The load algorithm, histogram parameters, result schema, and conformance
fixtures are shared specification. Their implementations are necessarily .NET
for the Lakona driver and TypeScript for the Pinus driver. Before source work
begins, update `docs/framework-benchmarking.md` to name this native-driver seam
explicitly so the implementation does not contradict the neutral load-
generator boundary.

### Do not extend `Lakona.Game.LoadTesting` for cross-framework results

`Lakona.Game.LoadTesting` remains the game-client virtual-user package. Its
current recorder intentionally caps retained latency samples and models
application scenarios, which is not the complete histogram and outcome model
required here. The benchmark may reuse its naming and scheduling lessons, but
must not add cross-framework schemas, Pinus concepts, coordinator lifecycle,
or benchmark-specific reporting to the shippable package.

### Native client choices are part of the result

The Lakona adapter uses public generated RPC/client and cluster APIs with its
declared production transport and serializer. The Pinus adapter uses the
ordinary connector request path and Pinus RPC routing. Neither adapter may use
private APIs, an in-process shortcut, a benchmark-only server bypass, or raw
sockets that avoid normal framework dispatch.

The initial implementation records the exact transport, serializer, client
library, runtime, build mode, package version, lockfile hash, source URL, and
license URL. Changing any of these changes run identity.

### Persistent connection policy

Each outstanding-concurrency slot owns one persistent client connection and
allows one in-flight request. Connections are established before warm-up and
remain open through drain. Therefore concurrency and connection count are
equal in version 1 and are both recorded in the result.

This policy is intentionally simple and identical at the semantic level. A
future suite can separate connection count from outstanding concurrency
without changing the version 1 suite.

### Deterministic payload and validation

For a case, payload bytes are generated from the suite seed, payload size, and
request ID. Every request carries:

- a monotonically increasing request ID unique to the driver process;
- the logical target key where applicable; and
- exactly the declared number of application payload bytes.

Every successful response returns the request ID, the payload unchanged, and
the identity of the server node that performed the terminal operation. The
driver validates all three before recording success. Missing, duplicate,
corrupt, misrouted, rejected, timed-out, or disconnected operations are
separate counters; any such count makes the case invalid even if throughput is
high.

### Histogram contract

Both drivers record all completed request latencies in an HDR-style histogram
configured by the suite: microsecond units, 1 microsecond lowest discernible
value, 60 seconds highest trackable value, and three significant digits. The
neutral on-disk representation stores bucket boundaries and counts rather than
runtime-specific binary objects.

Golden fixtures cover bucket selection, merge, p50, p95, p99, and maximum.
Both implementations must produce the same results for the same fixture before
their performance output is accepted. Average latency is not a required
comparison metric.

## Workload Mapping

| Workload | Lakona native path | Pinus native path | Validation |
| --- | --- | --- | --- |
| `frontdoor.echo` | Native client RPC -> generated server dispatch -> echo service | Pomelo-compatible client request -> connector handler | Same request ID and payload; terminal node is the front-door node |
| `cluster.direct` | Front-door RPC -> generated Lakona.Rpc request over TCP to a configured worker -> response through front door | Connector handler -> RPC to an explicitly selected backend server ID -> response through connector | Terminal node is the configured direct worker |
| `cluster.routed` | Front-door RPC -> `IRouteDirectory` lookup through `IClusterRouter` -> owning worker -> response | Connector handler -> registered Pinus router using logical target key -> selected backend RPC -> response | Terminal node equals the deterministic owner for the target key |

The routed topology starts two eligible backend workers. A stable hash of the
logical target key selects the expected owner. The test target set includes
keys owned by both workers so a hard-coded single destination cannot pass.

For Lakona routed workloads, use public node and route registration plus the
production cluster transport. Its direct workload uses generated unary RPC
because `IClusterNodeSender` is a one-way delivery API that returns only an
acceptance status, while `cluster.direct` requires the worker response. For
Pinus, use normal server discovery, router registration, and
backend RPC. If the exact Pinus release exposes different public names, adapt
the implementation to that release without changing the observable semantics;
record the mapping in the adapter README.

## Repository Layout

```text
benchmarks/framework/
  README.md
  run.ps1
  FrameworkBenchmark.slnx
  suites/
    smoke.json
    v1.json
  schemas/
    adapter-manifest.schema.json
    suite.schema.json
    case-command.schema.json
    case-result.schema.json
    run-manifest.schema.json
  src/
    FrameworkBenchmark.Contracts/
    FrameworkBenchmark.Coordinator/
  adapters/
    lakona/
      README.md
      adapter.json
      FrameworkBenchmark.Lakona.Contracts/
      FrameworkBenchmark.Lakona.Server/
      FrameworkBenchmark.Lakona.Driver/
    pinus/
      README.md
      adapter.json
      package.json
      package-lock.json
      tsconfig.json
      src/server/
      src/driver/
      test/
  tests/
    FrameworkBenchmark.Tests/
    fixtures/
scripts/framework-benchmark/
  check-framework-benchmark.ps1
artifacts/framework-benchmark/       # generated and gitignored
```

The benchmark solution is separate from `Lakona.slnx`; it may reference
Lakona source projects only from the Lakona adapter. Neutral projects must
pass an architecture test proving they do not reference `src/Lakona.*` or the
Pinus adapter. No benchmark project is packable.

## Contracts

### Suite

`suite.schema.json` owns:

- suite ID and schema version;
- workload IDs;
- framework selection;
- payload sizes and concurrency values;
- connection policy;
- deterministic seed and target-key count;
- startup, readiness, warm-up, measurement, request, drain, and shutdown
  timeouts; and
- histogram parameters and correctness threshold.

`smoke.json` runs both frameworks and all three workloads with a 32-byte
payload, concurrency 16, 2-second warm-up, and 5-second measurement. `v1.json`
runs the documented payload and concurrency matrix with a 5-second warm-up and
15-second measurement. These durations can change only by versioning the
suite; command-line overrides make a run ad hoc and are recorded visibly.

### Adapter manifest

`adapter.json` declares framework identity, immutable version inputs, license,
runtime prerequisite, prepare/build commands, server roles, native driver
command, supported workloads, readiness event, and material transport,
serializer, routing, and timeout configuration.

Commands are argument arrays, not shell command strings. The coordinator
expands only documented placeholders such as `${runDir}`, `${caseFile}`,
`${resultFile}`, and allocated port names. Unknown placeholders or duplicate
ports fail before any process starts.

### Process lifecycle

Servers write newline-delimited lifecycle events to stdout:

```json
{"event":"ready","role":"frontdoor","nodeId":"frontdoor-1","endpoints":{"client":"ws://127.0.0.1:20000"}}
```

Human diagnostics go to stderr. The coordinator treats process exit before
readiness, malformed lifecycle output, duplicate readiness, startup timeout,
and unexpected exit during a case as tool failures. It always attempts a
graceful stop and then kills the process tree after the shutdown timeout.

Lifecycle events are outside the measured request path. Reset is implemented
by restarting the local deployment in version 1; no benchmark control RPC is
added to the data plane.

### Case result

`case-result.json` includes case identity, validity, timestamps, achieved
requests per second, outcome counts, histogram, driver runtime, connection
count, request timeout, and adapter metadata. Counts must satisfy:

```text
started = completed + timedOut + disconnected + canceledAtDrain
completed = succeeded + rejected + corrupt + misrouted
```

`duplicateResponses` is an additional observation rather than a terminal
request outcome because a request may already have completed when an extra
response arrives. Any nonzero duplicate count still invalidates the case. The
coordinator rejects negative counts, unknown outcomes, identity mismatch,
schema mismatch, histogram/count disagreement, or missing adapter metadata.

### Run bundle

Each invocation writes:

```text
artifacts/framework-benchmark/<UTC timestamp>-<run id>/
  run-manifest.json
  summary.json
  validation.json
  histograms/
  logs/
  report.md
```

Temporary case commands and partial results remain under the same run
directory. The manifest marks an interrupted run incomplete rather than
silently removing it. Reports use ordinal, invariant formatting and stable
case ordering: workload, payload, concurrency, framework.

## Command-Line Experience

`run.ps1` supports only the options needed for version 1:

```text
-Suite smoke|v1          Default: smoke
-Framework all|lakona|pinus
-Workload all|frontdoor.echo|cluster.direct|cluster.routed
-NoPrepare               Require existing restored/build outputs
-Output <directory>      Override artifact root
-KeepProcessesOnFailure  Diagnostic opt-in; never the default
```

Preflight checks PowerShell 7, .NET 10 SDK, Node.js, npm, lockfile state, port
availability, and write access to the artifact directory. Missing
prerequisites produce one actionable error each. Build and restore output is
captured in the run logs instead of flooding the benchmark summary.

Exit codes are stable:

- `0`: every selected case completed and passed correctness validation;
- `1`: the tool ran, but one or more workload cases were invalid;
- `2`: prerequisite, build, lifecycle, schema, or internal tool failure; and
- `130`: user cancellation.

## Delivery Slices

Each slice ends in a runnable command and focused tests. Do not implement all
server applications first and defer integration until the end.

### Slice 1: Freeze contracts and run fake adapters

Create the directory skeleton, schemas, smoke/v1 suite files, neutral contracts
project, coordinator CLI, PowerShell entry point, and a programmable fake
adapter fixture. Its success driver writes a valid result; failure modes cover
early exit, readiness timeout, malformed or duplicate readiness, corrupt
output, and cancellation.

Tests:

- schema-valid and schema-invalid golden files;
- deterministic suite expansion and stable case IDs;
- safe placeholder expansion and port allocation;
- stdout/stderr capture and readiness parsing;
- timeout, cancellation, process-tree cleanup, and incomplete-run retention;
- histogram conformance and percentile fixtures; and
- architecture scan proving neutral projects do not reference a framework.

Acceptance:

```powershell
dotnet test benchmarks/framework/tests/FrameworkBenchmark.Tests `
  --filter FullyQualifiedName~FakeAdapterIntegration
```

The fixture produces a deterministic valid bundle through the coordinator,
while every fake failure maps to exit code 2 and leaves no child process alive.
Fake adapters are test inputs and are not exposed by the public PowerShell
entry point.

### Slice 2: End-to-end `frontdoor.echo`

Implement the Lakona front-door server and native .NET driver, then the Pinus
connector handler and TypeScript driver. Pin the Pinus package and compatible
client dependencies in `package-lock.json`; record their source and licenses.

Tests:

- exact 32-byte and 256-byte payload round trips;
- request-ID correlation under concurrency;
- corrupt, duplicate, timeout, and disconnect classification;
- warm-up exclusion and drain accounting; and
- a smoke run for each adapter independently before the combined run.

Acceptance:

```powershell
pwsh -NoProfile -File benchmarks/framework/run.ps1 -Workload frontdoor.echo
```

runs both native stacks, validates every response, and renders a two-row local
comparison without manual startup.

### Slice 3: Add `cluster.direct`

Extend both deployments with a front-door/source process and a known backend
worker. The front door performs the framework-native inter-process RPC and
returns the worker's terminal-node identity.

Tests:

- requests reach the configured backend rather than execute locally;
- backend unavailable and backend timeout are classified correctly;
- readiness waits for both source and backend; and
- shutdown removes both process trees after success and failure.

Acceptance: the smoke suite renders front-door and direct-cluster curves for
both frameworks, and all direct responses identify the configured worker.

### Slice 4: Add `cluster.routed`

Start two backend workers. Register stable target ownership in Lakona and an
equivalent logical-key router in Pinus. Generate enough deterministic keys to
exercise both owners and return the selected owner in every response.

Tests:

- shared routing fixtures map each key to the same expected owner semantics;
- both workers receive traffic;
- unknown and stale routes fail validation rather than falling back to direct;
- a deliberately wrong owner is counted as misrouted; and
- routed requests use normal framework dispatch as documented in each adapter
  README.

Acceptance: `cluster.routed` completes for both adapters, proves both owners
were exercised, and reports the relative cluster penalty beside the matching
front-door baseline without subtracting latencies.

### Slice 5: Complete the version 1 user experience

Add full-matrix expansion, combined reporting, preflight guidance, rerun
instructions, material metadata capture, interrupted-run handling, and the
dedicated validation script.

`scripts/framework-benchmark/check-framework-benchmark.ps1` performs offline
schema/unit checks plus the fake-adapter integration suite. An explicit switch
enables real Lakona/Pinus smoke tests because they require Node package restore
and take longer.

Acceptance:

1. A clean checkout succeeds with the documented one-command smoke path.
2. `-Suite v1` runs 48 cases: 2 frameworks x 3 workloads x 2 payload sizes x
   4 concurrency values.
3. Repeating the same command preserves identical suite and case identities
   while creating a new run identity.
4. Ctrl+C, startup failure, and invalid response leave no default child
   processes behind and produce actionable output.
5. The report prominently labels local/native limitations and never ranks an
   aggregate score.

## Verification Before Version 1 Completion

Run from the benchmark worktree:

```powershell
pwsh -NoProfile -File scripts/rpc/check-docs-consistency.ps1
dotnet build Lakona.slnx --no-restore
dotnet test Lakona.slnx --no-build
dotnet build benchmarks/framework/FrameworkBenchmark.slnx --no-restore
dotnet test benchmarks/framework/FrameworkBenchmark.slnx --no-build
npm test --prefix benchmarks/framework/adapters/pinus
pwsh -NoProfile -File scripts/framework-benchmark/check-framework-benchmark.ps1
pwsh -NoProfile -File benchmarks/framework/run.ps1 -Suite smoke
```

Commands that need restore run once with the required network permission;
subsequent verification uses lockfiles and `--no-restore`. The full version 1
matrix is a release acceptance run, not a per-edit unit-test requirement.

## Documentation and Cleanup

During implementation:

- keep `docs/framework-benchmarking.md` as the durable authority;
- keep adapter setup and disclosed asymmetry in each adapter README;
- keep user commands and result interpretation in
  `benchmarks/framework/README.md`;
- do not add benchmark projects to NuGet package-version maintenance;
- ignore `node_modules`, build output, and benchmark artifacts; and
- remove this plan after version 1 is complete, moving any lasting decisions
  discovered during implementation into the authority document.

## External References Used for the Pinus Mapping

- [Pinus repository and relationship to Pomelo](https://github.com/node-pinus/pinus)
- [Pinus simple example](https://github.com/node-pinus/pinus/tree/master/examples/simple-example)
- [Pinus RPC API](https://pinus.io/api-reference/pinus-rpc/globals.html)

The implementation must still pin and record the exact Pinus release selected
at Slice 2; these moving documentation links are design references, not run
identity.
