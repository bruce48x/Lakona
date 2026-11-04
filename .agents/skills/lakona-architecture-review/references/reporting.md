## Write Findings For The Maintainer

Give each finding a title that names the observable problem or the work it
makes harder. Write the opening paragraph in the report's language for a
Lakona maintainer who did not investigate the issue. Describe one concrete
operation in time order. Say what triggers the problem, what should happen,
and what happens or could happen instead. For a maintenance finding, show
which change takes extra work and why.

Put symbols, file links, evidence labels, and reproduction details after that
explanation. Introduce an unfamiliar term when the reader needs it and explain
its job. Distinguish the behavior a test actually observed from a possible
consequence inferred from the code.

For example, a Chinese report could introduce RELIABILITY-001 this way:

> **热更可能让一个已经开始的请求做到一半失败**
>
> 新版上线后，新请求走新版，已经开始的请求应该能用旧版做完。但旧请求接下来再找框架办事时，框架把这一步也当成了新请求，拒绝它继续使用旧版。Agar 的胜场结算先保存积分，再更新排行榜。如果两步之间发生热更，玩家的积分可能已经保存，这次排行榜更新却没有发出去。

The two-version test reproduced the rejection. The leaderboard consequence
follows from the sample's call order. State that distinction in the evidence.

Before returning the report, read each title and opening paragraph with the
evidence hidden. Could a maintainer explain when the issue appears, what they
expected, and what goes wrong or costs extra? Rewrite any opening that requires
a source link or knowledge of an internal symbol to answer those questions.

## Evidence And Triage

Report every supported finding; do not impose a numerical quota. Deduplicate
findings that share the same module, seam, cause, and remedy.

For each finding include:

- stable ID: `MACRO-###`, `MICRO-###`, `CONSUMER-###`, `PERF-###`,
  `RELIABILITY-###`, `EVOLUTION-###`, `OPS-###`, or `STANDARD-###`
- recommendation strength: `Strong`, `Worth exploring`, or `Speculative`
- scope and exact files or symbols
- observed evidence
- violated repository rule or design principle, when one exists; a
  `STANDARD-###` finding must always name the rule file and section
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

Use Markdown tables only where they improve comparison. Prefer headings and
short evidence-rich paragraphs for findings. Link repository files with
relative paths and line numbers where possible.

Before returning, run `git status --porcelain` and record its output in the
report to substantiate `Changes made by review` and the pre-existing worktree
changes; the tree must still contain anything that was pre-existing.

Return the absolute report path and a concise summary to the user. Do not ask
which finding to implement. Ask which findings they want to discuss.

## Discussion And Approval

Treat the Markdown report as the working record and discuss its findings in the
review session. Do not create an issue, ticket, branch, or implementation plan
as part of the review.

Only an explicit later decision that identifies an accepted finding can
authorize a separate implementation task. The review itself never authorizes
code changes.
