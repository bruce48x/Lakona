---
name: lakona-e2e-testing
description: Use when you need to verify that Lakona.Tool scaffolded projects actually work end-to-end — scaffold, build server, start server, send real RPC requests over WebSocket, and verify correct responses. Also use after any change to Lakona.Tool templates, RPC protocol, or game server infrastructure that could break generated projects at runtime.
---

# Lakona E2E Testing

Verify scaffolded Lakona projects work with real network round-trips: scaffold → build → start server → connect with RPC client → verify responses.

**Core principle:** Template-level and shape tests catch structural issues. E2E tests with real network I/O catch serialization mismatches, handler wiring bugs, and runtime DI failures that no static test can find.

## When to Use

- After changing `Lakona.Tool` scaffolding templates (especially `ChatClientTemplates`, `ServerProjectTemplates`)
- After changing RPC protocol, envelope codec, or transport framing
- After changing game server hosting, DI binding, or session lifecycle
- When a scaffolded project compiles but fails at runtime
- Before releasing a new `Lakona.Tool` version

**Do NOT use for:** template-level string checks (use unit tests), RPC layer unit tests (use Loopback transport), or Godot client UI testing (needs Godot Engine).

## Quick Reference

The script `scripts/game/ci/test-lakona-tool-matrix.ps1` automates the full
scaffold → build → start → verify → stop lifecycle for any combination of
engine, transport, and serializer.

```powershell
# Run all 12 combinations with full E2E verification
.\scripts\game\ci\test-lakona-tool-matrix.ps1

# Fast smoke test: only Godot + websocket, scaffold + build only
.\scripts\game\ci\test-lakona-tool-matrix.ps1 -Engine godot -Transport websocket -Quick

# Single transport + serializer, full E2E
.\scripts\game\ci\test-lakona-tool-matrix.ps1 -Transport kcp -Serializer memorypack

# CI-like mode using packed local NuGet feed
.\scripts\game\ci\test-lakona-tool-matrix.ps1 -DependencyMode NuGetFeed

# Keep scaffolded projects after test
.\scripts\game\ci\test-lakona-tool-matrix.ps1 -KeepScaffolds
```

Run `Get-Help .\scripts\game\ci\test-lakona-tool-matrix.ps1` or read the
parameter block at the top of the file for full options.

### Manual Steps (when you need hands-on control)

| Step | Command |
|------|---------|
| Scaffold | `dotnet run --project src/Lakona.Tool -- new --name <Name> --client-engine <engine> --transport <transport> --network-profile cluster --serializer <serializer> --persistence none --nugetforunity-source embedded --deploy-profile none --output .tmp/<dir>` |
| Build | `dotnet build .tmp/<dir>/<Name>/Server/Server.slnx` |
| Start | `$proc = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", ".tmp/<dir>/<Name>/Server/App/Server.App.csproj", "--no-build" -NoNewWindow -PassThru -RedirectStandardOutput ".tmp/server-out.txt" -RedirectStandardError ".tmp/server-err.txt"` |
| Stop | `Stop-Process -Id $proc.Id -Force` |
| Check | `Get-Content .tmp/server-out.txt` — look for `Application started` |

## E2E Verification Client

The matrix script (`scripts/game/ci/test-lakona-tool-matrix.ps1`) automatically
generates a temporary E2E verification `.csproj` and `Program.cs` for each
combination, referencing the scaffolded Shared project and the appropriate
transport and serializer assemblies from the repo. The generated client:

1. Constructs the transport and serializer for the combination under test
2. Connects to the server at the test port (default 20000)
3. Calls `LoginAsync` (ServiceId=1, MethodId=1) with `LoginRequest { PlayerName = "E2ETest" }`
4. Asserts `reply.Members.Count == 1` and `reply.Members[0].Name == "E2ETest"`
5. Exits 0 on success, 1 on failure

See the `$programContent` heredoc in the script for the exact C# template.

**Key:** use `await using` (not `using`) — `RpcClientRuntime` implements
`IAsyncDisposable`.

The E2E client always uses **ProjectReferences** to the local source for
transport, serializer, RPC Core, and RPC Client assemblies — it never uses
NuGet packages, ensuring it tests the current source.

## Service and Method IDs

Scaffolded chat projects use fixed contract IDs (from `Shared/Contracts/RpcContractIds.cs`):

| Service | ServiceId | Method | MethodId |
|---------|-----------|--------|----------|
| Login | 1 | LoginAsync | 1 |
| Chat | 2 | BindAsync | 1 |
| Chat | 2 | SendAsync | 2 |

## Server Configuration (scaffolded)

- Transport: WebSocket at `ws://127.0.0.1:20000/ws` (from `Server/App/appsettings.json`)
- Serializer: JSON (`JsonRpcSerializer` from `Lakona.Rpc.Serializer.Json`)
- Login handler: `Server.Hotfix.Login.LoginService` — hotfix-loaded, needs DI: `ILoginCallback` proxy + `IActorRuntime` + `connectionId`

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| **Using `byte[]` as RPC arg type** — `RpcClientRuntime` double-serializes: `_serializer.SerializeFrame(byte[])` produces base64 JSON, not raw bytes | Use typed arg: `RpcMethod<LoginRequest, LoginReply>(1, 1)` and pass `LoginRequest` directly |
| **Forgetting `--network-profile cluster`** — the only supported value; `single` is rejected | Always use `cluster` |
| **Not referencing scaffolded Shared.csproj** — E2E client can't find `LoginRequest`/`LoginReply` types | Add `<ProjectReference>` to scaffolded `Shared/Shared.csproj` |
| **Server port conflict** — another process on 20000 | Change port in `appsettings.json` or kill conflicting process |
| **HandlerError: RPC handler failed** — check server stderr for the real exception | Read `.tmp/server-err.txt` — often a JSON deserialization mismatch or DI resolution failure |
| **Scaffolded NuGet versions are stale** — scaffolded `.csproj` references old NuGet packages that don't have the latest source changes | Default mode uses ProjectReference patching to replace NuGet refs with local source refs. Use `-DependencyMode NuGetFeed` for full CI-like resolution from locally-packed packages |

## RPC Wire Format (for raw debugging)

If you need to bypass `RpcClientRuntime` and send raw frames:

```
Transport frame:  [4-byte BE uint32 length] [payload bytes]
Request envelope: [1-byte 0x01] [4-byte BE uint32 requestId] [4-byte BE int32 serviceId] [4-byte BE int32 methodId] [4-byte BE int32 payloadLen] [N-byte JSON payload]
Response envelope:[1-byte 0x02] [4-byte BE uint32 requestId] [1-byte status] [4-byte BE int32 payloadLen] [N-byte payload] [1-byte hasError] [optional: 4-byte errorLen + UTF-8 error]
```

Frame types: `0x01` = Request, `0x02` = Response, `0x03` = Push, `0x04` = KeepAlivePing, `0x05` = KeepAlivePong.
