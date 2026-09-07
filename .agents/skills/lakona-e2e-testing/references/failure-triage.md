# E2E Failure Triage

Inspect the failing phase and its artifacts before rerunning. Apply the entrypoint's authorization boundaries when fixing any failure.

1. **Pack failure** (LocalFeed only)
   - Check the failing `src/<Package>/<Package>.csproj`.
   - Check version metadata and missing packed files.
   - If package source changed under `src/**`, verify the relevant `<Version>` was bumped according to `CONTRIBUTING.md`.

2. **Scaffold failure**
   - Inspect `src/Lakona.Tool/Cli`, option parser behavior, and `docs/tool/generation-architecture.md`.
   - Treat deprecated CLI options in older scripts as script drift, not product regressions.
   - Current `new` options are `--name`, `--output`, `--client-engine`, `--client-engine-version`, `--transport`, `--serializer`, `--membership-provider`, `--nugetforunity-source`, and `--deploy-profile`.

3. **Restore or build failure in generated project**
   - Inspect the generated `NuGet.config`, `Server/App/Server.App.csproj`, `Shared/Shared.csproj`, and local feed contents.
   - Check whether the generated package versions match the locally packed package versions.
   - Check analyzer and generator packages first when generated types are missing.
   - For ProjectReference mode: verify csproj patching replaced the correct PackageReference elements.

4. **Runtime verification failure**
   - Inspect generated server stdout/stderr (`server-out.txt`, `server-err.txt`) and the E2E client log.
   - Classify by transport connection, serializer payload, RPC dispatch, DI/hotfix loading, or contract mismatch.
   - Check for "Lakona server started successfully". Do not treat ASP.NET's earlier "Application started" message as Lakona readiness because Startup Actors may still be activating.
   - Prefer a narrow framework fix over committing generated RPC glue or broad template rewrites.

5. **E2E client build failure**
   - Check that the E2E client can resolve all Lakona types.
   - For ProjectReference mode: verify all ProjectReference paths exist.
   - For LocalFeed mode: verify the local feed contains all needed packages.
   - For NuGetOrg mode: verify the published package versions match what the scaffold expects.
   - Source generator failures: check `CompilerVisibleProperty` items and analyzer references.

6. **Wrapper/script failure**
   - If the wrapper assumptions diverge from current generator behavior, update the wrapper or skill first.
   - Do not hide real product failures by weakening assertions.
