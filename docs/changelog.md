# Changelog Maintenance

`CHANGELOG.md` is a concise record of significant Lakona milestones. It is not
a commit log or an inventory of every released patch.

## What To Record

Add or update a milestone when a change materially affects at least one of:

- public APIs, package boundaries, or compatibility;
- architecture, runtime lifecycle, routing, persistence, or deployment;
- generated-project workflows or supported client platforms;
- user-visible behavior whose delivery marks a meaningful product milestone.

Combine all work completed on the same date under one milestone, even when the
work spans multiple subsystems. Describe the day's significant outcomes and
their impact, not the sequence of implementation tasks.

Do not record routine refactoring, test stabilization, documentation cleanup,
CI maintenance, or isolated patch details unless they are essential to
understanding a milestone.

## Required Format

Use this structure:

```markdown
## YYYY-MM-DD — Milestone title

**Key releases:** `Package.Name 1.2.0` and `Other.Package 2.0.0`.

- One high-value summary item.
- Up to two additional summary items when needed.
```

Rules:

- Use the completion or release date in `YYYY-MM-DD` format.
- Use exactly one milestone per date; update that milestone when more
  significant work lands on the same day.
- List every package whose release is central to the milestone, using its exact
  package ID and semantic version.
- Omit `Key releases` only when the milestone did not publish or advance a
  package version. Never infer or invent a version.
- Keep each milestone to one to three bullets.
- Prefer one milestone title over `Added`, `Changed`, and `Fixed` subsections.
- Keep entries newest first.

## Review Checklist

Before committing a changelog update, verify that:

- the entry represents a durable milestone rather than routine activity;
- its date reflects when the milestone was completed or released;
- package IDs and versions match project files or the corresponding Git
  history;
- related changes have been combined and low-value details removed;
- the documentation consistency check passes.
