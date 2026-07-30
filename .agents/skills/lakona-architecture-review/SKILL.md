---
name: lakona-architecture-review
description: Perform a manual, exhaustive, read-only Lakona repository review for macro architecture, micro code smells, generated-project friction, balanced performance, reliability and recovery, contract evolution and determinism, and operability and diagnosability. Use when the user explicitly invokes $lakona-architecture-review or requests a full architecture, complexity, stability, efficiency, consumer-experience, or framework-health review. Default to the entire current repository; use an incremental scope only when explicitly requested. Produce a Markdown report and never implement findings during the review.
metadata:
  internal: true
---

# Lakona Architecture Review

## Authority And Safety

1. Always read `CONTRIBUTING.md`,
   `docs/contributing/architecture-review.md`, `CONTEXT.md`, and
   `docs/design-philosophy.md` before reviewing. For a full review, read every
   current architecture authority linked by `CONTRIBUTING.md`. For an explicit
   reduced scope, read every authority whose area that scope touches and record
   the selection in the coverage ledger.
2. Treat the review as read-only. The review may write ignored artifacts only
   under the repository root's `.tmp/` directory. Do not edit tracked
   repository files, change package versions, create branches or commits, open
   pull requests, or implement a finding.
3. Preserve and report pre-existing worktree changes.
4. Resolve the repository root and write the report as Markdown to
   `.tmp/lakona-architecture-review-<yyyyMMdd-HHmmss>.md`. Create `.tmp/` when
   needed. Never place review reports or supporting artifacts in the operating
   system's temporary directory.
5. Place generated projects, benchmark output, coverage ledgers, and other
   review scratch artifacts under a review-specific subdirectory of the same
   repository-root `.tmp/`.

## Default Scope

Interpret `$lakona-architecture-review` with no qualifier as a full review of
the current repository, not as a recent-diff review.

Inventory all tracked, maintained material before analysis:

- runtime, generators, analyzers, transports, serializers, and tooling in
  `src/**`
- tests and repository guards in `tests/**`
- samples and starter-facing code in `samples/**`
- build, release, validation, and maintenance scripts
- project files, props, targets, workflows, and configuration
- current authorities and user-facing package documentation

Exclude build outputs, editor caches, vendored third-party code, binaries, and
deterministically generated artifacts. Record every exclusion in the coverage
section.

Use Git history to understand intent and recognize remnants, but do not limit
the default scope to recent changes. Honor a path, commit range, or incremental
scope only when the user explicitly supplies one.

An explicit path, commit range, or validation scenario overrides the full-review
default. Do not expand it into a full repository inventory. Inspect neighboring
callers, tests, authorities, and dependency edges only as supporting evidence,
and do not count them as reviewed scope.

In an explicit reduced scope, still assess every required pass. Run the full
generated-project procedure only when the scope affects generation, package
graphs, or consumer experience. Mark a pass `Not applicable` only when
repository evidence proves that the scope cannot affect it; explain that
decision in the coverage ledger.

## Required Independent Passes

Complete every pass even when an earlier pass finds strong problems. Do not let
a large finding suppress small findings. Zero findings in a pass is valid, but
skipping a pass is not.

### 1. Macro Architecture

Map modules, interfaces, seams, adapters, package dependencies, ownership, and
lifetimes. Look for:

- duplicate models, owners, runtime graphs, or sources of truth
- shallow modules and pass-through interfaces
- seams with only one real adapter
- package or assembly splits without independent ownership
- hidden fallback providers, ambient state, global replacement, and friend
  access
- scattered startup, shutdown, rollback, cancellation, or disposal ownership
- public compatibility surfaces without active behavior
- extension points created speculatively
- documentation that describes a cleaner architecture than the code implements

Apply the deletion test: if deleting a module removes complexity instead of
concentrating it behind a smaller interface, treat it as suspect.

### 2. Micro Code Smells

Review every maintained handwritten source, project, test, workflow, and script
file in the inventory. Inspect neighboring callers and tests when a smell
depends on usage. Look for small but real friction, including:

