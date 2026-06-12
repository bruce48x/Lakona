---
name: lakona-local-package-e2e
description: Use when validating Lakona.Tool generated projects against the latest local Lakona NuGet packages before publishing, when avoiding waits for nuget.org propagation, or when investigating scaffold/build/runtime failures that may be caused by package contents, package versions, source generators, transports, serializers, or game framework templates. Produces local-package verification results and improvement proposals for maintainer confirmation before code changes.
---

# Lakona Local Package E2E

Validate generated Lakona projects with NuGet packages packed from the current repository instead of waiting for published packages.

Use this skill to answer: "Do the latest local packages create, restore, build, and optionally run a generated project correctly?"

## Required Context

Always read `CONTRIBUTING.md` first. It is the repository authority for package boundaries, version bump rules, Unity constraints, and validation expectations.

When the failure involves project generation architecture, read `docs/tool/lakona-tool-refactor-architecture.md` before proposing fixes.

If comparing with existing broad E2E behavior, read `.claude/skills/lakona-e2e-testing/SKILL.md`, but prefer this skill for local NuGet feed verification.

## Wrapper

Use the bundled script as the default entry point:

```powershell
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1
```

Common runs:

```powershell
# Fast default smoke: Godot + websocket + memorypack, scaffold and server build.
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1

# Same smoke with runtime RPC verification.
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1 -Runtime

# One Unity-facing generated project build.
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1 -Engine unity -Transport kcp -Serializer memorypack

# Full matrix. Use only when the user asks for release-grade confidence or the change has broad blast radius.
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1 -Engine all -Transport all -Serializer all -Runtime

# Keep generated scaffolds for inspection.
.\.claude\skills\lakona-local-package-e2e\scripts\run-local-package-e2e.ps1 -KeepScaffolds -Runtime
```

The wrapper:

- Packs all `src/Lakona.*.csproj` projects into `.tmp/lakona-local-package-e2e/feed`.
- Uses `.tmp/lakona-local-package-e2e/packages` as an isolated NuGet package cache.
- Runs `dotnet run --project src/Lakona.Tool -- new` without deprecated options.
- Writes `NuGet.config` into each generated project so local packages resolve before nuget.org.
- Builds the generated server solution.
- Optionally starts the generated server and runs a temporary RPC verification client.
- Writes a Markdown report and JSON summary under `.tmp/lakona-local-package-e2e`.

## Validation Strategy

Choose the smallest run that can answer the question:

- Default smoke: `godot + websocket + memorypack`.
- Tool template or generated layout change: run the affected engine plus the affected transport/serializer.
- Transport change: run the changed transport with both serializers.
- Serializer change: run the changed serializer across at least websocket and one socket transport.
- Source generator or shared contract shape change: run `godot + websocket + memorypack -Runtime` first, then expand if it fails or passes but risk remains.
- Pre-release confidence: run the full matrix with `-Runtime`.

Do not claim package-level confidence from repository tests alone. The point of this skill is to validate the package restore surface that generated users experience.

## Failure Triage

Classify failures before proposing code changes.

1. **Pack failure**
   - Check the failing `src/<Package>/<Package>.csproj`.
   - Check version metadata and missing packed files.
   - If package source changed under `src/**`, verify the relevant `<Version>` was bumped according to `CONTRIBUTING.md`.

2. **Scaffold failure**
   - Inspect `src/Lakona.Tool/Cli`, option parser behavior, and `docs/tool/lakona-tool-refactor-architecture.md`.
   - Treat deprecated CLI options in older scripts as script drift, not product regressions.
   - Current `new` options are `--name`, `--output`, `--client-engine`, `--transport`, `--serializer`, `--persistence`, `--nugetforunity-source`, and `--deploy-profile`.

3. **Restore or build failure in generated project**
   - Inspect the generated `NuGet.config`, `Server/App/Server.App.csproj`, `Shared/Shared.csproj`, and local feed contents.
   - Check whether the generated package versions match the locally packed package versions.
   - Check analyzer and generator packages first when generated types are missing.

4. **Runtime verification failure**
   - Inspect generated server stdout/stderr and the temporary E2E client output.
   - Classify by transport connection, serializer payload, RPC dispatch, DI/hotfix loading, or contract mismatch.
   - Prefer a narrow framework fix over committing generated RPC glue or broad template rewrites.

5. **Wrapper/script failure**
   - If the wrapper assumptions diverge from current generator behavior, update the wrapper or skill first.
   - Do not hide real product failures by weakening assertions.

## Output Contract

After running this skill, report:

- Exact command run.
- Combination matrix covered.
- Report path under `.tmp/lakona-local-package-e2e`.
- Pass/fail count.
- Most likely root cause for each failure.
- Whether the problem appears to be the framework, generated template, package metadata, or test wrapper.
- Concrete improvement options, with a recommended option.

Stop after analysis and proposed improvements unless the user explicitly approves implementation.
