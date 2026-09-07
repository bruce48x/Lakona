# Full Documentation Audit Operations

Run these commands from the repository root for a full audit.

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