- strange branches, impossible states, ineffective guards, and silent fallback
- unused state, parameters, abstractions, options, and configuration
- one-line forwarding types, single-use helpers, and test-only production seams
- duplicate registration, conversion, validation, or cleanup logic
- stringly dispatch or reflection where typed information already exists
- nullable or boolean flags that hide lifecycle states
- unnecessary public surface and friend declarations
- dependencies constructed or resolved in surprising places
- ownership split between a method and its callers
- cancellation, disposal, async, and error handling that is correct only by
  convention
- names or indirection that force maintainers to jump between files to
  understand one concept

Do not report formatting preferences or generic style advice. A micro finding
must identify a concrete maintenance cost, failure risk, misleading contract,
or unnecessary concept. Keep its local scope visible instead of inflating it
into a broad architecture claim.

### 3. Generated-Project Experience

Treat generated projects as the strictest consumer test.

1. Read the current project-tooling authorities and discover the supported
   starter families and defaults.
2. Generate representative current default projects under the repository
   root's `.tmp/` review directory.
   Cover every supported client family when local tools permit it.
3. Inspect direct and transitive package dependencies, version declarations,
   generated project references, build assets, default files, and concepts
   exposed to users.
4. Simulate a package upgrade far enough to count the files, versions, and
   coordinated changes a user must make.
5. Build or run focused scaffold validation when feasible. Request required
   restore or external-tool permission instead of silently skipping it.
6. Record unavailable tools or blocked validation as coverage limitations.

Look specifically for dependency fan-out, independently versioned assets with
one owner, redundant packages, leaked implementation packages, multiple version
authorities, and starter concepts that do not earn their user-facing cost.

### 4. Performance And Resource Efficiency

Treat correctness and runtime contracts as constraints, not negotiable
performance variables. Read `docs/performance.md` and
`docs/framework-benchmarking.md`.

1. Identify static risks in hot paths, allocation, serialization, copying,
   batching, queues, locks, scheduling, timers, diagnostics, and network
   protocols.
2. Use an existing deterministic benchmark or regression harness when one
   exercises the real shared path. Do not infer material impact from code shape
   alone.
3. For quantitative claims, record workload semantics, runtime, build mode, OS,
   hardware, CPU count, GC mode, payload, topology, concurrency, offered load,
   warm-up, and measurement duration.
4. Measure the relevant vector: correctness and errors, throughput, offered
   load, p50/p95/p99 and maximum latency, CPU time and utilization, working set,
   allocation and GC behavior, network bytes and packets, and path-specific
   queue, contention, or delayed-work signals.
5. Compare alternatives through Pareto dominance. Reject an alternative that
   is no better on every relevant signal. Present trade-offs among the
   non-dominated alternatives against explicit workload, latency, resource,
   cost, and deployment constraints.

Do not declare a win from one metric while hiding regressions in another. Do
not publish a default aggregate score or invent weights across unlike metrics.
Calculate a composite score only when maintainers supply the weights and
deployment objective. Do not add benchmark-only production paths, weaken
correctness, or compare workloads with different semantics.

Label a static concern `Candidate`; label it `Measured` only after a repeatable
benchmark confirms material impact. An intentionally accepted trade-off must
state its bound or deployment constraint.

### 5. Reliability, Boundedness, And Recovery

Treat failure behavior as part of each module's interface. Build a failure
matrix covering:

- partial startup and rollback
- unavailable, slow, or unhealthy dependencies
- timeout and cancellation at every async seam
- disconnect, reconnect, duplicate, out-of-order, and stale work
- Hotfix load, publication, rollback, unload, and replacement
- graceful and forced shutdown
- overload, slow consumers, and saturated downstream modules

For every queue, cache, registry, retry loop, buffer, timer, background task,
connection, and retained generation, identify:

- its owner
- its capacity or other bound
- expiry, eviction, backpressure, or rejection behavior
- its stop, cancellation, or disposal condition

Look for failure amplification across sessions, Actors, endpoints, nodes, and
processes. Verify that recovery preserves ordering, idempotency, state identity,
and explicit lost-state outcomes. Treat malformed input, unbounded input rate,
and obvious resource-exhaustion paths as reliability findings; leave a complete
adversarial security audit to the security workflow.

### 6. Contract Evolution And Determinism

Distinguish obsolete compatibility shims from published contracts that must
remain stable. Inspect:

- RPC and notification IDs, wire formats, serialized member order, and error
  semantics
- stable state and type identity shared across Hotfix generations
- source-generator and project-renderer determinism
- startup and registration order that may depend on reflection, file order, or
  container enumeration
