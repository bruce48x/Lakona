# Architecture And Complexity Review

Lakona uses manual architecture reviews to discover structural friction,
over-design, ineffective code, local code smells, and generated-project
consumer costs before anyone proposes implementation work. Reviews also
identify performance risks across CPU, memory, latency, throughput, bandwidth,
allocation, garbage collection, and contention without optimizing one signal
in isolation.

## Trigger And Default Scope

Run the repository-local review explicitly from an agent session opened at the
repository root:

```text
$lakona-architecture-review
```

The default is a full review of the current repository. It is not limited to
recent commits or changed files. An incremental review is allowed only when the
request explicitly names a path, commit range, or reduced scope.

Reviews are manual. Lakona does not schedule or automatically trigger them.
Full reviews read all current architecture authorities. Explicit reduced-scope
reviews read the authorities that their scope touches and may mark a review
pass as not applicable only when repository evidence supports that decision.
An explicit path, commit range, or validation scenario overrides the full-review
default. Do not expand it into a full repository inventory or generate every
starter unless the named scope affects project generation. Neighboring code,
tests, and authorities may be inspected as supporting evidence without being
claimed as reviewed scope.

## Required Review Passes

Every full review independently completes:

1. **Macro architecture:** modules, interfaces, seams, adapters, package
   ownership, dependency direction, lifecycle ownership, duplicate models, and
   speculative extension points.
2. **Micro code smells:** every maintained handwritten source, project, test,
   workflow, and script file, including neighboring callers needed to judge
   apparently strange code.
3. **Generated-project experience:** actual starter output, direct and
   transitive dependencies, version authority, upgrade fan-out, leaked build
   assets, and user-facing concepts.
4. **Performance and resource efficiency:** correctness-preserving trade-offs
   across CPU, memory, bandwidth, throughput, latency distributions,
   allocation, garbage collection, queues, and contention.
5. **Reliability, boundedness, and recovery:** failure containment, rollback,
   cancellation, overload behavior, recovery, and an owner, limit, and
   termination condition for every long-lived resource.
6. **Contract evolution and determinism:** published protocol and serialization
   stability, Hotfix type identity, deterministic generation and startup, Unity
   compatibility, and a single package-version authority.
7. **Operability and diagnosability:** truthful readiness, actionable failures,
   low-cardinality diagnostics, control-plane isolation, and enough evidence to
   localize production faults without a debugger.
8. **Standards conformance:** checkable documented rules from
   `CONTRIBUTING.md`, `docs/contributing/engineering.md`, and
   `docs/contributing/testing.md`, reviewed against the change delta since the
   last recorded review baseline. Every breach cites the rule file and
   section. A documented repository standard is never generic style.

Finding a strong problem in one pass does not end or reduce another pass. Small
findings remain local instead of being inflated into broad redesigns.

The standards pass is delta-shaped. It reviews changes since the baseline
commit recorded by the most recent report, or since the most recent release
named in `CHANGELOG.md` when no baseline exists. Documented rule breaches are
hard findings, not design judgement, and must cite the rule.

## Coverage Standard

A full review starts with a tracked-file inventory and ends with a coverage
ledger. The ledger records reviewed and excluded material for each top-level
area — including each standards rule family, with scanned and finding counts —
and explains every exclusion.

Do not call a review complete when any required pass or maintained area is
partial. Interrupted work produces an incomplete report and resumes from its
ledger; it does not substitute sampling for full coverage.

Git history is evidence of intent and previous cleanup, not the default review
scope.

## Reports

Write every review report as a `.md` file under the current repository root's
ignored `.tmp/` directory using this path:

```text
.tmp/lakona-architecture-review-<yyyyMMdd-HHmmss>.md
```

Create `.tmp/` when needed. Put generated projects, benchmark output, coverage
ledgers, and every other review-created scratch artifact under a
review-specific subdirectory of the same `.tmp/`. Architecture reviews must
not use the operating system's temporary directory.

