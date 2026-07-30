---
name: lakona-doc-maintenance
description: Audit, prune, and reorganize Lakona repository documentation. Use when the user asks to clean docs, reduce contributor documentation clutter, remove stale implementation plans, check whether a doc should be deleted, update CONTRIBUTING.md documentation maps, remove ULink-era wording, or perform periodic documentation maintenance in the Lakona repo.
metadata:
  internal: true
---

# Lakona Doc Maintenance

## Purpose

Keep Lakona's repository documentation current and low-noise. Prefer deleting
obsolete material over archiving it. Keep the default contributor path focused
on current architecture, active rules, and useful navigation.

## Authority And Scope

1. Read `CONTRIBUTING.md` first. It is the repository authority for contributor
   workflow and maintenance rules.
2. Treat `README.md` as user-facing. Do not edit it unless the user explicitly
   includes it in the cleanup scope.
3. Treat package `README.md` files as user-facing package docs.
4. Treat `docs/**` as durable contributor and maintainer documentation.
5. Treat completed implementation plans, obsolete roadmaps, migration notes, and
   history-only decisions as deletion candidates, not archive candidates.

## Classification

Classify every relevant doc before editing:

- **Current authority:** defines active workflow, package boundaries, runtime
  contracts, validation rules, or architecture.
- **Current supplement:** explains a current subsystem but is not an entry point.
- **User-facing:** belongs to `README.md`, package README files, samples, or blog.
- **Stale plan:** task list, implementation plan, roadmap, or phase document for
  work that has already landed.
- **History-only:** explains removed frameworks, migration mechanics, old package
  names, or old starter designs without current operational value.
- **Duplicate:** repeats content already covered by a clearer current authority.

Default actions:

- Keep current authority and current supplements.
- Leave user-facing docs alone unless explicitly scoped in.
- Delete stale plans and history-only docs.
- Merge only durable current rules from duplicates into the active authority,
  then delete the duplicate.

## Lakona-Specific Rules

- `CONTRIBUTING.md` should be a concise contributor entry point, not a full
  architecture manual.
- `CONTRIBUTING.md` documentation maps should link only current authoritative
  docs.
- Do not preserve removed framework branding, old package names, or migration
  history in current docs unless there is an active compatibility reason.
- Do not create a new archive bucket just to save old decisions.
- If a historical decision still matters, rewrite it as a current rule in the
  relevant authority document.
- Move valuable content from `docs/superpowers/**` into permanent `docs/**`
  documentation, then clean up `docs/superpowers/**`.
- Remove empty archive directories after deleting their contents.

## Complete Quality Audit Checklist

For a full documentation audit, complete all six passes independently. Do not
stop after finding one class of problem. Record each pass as **clear**,
**findings**, or **not applicable**, and cite the files, implementation, tests,
or generated output used as evidence. For a reduced-scope audit, run every pass
that can be affected by the scoped change; do not mark a pass not applicable
without checking.

1. **Factual and semantic consistency**
   - Compare claims across authoritative docs and against the implementation,
     tests, configuration, and generated output when those are the real
     evidence.
   - Verify mutable facts such as defaults, public API signatures, project
     layout, ownership, failure behavior, delivery guarantees, and runtime
     contracts.
   - Treat a consistently repeated claim as unverified until it agrees with the
     system it describes.

2. **Ownership and boundary clarity**
   - Ensure each cross-cutting contract has one clearly named authoritative
     owner and that related docs use the same boundary.
   - Check commonly confused boundaries explicitly: configuration versus
     runtime behavior, stable App code versus Hotfix code, Application HTTP
     versus Management or RPC surfaces, and cluster-wide versus endpoint-local
     concerns.
   - Resolve vague, overlapping, or contradictory ownership statements in the
     relevant authority document.

3. **Stale plans and history on the current documentation path**
   - Find completed plans, roadmaps, phase checklists, migration instructions,
     implementation diaries, resolution summaries, and superseded decisions.
   - Rewrite any still-valid rule as a present-tense contract in the relevant
     authority document.
   - Delete the remaining history-only artifact instead of moving it to an
     archive.

