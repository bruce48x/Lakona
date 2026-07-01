# Large Cross-Cutting Change Workflow

This workflow is platform-neutral. It is written for human contributors and AI
agents, regardless of which coding-agent environment or model provider is in
use.

Use this workflow before implementing changes that span packages, public APIs,
runtime lifecycle, hot reload, scheduling, concurrency, source generation,
generated templates, sample migrations, or repository-wide documentation.

## Trigger

Treat a task as a large cross-cutting change when any of these are true:

- It is expected to modify two or more packages under `src/**`.
- It changes public APIs, package README examples, generated template output, or
  source generator behavior.
- It changes runtime lifecycle, hot reload, unload, scheduler, timer,
  concurrency, cancellation, dispatch, actor, session, or cluster behavior.
- It migrates a sample or starter project to a new runtime contract.
- It removes or replaces an existing framework surface.
- It is likely to touch more than 20 files or require more than one test
  project for meaningful validation.

If uncertain, classify the work as large until a scope checkpoint proves
otherwise.

## Scope Checkpoint

Before implementation, write a short checkpoint with:

- Goal: the user-visible or maintainer-visible behavior change.
- Affected surfaces: packages, samples, templates, docs, tests, and public APIs.
- Coupling assessment: which parts are strongly coupled and must stay under one
  implementation owner.
- Independent slices: helper-agent or parallelizable work with disjoint write
  scopes.
- Compatibility stance: whether breaking changes are acceptable and which old
  surfaces must be removed.
- Validation plan: exact test projects, smoke scripts, scans, and skipped tests.
- Versioning impact: packages under `src/**` that require version bumps.

Stop and ask for direction if the checkpoint shows the task is materially
larger than requested.

## Implementation Ownership

Use one continuity-preserving implementation owner for strongly coupled runtime
work. Examples include scheduler plus cancellation behavior, hot reload plus
runtime snapshots, dispatch plus ambient scope, or source generator plus runtime
activation.

Use helper agents only when the work is independent:

- documentation updates after the runtime contract is stable
- sample migration after the new API shape compiles
- source scans and checklist verification
- focused tests for an already-defined contract

Do not split strongly coupled runtime logic across many fresh agents by
default. Fresh agents lose local design context and tend to rediscover boundary
conditions late.

## Model Capability

Use platform-neutral capability levels:

- Fast or standard model: mechanical edits, docs wording, file moves, source
  scans, simple tests.
- Strong model: cross-module implementation, sample migration, generated code
  shape, integration debugging.
- Strongest available model: architecture, lifecycle, scheduler, concurrency,
  hot reload, public API design, and final review.

Tool-specific labels such as reasoning level or model names belong in the
tool adapter, not in this workflow.

## Milestones

Prefer small milestones that can be reviewed and integrated:

1. API shape and minimal compile path.
2. Runtime implementation and focused unit tests.
3. Hot reload, lifecycle, cancellation, or concurrency behavior.
4. Source generator or template changes.
5. Sample migration.
6. Documentation and package README examples.
7. Benchmark or performance measurement.
8. Final cleanup, version bumps, and integration.

Merge or rebase the base branch between milestones when the branch is long
lived or other agents are active.

## Review Strategy

Use risk-based review gates:

- Architecture review before implementation when public API, lifecycle,
  concurrency, or hot reload behavior changes.
- Focused implementation review after each high-risk milestone.
- Checklist review for mechanical migrations and docs.
- Final integration review across the complete diff before merging.

Reviewer prompts should include the scope checkpoint, base and head commits,
known skipped tests, and specific risks. Do not ask a reviewer to infer the
requirements from conversation history.

## Hygiene Checklist

Before final review or merge, run the relevant checklist:

- Public API: scan for old API names, removed surface references, and stale docs.
- Package examples: ensure README snippets use valid C# shapes and current API
  names.
- Versioning: bump every modified shippable `src/**` package version that must
  reach NuGet.
- Tests: run affected test projects sequentially when they share build outputs
  or global state.
- Samples: run focused business logic tests or smoke scripts for migrated
  samples.
- Benchmarks: provide a smoke path for new benchmark scripts.
- Generated output: verify templates or source generator tests cover the new
  shape.
- Integration: merge or rebase the base branch before final validation.
- Git hygiene: run `git diff --check` and inspect staged changes before
  committing.

If a required validation is intentionally skipped, record the exact reason and
the residual risk.

## Failure Handling

When validation fails:

- Read the full error before changing code.
- Reproduce with the smallest command that still fails.
- Identify whether the failure is product logic, test isolation, environment,
  stale generated output, package metadata, or merge drift.
- Make one minimal fix for the identified root cause.
- Re-run the smallest failing command, then the broader affected suite.

Do not weaken assertions or broaden timeouts until the root cause is understood.

## Completion Criteria

A large cross-cutting change is not complete until:

- the scope checkpoint's requirements are either implemented or explicitly
  removed from scope
- high-risk milestones have review coverage
- affected tests and scans pass on the final integrated branch
- skipped validations are documented with residual risk
- package versions and user-facing examples are consistent with the final code