- Unity 2022 LTS, C# 9.0, IL2CPP, and ordinary .NET compatibility
- direct and transitive package versions and their single source of truth
- configuration defaults and upgrade behavior across supported topologies

The same supported input and configuration must produce the same generated
shape and runtime decisions. Report accidental nondeterminism even when one run
usually succeeds.

### 7. Operability And Diagnosability

Check whether maintainers can detect and localize failures without attaching a
debugger:

- readiness must reflect partial startup, stopping, unhealthy dependencies, and
  lost framework state truthfully
- errors must identify the owner, lifecycle phase, and cause without leaking
  payloads, request values, or user data
- metrics, traces, and events must distinguish network, serialization, queue,
  dispatch, application, and recovery delay
- metric tags must remain low-cardinality
- control and diagnostics paths must remain isolated from the measured data path
- diagnostics cost must not create material allocation, contention, or bandwidth
  regressions

Prefer a small diagnostic interface with high leverage over many counters and
logs that still fail to identify the responsible module.

## Verification Integrity

For every reliability, evolution, operability, or performance finding, state
whether the evidence is static, reproduced, measured, or blocked. Identify the
verification needed to accept or reject it:

- contract or lifecycle test
- fault injection
- deterministic concurrency test
- stress or soak test
- focused benchmark
- repository guard

Do not claim runtime impact, recovery correctness, absence of leaks, or
diagnostic sufficiency from static inspection alone. Conversely, do not discard
a concrete static risk merely because no harness exists; report the missing
verification as part of the finding.

## Coverage Integrity

Maintain a coverage ledger while reviewing. For each top-level area, record:

- inventory count
- reviewed count
- excluded count and reasons
- relevant validation performed
- complete, not applicable, partial, or blocked status

Do not describe a report as full while any required pass or maintained area is
partial. If interrupted, save the partial Markdown report at its repository
root `.tmp/` path, label it `Status: Incomplete`, and continue from its ledger.
Never replace missing coverage with sampling or inference.

## Evidence And Triage

Report every supported finding; do not impose a numerical quota. Deduplicate
findings that share the same module, seam, cause, and remedy.

For each finding include:

- stable ID: `MACRO-###`, `MICRO-###`, `CONSUMER-###`, `PERF-###`,
  `RELIABILITY-###`, `EVOLUTION-###`, or `OPS-###`
- recommendation strength: `Strong`, `Worth exploring`, or `Speculative`
- scope and exact files or symbols
- observed evidence
- violated repository rule or design principle, when one exists
- current cost or credible failure mode
- performance status and measured trade-off vector when applicable
- evidence status and required verification
- deletion-test result when applicable
- counterevidence and uncertainty
- discussion question
- possible direction without detailed interface design or an implementation
  plan

Strength and size are separate. A small local smell may be `Strong`; a large
redesign may be `Speculative`.

Also record investigated suspicions that were rejected when doing so prevents a
future reviewer from repeating substantial work.

## Markdown Report

Use this structure:

```markdown
# Lakona Full Architecture Review

- Date:
- Commit:
- Status: Complete | Incomplete | Blocked
- Scope: Full repository | Explicit incremental scope
- Changes made by review: None
- Pre-existing worktree changes:

## Executive Summary
## Coverage
## Macro Architecture Findings
## Micro Code-Smell Findings
## Generated-Project Experience Findings
## Performance And Resource-Efficiency Findings
## Performance Trade-off Matrix
## Reliability, Boundedness, And Recovery Findings
## Contract Evolution And Determinism Findings
## Operability And Diagnosability Findings
## Verification Gaps
## Rejected Suspicions
## Coverage Limitations
## Recommended Discussion Order
```

Use Markdown tables only where they improve comparison. Prefer headings and
short evidence-rich paragraphs for findings. Link repository files with
relative paths and line numbers where possible.

Return the absolute report path and a concise summary to the user. Do not ask
which finding to implement. Ask which findings they want to discuss.

## Discussion And Approval

Treat the Markdown report as the working record and discuss its findings in the
Codex task. Do not create an issue, ticket, branch, or implementation plan as
part of the review.

Only an explicit later decision that identifies an accepted finding can
authorize a separate implementation task. The review itself never authorizes
code changes.
