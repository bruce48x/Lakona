# Changelog

Lakona was created on 2026-06-07 by merging the former Lakona.Game, Lakona.Actor,
and Lakona.Rpc repositories into a single monorepo. This changelog starts from
that consolidation. Historical release notes from the pre-merge repositories are
archived at `docs/maintenance/imported-contributing-notes.md`.

## 2026-06-07

### Merged: Lakona.Rpc.Starter into Lakona.Tool

`Lakona.Tool` is now the single .NET CLI tool for Lakona project generation. The
standalone `Lakona.Rpc.Starter` package and `lakona-starter` command have been
removed — RPC workspace generation runs in-process.

**Breaking change:** Install and run `lakona-tool` instead of `lakona` or
`lakona-starter`. There is no compatibility alias.

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

**What changed:**

- `ToolCommandName` switched from `lakona` to `lakona-tool`.
- Starter generator code moved into `src/Lakona.Tool/RpcStarter/` as the
  internal `Lakona.Tool.RpcStarter` module. Generation is now in-process via
  `RpcStarterGenerator`, called directly by `CliApplication.NewAsync`.
- `ToolProcessRunner` and all external `lakona-starter` invocation, installation,
  and version-checking behavior removed.
- `ToolPackageVersions.ULinkRpcStarter` removed; starter release versions are
  resolved from the embedded `ReleaseVersions.json` inside `Lakona.Tool`.
- Starter tests moved into `tests/Lakona.Tool.Tests/RpcStarter/`.
- `src/Lakona.Rpc.Starter` and `tests/Lakona.Rpc.Starter.Tests` deleted.
- CI Godot daily workflow consolidated to a single `lakona-tool` job.
  `scripts/rpc/ci/verify-starter-godot.sh` deleted.
- Docs updated to show `lakona-tool` as the only project generator.
  `src/Lakona.Tool/README.md` rewritten as the canonical tool README.
- `ProcessInvocation` record struct removed from `ToolModels.cs`.