4. **Duplication and update fan-out**
   - Search for mutable facts copied across multiple docs, especially complete
     configuration blocks, exact API signatures, defaults, project trees, and
     generated examples.
   - Keep one canonical definition. Replace other copies with the minimum
     context needed by their readers plus a link to the authority.
   - Preserve useful explanation and deliberate terminology repetition; remove
     duplication only when it creates competing maintenance surfaces.

5. **Competing authority mechanisms**
   - Treat the documentation map in `CONTRIBUTING.md` as the sole registry of
     authoritative contributor documentation.
   - Confirm every mapped target exists and every document treated as a current
     authority is represented in that map.
   - Remove document-local currentness metadata such as `Status`, `Date`,
     `Audience`, or `Last reviewed`, and remove self-declared authority labels
     that compete with the map.
   - When implementation changes architecture, configuration, public APIs,
     generated output, or runtime contracts, confirm the same change updates
     every affected authoritative document.

6. **Historical and transitional wording**
   - Review terms such as `legacy`, `old`, `removed`, `historical`, `migration`,
     `first implementation`, `current iteration`, and future-phase language.
   - Replace historical narration with the current positive contract, current
     non-goal, or current constraint.
   - Preserve wording that has present operational meaning, including protocol
     versions, active compatibility guarantees, release history, and the
     current App/Hotfix generation distinction.
   - Remove completed-resolution tables and before/after narratives when they
     no longer guide current work.

## Workflow

1. Read `CONTRIBUTING.md`.
2. Inventory docs and references:

   ```powershell
   Get-ChildItem -Path . -Include *.md -Recurse -File |
     Where-Object { $_.FullName -notmatch '\\.git\\|\\bin\\|\\obj\\|\\Library\\|\\Temp\\' } |
     Select-Object FullName,Length |
     Sort-Object FullName
   ```

3. Scan headings to identify plans, roadmaps, archive sections, and duplicate
   entry points:

   ```powershell
   rg -n "^#|^##|^###" CONTRIBUTING.md docs CHANGELOG.md
   ```

4. Run all six passes in the Complete Quality Audit Checklist and keep an
   evidence-backed result for each pass.

5. Check references before deleting:

   ```powershell
   rg -n "path-or-filename-to-delete" CONTRIBUTING.md docs README.md CHANGELOG.md
   ```

6. Edit narrowly:
   - delete stale files
   - remove links to deleted docs
   - compact entry maps
   - replace completed phase sections with current contracts only when the
     contract is still useful

7. Repeat the affected checklist passes after editing. A cleanup is incomplete
   if it fixes one document while leaving a conflicting authority or duplicate
   mutable fact elsewhere.

8. Remove temporary planning docs created during the cleanup before finishing.

## Verification

Run fresh checks before claiming completion:

- Confirm the six audit passes have explicit results and evidence.
- Confirm every current authority is mapped by `CONTRIBUTING.md` and every
  mapped target exists.
- Re-run the searches that found duplicate facts, competing authority markers,
  and transitional wording; review every remaining match as intentional.

```powershell
rg -n "deleted-file-name|deleted-directory-name" CONTRIBUTING.md docs README.md CHANGELOG.md
```

```powershell
$errors = @()
$files = @('CONTRIBUTING.md','CHANGELOG.md') + (Get-ChildItem -Path docs -Recurse -Filter *.md | ForEach-Object { $_.FullName })
foreach ($file in $files) {
  $full = if ([System.IO.Path]::IsPathRooted($file)) { $file } else { Join-Path (Get-Location) $file }
  $text = Get-Content -Raw -LiteralPath $full
  $matches = [regex]::Matches($text, '\[[^\]]+\]\(([^)]+)\)')
  foreach ($match in $matches) {
    $target = $match.Groups[1].Value
    if ($target -match '^[a-z]+:' -or $target.StartsWith('#') -or $target.StartsWith('mailto:')) { continue }
    $clean = ($target -split '#')[0]
    if ([string]::IsNullOrWhiteSpace($clean)) { continue }
    if ($clean -notmatch '\.md$') { continue }
    $resolved = Join-Path (Split-Path -Parent $full) $clean
    if (-not (Test-Path -LiteralPath $resolved)) { $errors += "$file -> $target" }
  }
}
if ($errors.Count -gt 0) {
  $errors | ForEach-Object { Write-Output $_ }
  exit 1
}
```

```powershell
git diff --check
```
