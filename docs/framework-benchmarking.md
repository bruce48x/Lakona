# Cross-Framework Game Server Benchmarking

This document defines the design contract for the framework-neutral
macrobenchmark tool under `benchmarks/framework`. The implemented `local-dev`
profile provides a deliberately small, one-command experience. The publishable
multi-machine profile is specified separately and does not gate `local-dev`.

The version 1 comparison is Lakona and
[Pinus](https://github.com/node-pinus/pinus), from the Pomelo framework
lineage. These frameworks differ materially in runtime, language, dispatch,
and cluster model, so they are sufficient to test whether the workload and
adapter seams are genuinely framework-neutral. A direct Pomelo adapter is not
supported and requires a separately justified version and protocol contract.
Candidate names do not imply support, endorsement, or permission to
redistribute their source. Every run must pin an exact upstream revision and
respect that framework's license.

## Purpose and Boundary

The platform answers coarse-grained questions such as:

- How much request/response throughput can a framework sustain at an
  acceptable tail latency?
- What is the latency and throughput of a direct RPC between two cluster
  nodes?
- What additional cost appears when an RPC includes Actor or Entity lookup,
  routing, and forwarding?
- How do payload size, connection count, concurrency, and topology change the
  result?

This platform is not the benchmark implementation for an individual entry in
[Runtime Performance](./performance.md). A risk-register benchmark isolates a
specific Lakona path and guards a specific change. The cross-framework
platform treats each framework deployment as a system under test and measures
the complete path defined by a shared workload.

The neutral coordinator, protocols, result schema, and load generator must not
reference Lakona runtime assemblies. Lakona is one adapter beside the other
frameworks. `Lakona.Game.LoadTesting` may provide implementation support to the
Lakona adapter, but it is not the platform's neutral orchestration boundary.

The platform must not publish one aggregate framework score. Different
workloads answer different architectural questions, and collapsing them into
one number would hide the tradeoffs.

Version 1 results are development evidence, not publishable cross-framework
performance claims. Its load generator and server processes may share one
workstation, so the report must label the run as local and disclose that CPU,
memory, scheduler, and loopback contention can influence the result.

## Comparison Principles

1. **Compare semantics, not framework APIs.** A workload specifies observable
   behavior, payload, topology, and correctness rules. Each adapter maps that
   contract to its framework's native concepts.
2. **Separate controlled and native modes.** Results that control the wire
   protocol and serialization must never be mixed with results using each
   framework's recommended stack.
3. **Correctness precedes speed.** Every response must be validated. A run with
   missing, duplicate, corrupt, misrouted, or timed-out responses is not a
   successful throughput result.
4. **Measure distributions under declared load.** Report tail latency, offered
   load, achieved load, and errors rather than only averages or peak operations
   per second.
5. **Keep measurement outside the hot path where possible.** Lifecycle control
   and resource collection are out of band. Instrumentation added to one
   adapter must not give it materially different measured work.
6. **Make the run reproducible.** Source revisions, build commands,
   configuration, hardware, operating system, topology, and raw samples belong
   to the result.
7. **Disclose unavoidable asymmetry.** Actor models, schedulers, memory
   managers, serializers, and routing capabilities differ. The report must
   describe those differences instead of concealing them behind a common
   label.

## Delivery Profiles

### Local Version 1

Version 1 presents one user-facing command that checks prerequisites, builds
or locates both adapters, allocates ports, starts the required processes, waits
for readiness, runs the selected cases, validates responses, shuts everything
down, and writes a report. The user must not manually coordinate processes or
translate raw load-generator output.

The first release supports:

- Lakona and Pinus adapters;
- one-workstation execution with isolated server and load-generator processes;
- native mode, with every transport and serializer choice disclosed;
- `frontdoor.echo`, `cluster.direct`, and `cluster.routed`;
- 32-byte and 256-byte payloads;
- fixed outstanding concurrency of 1, 16, 64, and 256, subject to a declared
  safe ceiling;
- response validation, achieved throughput, p50, p95, p99, maximum latency,
  timeouts, and other error counts; and
- a console summary plus a self-contained Markdown or HTML report and the raw
  data required to diagnose a failed run.

The configuration and result schemas retain a measurement-mode field so
controlled mode can be added without redefining run identity. Version 1 does
not claim to isolate framework runtime cost from the framework's native wire
protocol and serializer.

### Publishable Profile

The publishable profile adds controlled mode, open-loop offered-rate tests,
coordinated-omission correction, separate machines, operating-system resource
collection, repeated independent runs, order randomization, environment
controls, and immutable evidence bundles. The requirements in
Reproducibility and Fairness apply to this profile. They guide the architecture
but do not block the local version 1 release.

## Platform Modules

The platform has five logically separate and independently replaceable
modules. Version 1 may ship them behind one command and in one repository; the
module boundaries do not require five separately installed products.

### Benchmark Specification

The specification owns versioned workload semantics, topology definitions,
parameter matrices, correctness rules, and the controlled data-plane protocol.
It is language-neutral and is the authority shared by every adapter.

### Coordinator

The coordinator builds or locates pinned framework revisions, starts the
declared processes, waits for readiness, applies the run policy, stops the
deployment, and assembles the immutable result bundle. It communicates with
adapters only through a process manifest and an out-of-band lifecycle contract.

Lifecycle operations must cover prepare, start, readiness, reset to a known
seed, diagnostics snapshot, and shutdown. They are never part of measured
request latency.

### Load Generator

The load policy, case command, histogram contract, and result schema are
framework-neutral. Controlled mode uses one standalone generator with no
dependency on a framework under test. Native mode necessarily uses an
adapter-owned driver for each framework's supported client protocol and
libraries. That driver runs in a separate process, timestamps requests inside
its native data path, and implements the same versioned load and result
contracts; per-request coordinator IPC must not become an extra measured hop.

Version 1 launches the selected native driver as a child process on the local
workstation and supports fixed-concurrency capacity runs. Publishable network
benchmarks place generators away from server nodes so their CPU, memory, and
scheduler do not compete with the deployment, and add fixed-rate open-loop
latency runs.

The generator must retain a mergeable latency histogram or equivalent raw
distribution. Open-loop runs in the publishable profile must account for
coordinated omission: a framework pause must appear as delayed or timed-out
work rather than silently reducing the offered request rate.

### Framework Adapters

One adapter owns the smallest framework-specific application that implements
the workloads and deployment topology. In native mode it also owns the driver
that speaks the ordinary client protocol while conforming to the neutral load
contract. An adapter declares:

- framework name, revision, runtime, build mode, and license source;
- supported workloads, modes, transports, and topologies;
- commands, ports, readiness checks, and node roles;
- serializer, transport, routing, and timeout configuration; and
- any framework-specific diagnostics that can be collected without changing
  the measured behavior.

An unsupported semantic capability is reported as unsupported. An adapter must
not substitute an easier workload or bypass normal framework dispatch. Native
mode should use an ordinary production-facing path and recommended
configuration, not a benchmark-only optimization unavailable to users.

### Metrics and Reporting

Resource collection runs out of process when the operating system permits it.
The reporter consumes only the common result schema and produces per-workload
tables and graphs. Framework-specific traces and memory diagnostics are
attached as evidence but are not automatically treated as cross-runtime
metrics.

## Measurement Modes

### Controlled Mode

Controlled mode uses one versioned binary frame format, the same payload bytes,
the same operation semantics, and the same success and error representation.
The request minimally identifies a request, operation, target Entity where
applicable, and payload. The response correlates the request and returns the
specified payload, checksum, state version, or error.

JSON is not suitable for the measured controlled data plane. Before
implementation, the exact frame layout, byte order, size limits, and checksum
algorithm must be frozen as a versioned protocol. Each adapter implements that
protocol through its framework without routing around normal dispatch.

Controlled mode answers how the framework's networking, scheduling, dispatch,
and RPC path behaves after removing default protocol and serializer choices as
variables.

### Native Mode

Native mode uses the transport, serialization, generated bindings, and routing
path recommended by that framework for the workload. It measures the result a
typical user can expect from the supported stack.

Because native mode changes more than the framework runtime, its reports must
identify every material protocol and configuration choice. Native and
controlled results must appear in separate tables and must not share a ranking.
Native mode provides the shortest path to a representative developer workflow.
Controlled mode requires frozen frame and adapter contracts.

## Standard Workloads

Every workload has a deterministic response that the load generator can
validate.

| Workload | Required semantics | Primary cost represented |
| --- | --- | --- |
| `frontdoor.echo` | Return the request payload unchanged | Complete unary request/response path |
| `frontdoor.checksum` | Return a specified checksum of the payload | Dispatch plus small deterministic work |
| `state.serial-increment` | Route by Entity ID, increment its state once, and return the new version | Mailbox or equivalent state serialization |
| `cluster.direct` | Node A calls a known service on Node B and returns its response | Direct inter-node RPC |
| `cluster.routed` | Node A resolves an Entity or Actor location, calls its owner, and returns its response | Lookup, routing, and inter-node RPC |
| `gateway.forward` | A gateway forwards the client request to a logic node and relays the response | Typical client-to-cluster path |

`cluster.routed` specifies observable routing semantics, not an Actor API. In
Lakona, the logical target may be resolved through Entity or Actor ownership.
In Pinus, a route or dispatcher may select a backend using a player, room, or
other logical key before the backend RPC is performed. Both adapters must use
their ordinary framework dispatch paths. If an adapter cannot perform this
semantic operation, it reports the workload as unsupported instead of adding
an artificial Actor layer or substituting `cluster.direct`.

An echo-only result is not sufficient to characterize a game-server framework.
At minimum, a published comparison must include a stateless front-door path, a
direct cluster call, and a stateful or routed workload.

The full parameter families are:

- payloads of 32 bytes, 256 bytes, 4 KiB, and 64 KiB;
- fixed outstanding concurrency of 1, 16, 64, 256, and 1,024, subject to an
  environment's declared safe ceiling;
- common offered rates selected below saturation, at the knee, and above the
  knee of the throughput curve; and
- enough Entity IDs and connections to distinguish one hot destination from a
  distributed workload.

Exact matrices are versioned with a suite. A report may run a documented subset
but may not silently change a workload definition.

The `local-dev` profile uses the documented Local Version 1 subset. Larger
payloads, additional concurrency levels, open-loop rates, hot-destination
cases, and distributed-Entity matrices are outside that profile.

## Standard Topologies

| Topology | Process placement | Question answered |
| --- | --- | --- |
| `local-dev` | Load generator and all required server processes on one workstation | Easy development comparison; not a network-cluster result |
| `single-node` | Load generator and one server on separate hosts | Front-door framework capacity |
| `same-host-cluster` | Two server processes on one host | Process and framework RPC overhead with minimal network variance |
| `lan-direct` | Load generator, Node A, and Node B on separate LAN hosts | Direct inter-node RPC under a real network hop |
| `lan-routed` | Separate load generator, gateway/source, directory if required, and target node | Routed or forwarded cluster behavior |

Loopback and same-host results must never be presented as network-cluster
results. Virtualized, containerized, and bare-metal profiles are separate
environments. Containers may pin build inputs, but host networking, CPU
placement, and container limits must be recorded because they can change the
result.

`local-dev` is implemented. The other topologies belong to the publishable
profile.

## Cluster RPC Measurement

Cluster RPC needs two complementary observations:

1. The externally observed workload starts at the load generator, enters Node
   A, performs the Node A to Node B call, and returns through Node A. This is
   the primary comparable result. The generator is a separate process in
   version 1 and a separate host in publishable network runs.
2. An optional source-node histogram measures only the Node A to Node B to Node
   A round trip. It is diagnostic because each framework must implement that
   timing inside its own runtime.

Every cluster suite also runs the corresponding front-door baseline with the
same payload and measurement mode. Reports show both absolute curves and the
relative cluster penalty. They must not subtract one latency from the other and
claim that the difference is pure RPC cost; queues and saturation make the
paths non-linear.

A raw transport echo may be included as an environment control. It describes
the network floor and load-generator capacity, not a framework score.

## Load Policy

The publishable profile uses complementary capacity and latency policies:

- A capacity run uses fixed outstanding concurrency and finds the achieved
  throughput curve, saturation point, errors, and latency growth.
- A latency run uses open-loop fixed offered rates. It includes common absolute
  rates across frameworks and rates derived from each framework's previously
  measured capacity.

Reporting only a percentage of each framework's own capacity can hide absolute
capacity differences. Reporting only one common rate can unfairly compare one
framework at idle with another beyond saturation. Both views are required.

Version 1 implements only the fixed-concurrency capacity policy. Its report
must not describe the highest observed point as a universal capacity limit or
compare tail latency at an undeclared offered load.

Each case has a declared startup timeout, warm-up policy, steady measurement
duration, drain timeout, and correctness threshold. Warm-up ends according to a
versioned policy, not a manually selected favorable interval.

## Required Results

Version 1 reports include:

- achieved requests per second;
- p50, p95, p99, and maximum observed latency;
- completed, rejected, corrupt, duplicate, timed-out, and disconnected counts;
  and
- framework revision, runtime, transport, serializer, build mode,
  configuration, local environment identity, and an explicit local-result
  disclaimer.

Publishable cross-framework reports additionally include:

- offered and achieved requests per second;
- p50, p95, p99, p99.9, and maximum observed latency;
- completed, rejected, corrupt, duplicate, timed-out, and disconnected counts;
- server CPU time, CPU utilization, physical-core count, and throughput per
  physical core;
- process resident or working-set memory;
- network bytes and packets sent and received; and
- framework revision, runtime, transport, serializer, build mode,
  configuration, and environment identity.

Allocation rate, garbage-collection pauses, allocator statistics, scheduler
queues, and traces are valuable diagnostics. They are not automatically
comparable between .NET, Lua, C, and other runtimes. Reports keep them in
framework-specific sections and must not rank frameworks by a shared
bytes-per-operation column unless the runtimes and measurement semantics are
equivalent.

## Reproducibility and Fairness

A publishable comparison must:

- use pinned framework commits or immutable releases;
- preserve the complete adapter source and effective configuration;
- use release or production builds with debuggers and verbose logging disabled;
- record CPU model, physical and logical cores, memory, operating system,
  runtime, power policy, CPU affinity, and network interface details;
- reserve the machines and avoid unrelated workloads during a run;
- place the generator away from measured server machines for LAN tests;
- prove that the generator and network are not the bottleneck with a control
  run;
- perform multiple independent process runs, not only repeated intervals in
  one process;
- rotate or randomize framework order to reduce thermal and temporal bias; and
- retain raw histograms, counters, validation results, and process logs.

The default published summary uses at least five valid independent runs and
shows variation as well as the central result. A failed correctness threshold
remains visible and cannot be discarded merely because another repetition was
faster.

Each publishable execution produces an immutable bundle containing at least:

```text
run-manifest.json       Suite, framework revisions, configuration, environment
summary.json            Common calculated metrics and validity
histograms/             Mergeable latency distributions
counters/               Time-series process and network observations
validation.json         Response and error accounting
logs/                   Coordinator and framework process logs
report.md               Human-readable comparison for this bundle
```

The schema and calculation version are recorded so old raw results can be
reprocessed without pretending that changed calculations are the same run.
Canonical throughput baselines belong in retained run artifacts or a trend
store, not as hardware-specific pass/fail constants in source control.

## Version 1 Implementation

The local native-mode implementation lives under `benchmarks/framework`. It
contains the versioned schemas and suites, one-command coordinator, process
ownership and cleanup, correctness validation, raw result bundle and report,
and native Lakona and Pinus adapters for `frontdoor.echo`, `cluster.direct`,
and `cluster.routed`.

The canonical command, prerequisites, filters, output layout, interruption
behavior, and validation commands are maintained in the
[local benchmark guide](../benchmarks/framework/README.md). A clean checkout
with those prerequisites must produce a useful report through the documented
command, and failures must identify the missing prerequisite, failed process,
or invalid response.

`local-dev` excludes a separately deployable platform, automated LAN
orchestration, controlled mode, and publication-grade environment controls.
Those capabilities deepen the same specification, lifecycle, adapter,
and result boundaries rather than create a parallel benchmark implementation.
