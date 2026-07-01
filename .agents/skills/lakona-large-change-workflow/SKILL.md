---
name: lakona-large-change-workflow
description: Platform-neutral workflow for planning, implementing, reviewing, and integrating large cross-cutting Lakona changes. Use when an AI agent or contributor is asked to change multiple packages, public APIs, runtime lifecycle, hot reload, scheduling, concurrency, source generation, generated templates, sample migrations, or repository-wide documentation.
---

# Lakona Large Change Workflow

## Overview

Use this skill as a thin entry point to Lakona's platform-neutral large-change
workflow. The durable workflow lives in
`docs/agent-workflows/large-cross-cutting-change.md` so any capable coding
agent can follow it without depending on one vendor's terminology or tools.

## Required Reading

1. Read `CONTRIBUTING.md`. It is the repository authority.
2. Read `docs/agent-workflows/large-cross-cutting-change.md`.
3. Apply that workflow before starting implementation or when recovering a
   large change already in progress.

## Platform Neutrality

Use the workflow's generic terms in prompts and plans:

- AI agent or implementation owner
- helper agent or reviewer agent
- fast, standard, strong, or strongest available model
- isolated workspace, branch, or equivalent environment
- checkpoint, milestone, review gate, and validation command

Do not write plans that depend on one platform's agent names, reasoning labels,
or orchestration features. If the current tool has platform-specific controls,
map them from the generic workflow locally.

## Output Contract

After applying the workflow, report:

- whether the task is large cross-cutting or not
- the scope checkpoint, if it is large
- the planned milestones and review gates
- which parts require one continuity-preserving owner
- which helper-agent tasks are safe because they are independent
- the validation and hygiene checklist for the work
