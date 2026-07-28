# NuGet Publishing

NuGet publishing is performed by GitHub Actions, not local manual pushes. Each
package version is declared by the `<Version>` property in its `.csproj`.

The main Linux workflow owns the release sequence. It builds and tests the
repository once, packs and publishes NuGet packages, and only then calls the
reusable Lakona Hub publishing workflow when Hub release inputs changed.
Lakona Hub must not regain an independent `push` trigger: a failed or skipped
NuGet publication must prevent the Hub release for that commit.

Any shippable library change under `src/**` that must reach NuGet must bump the
affected package version before pushing. Publishing uses `--skip-duplicate`, so
an unchanged published version can make CI succeed while silently skipping the
changed package.

Rules:

- Bump `<Version>` in every modified release package project, including small
  bug fixes.
- Do not bump for docs-only or test-only changes unless packed content changes.
- When a bumped library is consumed by generated scaffolding, update matching
  release-version data, template constants, sample references, and changelog
  milestones in the same change.
- Run the package dependency closure guard:

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Install the repository Git hooks once per clone so this same guard runs before
any commit whose staged files can affect package outputs:

```powershell
pwsh -NoProfile -File scripts/git/install-hooks.ps1
```

The hook delegates to the .NET NuGet and Hub release guards; it does not
maintain a second package graph or infer version bumps independently.

For local pack verification only:

```powershell
New-Item -ItemType Directory -Force artifacts/nuget | Out-Null
Get-ChildItem src -Filter *.csproj -Recurse | ForEach-Object {
  dotnet pack $_.FullName --no-restore -c Release -o artifacts/nuget
}
```
