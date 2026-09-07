# 8. Standards Conformance

Rule conformance is a property of edits, so this pass is delta-shaped, not
tree-shaped. Diff `HEAD` against the baseline commit recorded by the most
recent review report. Without a recorded baseline, use the most recent release
commit named in `CHANGELOG.md`. Record the commit this pass reviewed as the
baseline for the next run.

Read [standards-checklist.md](standards-checklist.md) before starting. For every rule
family in that inventory:

- review the in-scope diff, plus any file the diff proves violates a rule
  whose surface extends beyond it
- record scanned count, finding count, and the `Not applicable` justification
  in the coverage ledger, exactly like the other passes
- a finding must quote the offending hunk and cite the rule file and section —
  citing the rule is the finding's identity here, never an optional attribute

A documented repository standard is never generic style; do not route it
through the Pass 2 style filter. Hard rule breaches default to `Strong`.
Judgement-shaped breaches may be `Worth exploring` with the reasoning stated.

If the session offers the `code-review` skill, its Standards axis covers the
same ground for commit deltas. Adopt its most recent report for the baseline
window and record the report path instead of re-deriving the same findings.
Otherwise run this pass directly.
