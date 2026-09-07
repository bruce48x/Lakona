---
name: lakona-doc-maintenance
description: Audit or clean Lakona contributor documentation, stale plans, duplicate authorities, and documentation maps within the requested scope.
metadata:
  internal: true
---

# Lakona Doc Maintenance

Keep the contributor path focused on current rules and architecture. Preserve
useful present-day contracts; delete obsolete material instead of archiving it.

## Scope And Reading

Follow `CONTRIBUTING.md` and reuse applicable instructions already read. Root
and package README files are user-facing; leave them alone unless explicitly
included in scope. `docs/**` contains durable maintainer documentation.

| Work | Reference to read |
| --- | --- |
| Classify a document, merge duplicates, or decide what to remove | [classification.md](references/classification.md) |
| Assess documentation quality | [audit-checklist.md](references/audit-checklist.md) |
| Run a full audit's inventory, searches, and link verification | [operations.md](references/operations.md) |

A full audit completes six independent passes: factual consistency, ownership,
stale plans, duplication, competing authorities, and transitional wording.
Read the complete audit checklist for that mode. For reduced scope, read and run
each affected pass; justify any not-applicable decision with evidence. The full
operations reference is not required reading for a small scoped correction.

## Work And Completion

1. Identify the requested documents and their incoming references. Use `rg` to
   discover maintained files, references, and relevant headings.
2. Apply the relevant classification and audit guidance. Check factual claims
   against implementation, tests, configuration, or generated output.
3. Before deleting, check incoming links. Move any durable rule into its current
   authority and update affected links and the `CONTRIBUTING.md` map.
4. Edit within scope. Repeat affected checks after edits; leave no conflicting
   authority, broken incoming reference, or duplicate mutable fact caused by
   the cleanup. Remove temporary planning docs created during the cleanup.
5. Report evidence and clear/findings/not-applicable results for each required
   pass. Full audits also perform the operations reference's verification.
   Scoped corrections verify affected links and `git diff --check`.

Follow the repository's validation stopping rules. Do not present a partial
audit as complete or infer correctness merely from repeated documentation.
