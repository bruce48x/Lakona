# 8. Standards Conformance

This pass normally reviews edits since a verified baseline. Follow the
[authority's comparison scope and baseline rules](../../../../docs/contributing/architecture-review.md#standards-comparison-scope).
An explicit commit range overrides the default current-worktree target.

## Comparison Procedure

1. Record `git rev-parse HEAD`, the requested scope, and
   `git status --porcelain=v1 --untracked-files=all` before reviewing.
2. Use an explicitly supplied baseline, or the reviewed commit from the most
   recent complete report covering the same scope. Resolve it with
   `git rev-parse --verify '<ref>^{commit}'` and verify it is an ancestor of the
   target commit with `git merge-base --is-ancestor <baseline> <target>`.
   If an explicit range cannot be resolved, report that scope as blocked;
   do not silently replace the requested range.
3. Without a usable prior baseline, perform an initial full-tree standards
   pass over every in-scope current tracked and non-ignored untracked file.
   Record `Baseline: None (initial full-tree pass)` and the reason. Do not
   infer a release commit from changelog dates or package versions.
4. With a valid baseline and the default current-worktree target, review the
   union of these surfaces, keeping staged and unstaged edits distinct:

   ```bash
   git diff <baseline> HEAD
   git diff --cached HEAD
   git diff
   git ls-files --others --exclude-standard
   ```

   Read untracked file contents as additions; a filename list is not coverage.
   Do not reduce this to a net diff that could hide staged edits reversed in
   the worktree. Apply the user's path restrictions to all four surfaces.
   For an explicit commit range, review that range and state that worktree
   changes are excluded unless the user also requested them.
5. Record the resolved baseline and target commit, comparison mode, worktree
   status, and covered files in the report. A complete report may nominate
   its reviewed `HEAD` as the next baseline only for the scope it covered;
   dirty files remain recorded separately, not represented by that SHA.

## Rule Coverage

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

If the session offers the `code-review` skill, reuse its Standards evidence
only when its scope, baseline, target, and worktree coverage match this pass.
Record the report path and review any uncovered surfaces directly; a commit-only
report does not cover current staged, unstaged, or untracked changes.