The report must identify its commit, scope, completion status, repository
changes made by the review, pre-existing worktree changes, coverage, macro
findings, micro findings, standards findings, generated-project findings,
performance findings and trade-offs, reliability findings, evolution findings,
operability findings, verification gaps, rejected suspicions, limitations, and
recommended discussion order.

Reports do not belong in tracked repository content by default. The ignored
`.tmp/` report is the normal local working record. `docs/plans/**` remains
available only when a maintainer explicitly requests a tracked temporary review
or handoff.

## Evidence Standard

Each finding identifies exact files or symbols, observed evidence, concrete
maintenance cost or credible failure mode, counterevidence, uncertainty, and a
discussion question. A standards finding must additionally cite the rule file
and section it breaches.

Use these recommendation strengths:

- `Strong`
- `Worth exploring`
- `Speculative`

Recommendation strength is independent of size. A small local smell can be
strong; a large redesign can remain speculative.

Do not report formatting preferences as architecture findings. Do not impose a
finding quota, and do not invent findings to fill a category.

Performance findings follow [Runtime Performance](../performance.md) and
[Cross-Framework Game Server Benchmarking](../framework-benchmarking.md).
Correctness precedes speed. A static concern remains a candidate until a
repeatable measurement confirms material impact.

Do not declare an improvement from one metric while hiding regressions in
another. Do not publish a default aggregate score or invent weights across CPU,
memory, bandwidth, throughput, and latency. Prefer Pareto comparisons: reject a
design dominated on every relevant signal, then present the remaining
trade-offs against explicit workload, latency, resource, and deployment
constraints. Calculate a composite score only when maintainers supply the
weights and deployment objective.

## Stability, Evolution, And Operability Evidence

Stable does not mean that a framework never fails. It means failures are
explicit, contained, bounded, recoverable where promised, and diagnosable.
Review partial startup, unavailable or slow dependencies, timeout,
cancellation, disconnect, reconnect, duplicate and out-of-order work, stale
events, Hotfix reload and unload, shutdown, and overload.

Every queue, cache, retry loop, buffer, registry, timer, background task, and
connection owner must have:

- one identifiable owner
- a declared capacity or other bound
- expiry, eviction, backpressure, or rejection behavior
- an explicit stop or disposal condition

Contract-evolution review distinguishes obsolete compatibility shims from
published promises that must remain stable. Check wire IDs, serialized member
order, state shared across Hotfix generations, generated output determinism,
startup-order determinism, supported runtime and language constraints, and
package-version ownership.

Operability review checks whether readiness and diagnostics expose the actual
owner, lifecycle phase, and cause of a failure. Diagnostics must remain
low-cardinality, avoid payload or user data, stay outside measured hot paths
where possible, and cost less than the uncertainty they remove.

Treat obvious malformed-input, unbounded-rate, and resource-exhaustion paths as
reliability findings. A complete adversarial security audit remains a separate
workflow.

Every stability conclusion identifies its verification path. Use a contract
test, fault injection, deterministic concurrency test, stress or soak test,
focused benchmark, or repository guard as appropriate. Static evidence may
justify a candidate; it does not prove runtime impact or recovery correctness.

## Discussion And Approval Workflow

A review is read-only. It may create ignored report and scratch artifacts under
the repository root's `.tmp/`, but it must not modify tracked repository files,
change versions, create a branch or commit, open a pull request, or implement a
finding.

The Markdown report is the working record. Discuss its findings directly in the
review session. A finding may be accepted, rejected, or deferred during that
discussion, but none of those states is inferred from the report alone.

Only an explicit later request naming an accepted finding starts a separate
implementation task. Do not create an issue, ticket, branch, or implementation
plan as part of the review.

After implementation:

- move durable rules into the relevant authority document
- add a repository guard when an objective invariant can prevent regression
- delete any temporary in-repository plan

When rejecting a finding for a durable architectural reason, record that reason
in the relevant authority so future reviews do not repeatedly propose it.
