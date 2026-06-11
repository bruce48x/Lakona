# RPC Starter Decision Records

This directory contains contributor-facing decision records for the RPC starter
generation path. These are not user quick starts.

Use these records when changing `src/Lakona.Tool/RpcStarter/**`,
`src/Lakona.Rpc.Analyzers/**`, starter package planning, Unity shared-source
layout, or tests that validate generated RPC starter projects.

For user-facing setup and tutorials, use the repository `README.md`, package
READMEs, and `blog/**`. For contributor rules, `CONTRIBUTING.md` remains the
single authority.

## Scope

The standalone RPC starter still uses the historical generated layout:

- `Shared/`
- `Server/Server/`
- `Client/`

The newer Lakona game scaffolding path uses `Server/App/` and may reuse the same
RPC source-generation and Unity package constraints. When applying these
records to game scaffolding, apply only the shared RPC decisions, not the
standalone starter directory layout.

## Records

| Record | Use When |
| --- | --- |
| [Source Generation](source-generation.md) | Changing the analyzer/source-generator route or generated RPC glue policy |
| [Dependency Planning](dependency-planning.md) | Changing starter package ownership or dependency matrix tests |
| [Unity Shared Source Link](unity-shared-source-link.md) | Revisiting Unity `Shared` consumption or precompiled shared assembly proposals |
