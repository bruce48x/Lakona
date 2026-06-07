# Starter Dependency Planning

Status: implemented

Date: 2026-05-09

Updated: 2026-05-25

## Decision

`Lakona.Tool`'s RPC starter module centralizes package ownership in `StarterDependencyPlanner`.

Starter templates should render dependency plans. They should not independently decide serializer, transport, analyzer, or Unity runtime package ownership.

## Context

`Lakona.Tool`'s RPC starter module generates multiple project areas from the same user choices:

- `Shared/Shared.csproj`
- `Server/Server/Server.csproj`
- Unity / Tuanjie client `Assets/packages.config`
- Godot client `Client.csproj`

Those projects do not consume dependencies through the same mechanism.

- Server and Godot consume `Shared` through SDK-style `.csproj` `ProjectReference`.
- Unity and Tuanjie consume `Shared` as a local UPM source package and restore runtime DLLs through NuGetForUnity `packages.config`.

Dependency ownership is therefore based on both feature selection and consumer model.

## Planner Shape

The planner owns one package plan per generated project role:

- `Shared`
- `Server`
- `UnityClient`
- `GodotClient`
- `ConsoleClient`

The plan returns `StarterPackageReference` values with package id, version, `manuallyInstalled`, `PrivateAssets`, and `IncludeAssets` metadata.

## Current Rules

### Shared

`Shared.csproj` directly references `Lakona.Rpc.Core`.

When `memorypack` is selected, `Shared.csproj` also directly references:

- `Lakona.Rpc.Serializer.MemoryPack`
- `MemoryPack`
- `MemoryPack.Generator`

When `json` is selected, `Shared.csproj` does not reference `Lakona.Rpc.Serializer.Json`.

### Server

Server consumes `Shared.csproj` through `ProjectReference`.

Server directly references:

- `Lakona.Rpc.Server`
- selected transport package
- `Lakona.Rpc.Analyzers` as a private analyzer/source-generator package

When `json` is selected, Server also directly references `Lakona.Rpc.Serializer.Json`.

When `memorypack` is selected, Server does not repeat serializer/runtime packages already provided by `Shared.csproj`.

### Godot Client

Godot consumes `Shared.csproj` through `ProjectReference`.

Godot directly references:

- `Lakona.Rpc.Core`
- `Lakona.Rpc.Client`
- selected transport package
- `Lakona.Rpc.Analyzers` as a private analyzer/source-generator package

When `json` is selected, Godot also directly references `Lakona.Rpc.Serializer.Json`.

When `memorypack` is selected, Godot does not repeat serializer/runtime packages already provided by `Shared.csproj`.

### Unity / Tuanjie Client

Unity and Tuanjie consume `Shared` through a local UPM source package, not through SDK-style transitive restore.

Their `Assets/packages.config` must keep explicit runtime packages needed by Unity compilation and play mode, including:

- `Lakona.Rpc.Core`
- `Lakona.Rpc.Client`
- selected transport package
- selected serializer package
- `Lakona.Rpc.Analyzers`
- serializer-specific Unity runtime dependencies
- KCP runtime dependencies when KCP transport is selected

This is intentionally different from Server and Godot. Do not remove Unity/Tuanjie serializer entries just because `Shared.csproj` contains a matching package reference.

## Testing Requirements

`StarterDependencyPlannerTests` must cover the dependency matrix directly:

- `Shared + memorypack` includes MemoryPack serializer/runtime/generator packages.
- `Shared + json` does not include `Lakona.Rpc.Serializer.Json`.
- Server and Godot avoid redundant MemoryPack serializer/runtime declarations.
- Server and Godot include JSON serializer packages for JSON projects.
- SDK-style generated projects include `Lakona.Rpc.Analyzers` with private analyzer metadata.
- Unity/Tuanjie keep explicit serializer, analyzer, transport runtime, and serializer runtime dependencies.

Rendered template tests should verify integration, not duplicate the full dependency matrix.
