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

| Step | Command |
|------|---------|
| Scaffold | `dotnet run --project src/Lakona.Tool -- new --name <Name> --client-engine godot --transport websocket --network-profile cluster --serializer json --persistence none --nugetforunity-source embedded --deploy-profile none --output .tmp/<dir>` |
| Build | `dotnet build .tmp/<dir>/<Name>/Server/Server.slnx` |
| Start | `$proc = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", ".tmp/<dir>/<Name>/Server/App/Server.App.csproj", "--no-build" -NoNewWindow -PassThru -RedirectStandardOutput ".tmp/server-out.txt" -RedirectStandardError ".tmp/server-err.txt"` |
| Stop | `Stop-Process -Id $proc.Id -Force` |
| Check | `Get-Content .tmp/server-out.txt` — look for `RPC server listening on ws://0.0.0.0:20000/ws` |

## E2E Verification Client Pattern

Create `.tmp/e2e-test/E2EVerification.csproj` and `Program.cs`:

**csproj** — reference Lakona RPC assemblies AND the scaffolded Shared project:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj" />
    <ProjectReference Include="../../src/Lakona.Rpc.Client/Lakona.Rpc.Client.csproj" />
    <ProjectReference Include="../../src/Lakona.Rpc.Transport.WebSocket/Lakona.Rpc.Transport.WebSocket.csproj" />
    <ProjectReference Include="../../src/Lakona.Rpc.Serializer.Json/Lakona.Rpc.Serializer.Json.csproj" />
    <ProjectReference Include="../<scaffold-dir>/<Name>/Shared/Shared.csproj" />
  </ItemGroup>
</Project>
```

**Program.cs** — use typed request/response, let RpcClientRuntime handle serialization:
```csharp
using Shared.Contracts.Chat;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.WebSocket;

var transport = new WsTransport("ws://127.0.0.1:20000/ws");
var serializer = new JsonRpcSerializer();
var client = new RpcClientRuntime(transport, serializer);

await client.StartAsync();

// Login: ServiceId=1, MethodId=1
var reply = await client.CallAsync(
    new RpcMethod<LoginRequest, LoginReply>(1, 1),
    new LoginRequest { PlayerName = "E2ETest" });

Console.WriteLine($"Members={reply.Members.Count}, RecentMessages={reply.RecentMessages.Count}");
await client.DisposeAsync();
```

**Run:** `dotnet run --project .tmp/e2e-test/E2EVerification.csproj`

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

## RPC Wire Format (for raw debugging)

If you need to bypass `RpcClientRuntime` and send raw frames:

```
Transport frame:  [4-byte BE uint32 length] [payload bytes]
Request envelope: [1-byte 0x01] [4-byte BE uint32 requestId] [4-byte BE int32 serviceId] [4-byte BE int32 methodId] [4-byte BE int32 payloadLen] [N-byte JSON payload]
Response envelope:[1-byte 0x02] [4-byte BE uint32 requestId] [1-byte status] [4-byte BE int32 payloadLen] [N-byte payload] [1-byte hasError] [optional: 4-byte errorLen + UTF-8 error]
```

Frame types: `0x01` = Request, `0x02` = Response, `0x03` = Push, `0x04` = KeepAlivePing, `0x05` = KeepAlivePong.
