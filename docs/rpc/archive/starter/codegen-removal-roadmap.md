# Lakona.Rpc.CodeGen Removal Roadmap

Status: complete

Date: 2026-05-25

Archive note: this is a completed removal record, not current implementation guidance. Current generation rules live in [Source Generation](../../starter/source-generation.md).

Progress:

- 2026-05-25: Steps 1-3 landed for source-generator validation and sample migration. Samples no longer keep committed generated RPC glue and .NET/Godot sample builds rely on `Lakona.Rpc.Analyzers`.
- 2026-05-25: Step 4 landed. `lakona-starter codegen`, starter legacy project-tool codegen execution, and bundled CodeGen version resolution were removed.
- 2026-05-25: Step 5 landed. The `Lakona.Rpc.CodeGen` project, its test project, and test solution references were removed.
- 2026-05-25: Step 6 landed. Public docs and contributor guidance now describe source generation as the normal workflow.

## Decision

`Lakona.Rpc.CodeGen` was a legacy migration tool and has been removed after source-generator validation.

Lakona.Rpc is still in internal development, so this removal does not need a long compatibility window. Do not design new features around preserving the CLI codegen path.

## Removal Gate

Deletion was gated on these checks:

- `Lakona.Rpc.Analyzers` is the only generator implementation used by starter-generated projects.
- Unity 2022 LTS, Unity CN, and Tuanjie compile `Rpc.Generated` client APIs through the analyzer/source-generator path.
- Server, Godot, Unity, Unity CN, and Tuanjie starter smoke tests cover generated client/server glue without committed generated source.
- Source-generator tests cover service clients, notification binders, facade shape, server binders, notification proxies, id constants, referenced contract assemblies, and failure diagnostics.
- Public docs and starter README no longer teach `Lakona.Rpc.CodeGen` as a normal workflow.

## Steps

1. Expand analyzer coverage
   - Move remaining behavior assertions from `tests/Lakona.Rpc.CodeGen.Tests` to source-generator-focused tests.
   - Keep parser/emitter tests only while they protect code still used by the analyzer path.

2. Validate Unity analyzer delivery
   - Prove the Unity-compatible starter imports `Lakona.Rpc.Analyzers` as a Roslyn analyzer/source-generator asset.
   - Add a Unity/Tuanjie smoke test that fails if `Rpc.Generated.RpcClient` is missing.
   - Do not add generated-source fallback files.

3. Migrate samples
   - Remove committed generated RPC glue from samples unless a sample is explicitly testing legacy migration.
   - Make sample builds rely on analyzer output.

4. Remove starter legacy command surface
   - Delete `lakona-starter codegen`.
   - Delete `StarterProjectTool`, `StarterCodeGenCommandOptions`, `ClientCodeGenMode`, and `ClientCodeGenOutput`.
   - Remove `CodeGen` from `ReleaseVersions.json` and version resolution.

5. Delete CLI package
   - Remove `src/Lakona.Rpc.CodeGen`.
   - Remove CLI-only tests and `tests/Lakona.Rpc.CodeGen.Tests` cases that no longer apply.
   - Remove `Lakona.Rpc.CodeGen` from `tests/Tests.slnx`.

6. Clean docs
   - Remove normal-workflow references to `Lakona.Rpc.CodeGen`.
   - Keep a short migration note only if there are still internal projects requiring manual cleanup.

## Non-Goals

- Do not preserve `Lakona.Rpc.CodeGen` for external compatibility.
- Do not introduce a second generator implementation for modern .NET and Unity separately.
- Do not reintroduce generated-source starter workflows to bridge temporary Unity import issues.

## Completion Criteria

The removal is complete when a clean checkout can run all tests and generate every starter target without `src/Lakona.Rpc.CodeGen`, `lakona-starter codegen`, generated RPC source directories, or local tool manifests.

Completed on 2026-05-25.
