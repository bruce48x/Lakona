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

4. Check references before deleting:

   ```powershell
   rg -n "path-or-filename-to-delete" CONTRIBUTING.md docs README.md CHANGELOG.md
   ```

5. Edit narrowly:
   - delete stale files
   - remove links to deleted docs
   - compact entry maps
   - replace completed phase sections with current contracts only when the
     contract is still useful

6. Remove temporary planning docs created during the cleanup before finishing.

## Verification

Run fresh checks before claiming completion:

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
