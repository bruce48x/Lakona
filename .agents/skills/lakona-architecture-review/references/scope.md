## Authority And Safety

1. Follow the scoped reading policy in `CONTRIBUTING.md`. Read
   `docs/contributing/architecture-review.md`, `CONTEXT.md`, and
   `docs/design-philosophy.md` before reviewing. For a full review, read every
   current architecture authority linked by `CONTRIBUTING.md`. For an explicit
   reduced scope, read every authority whose area that scope touches and record
   the selection in the coverage ledger.
2. Treat the review as read-only. The review may write ignored artifacts only
   under the repository root's `.tmp/` directory; a maintainer explicitly
   requesting a tracked report under `docs/plans/` is the only exception. Do
   not edit tracked repository files, change package versions, create branches
   or commits, open pull requests, or implement a finding.
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
