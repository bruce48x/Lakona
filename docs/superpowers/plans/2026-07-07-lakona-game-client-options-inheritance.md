# LakonaGameClientOptions Inheritance Implementation Plan

> **For implementers:** Execute as one coordinated change under a single owner.

**Goal:** Make `LakonaGameClientOptions` inherit `RpcClientOptions` and remove the misleading `LakonaGameClient(RpcClientOptions, ...)` public entry.

**Architecture:** Runtime options shape, source generator output, tool templates, maintained samples, docs, and package versions change together.

**Spec:** [2026-07-07-lakona-game-client-options-inheritance-design.md](../specs/2026-07-07-lakona-game-client-options-inheritance-design.md)

## Tasks

1. Unseal `RpcClientOptions`; rewrite `LakonaGameClientOptions` with inheritance and `UseSecurity()` override.
2. Update `LakonaRpcSourceGenerator` to emit single constructor and `_options` pass-through.
3. Update Unity/Godot/Console tool templates.
4. Migrate Agar factories and Godot.Chat hand-written client code.
5. Update tests and docs.
6. Bump package versions and run full validation.

## Validation

```powershell
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```
