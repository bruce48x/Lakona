## Evidence And Triage

Report every supported finding — no quota. Deduplicate findings sharing
module, seam, cause, and remedy.

Each finding includes:

- stable ID: `MACRO-###`, `MICRO-###`, `CONSUMER-###`, `PERF-###`,
  `RELIABILITY-###`, `EVOLUTION-###`, `OPS-###`, or `STANDARD-###`
- strength: `Strong`, `Worth exploring`, or `Speculative`
- scope: exact files or symbols
- observed evidence
- violated repository rule or design principle, when one exists; a
  `STANDARD-###` must name the rule file and section
- current cost or credible failure mode
- performance status and measured trade-off vector, when applicable
- evidence status and required verification
- deletion-test result, when applicable
- counterevidence and uncertainty
- discussion question
- possible direction — no detailed interface design or implementation plan

Strength and size are separate: a small local smell may be `Strong`; a
large redesign may be `Speculative`.

Also record investigated suspicions that were rejected when that saves a
future reviewer substantial rework.

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
- Standards baseline commit:

## Executive Summary
## Coverage Ledger
## Macro Architecture Findings
## Micro Code-Smell Findings
## Generated-Project Experience Findings
## Performance And Resource-Efficiency Findings
## Performance Trade-off Matrix
## Reliability, Boundedness, And Recovery Findings
## Contract Evolution And Determinism Findings
## Operability And Diagnosability Findings
## Standards Conformance Findings
## Verification Gaps
## Rejected Suspicions
## Coverage Limitations
## Recommended Discussion Order
```

Use Markdown tables only where they improve comparison; prefer headings
and short evidence-rich paragraphs for findings. Link files with relative
paths and line numbers where possible.

Before returning, run `git status --porcelain` and record its output in
the report to substantiate `Changes made by review` and pre-existing
worktree changes; the tree must still contain anything pre-existing.

Return the absolute report path and a concise summary. Do not ask which
finding to implement — ask which findings to discuss.

## Discussion And Approval

The Markdown report is the working record; discuss its findings in the
review session. Do not create an issue, ticket, branch, or implementation
plan as part of the review.

Only an explicit later decision identifying an accepted finding can
authorize a separate implementation task; the review itself never
authorizes code changes.
