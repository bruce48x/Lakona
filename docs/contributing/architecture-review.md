# Architecture And Complexity Review

Lakona uses manual architecture reviews to discover structural friction,
over-design, ineffective code, local code smells, and generated-project
consumer costs before anyone proposes implementation work. Reviews also
identify performance risks across CPU, memory, latency, throughput, bandwidth,
allocation, garbage collection, and contention without optimizing one signal
in isolation.

## Trigger And Default Scope

Run the repository-local review explicitly from a Codex task opened at the
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

Finding a strong problem in one pass does not end or reduce another pass. Small
findings remain local instead of being inflated into broad redesigns.

## Coverage Standard

A full review starts with a tracked-file inventory and ends with a coverage
ledger. The ledger records reviewed and excluded material for each top-level
area and explains every exclusion.

Do not call a review complete when any required pass or maintained area is
partial. Interrupted work produces an incomplete report and resumes from its
ledger; it does not substitute sampling for full coverage.

Git history is evidence of intent and previous cleanup, not the default review
scope.

## Reports

Write every review report as a `.md` file in the operating system's temporary
directory using this name:

```text
lakona-architecture-review-<yyyyMMdd-HHmmss>.md
```

The report must identify its commit, scope, completion status, repository
changes made by the review, pre-existing worktree changes, coverage, macro
findings, micro findings, generated-project findings, performance findings and
trade-offs, rejected suspicions, limitations, and recommended discussion
order.

Reports do not belong in the repository by default. `docs/plans/**` remains
available only when a maintainer explicitly requests a temporary in-repository
review or handoff.

## Evidence Standard

Each finding identifies exact files or symbols, observed evidence, concrete
maintenance cost or credible failure mode, counterevidence, uncertainty, and a
discussion question.

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

## Discussion And Approval Workflow

A review is read-only. It may create its temporary Markdown report, but it must
not modify repository files, change versions, create a branch or commit, open a
pull request, or implement a finding.

The Markdown report is the working record. Discuss its findings directly in the
Codex task. A finding may be accepted, rejected, or deferred during that
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
