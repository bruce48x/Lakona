# Editing Guide

Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push DTOs, and named contract ids.

Edit `Server/App/` for stable orchestration, actor fields, service proxies, host binding, runtime integration, and the hotfix `BuildTag`.

Edit `Server/Hotfix/` for replaceable Services and Actor Behaviors.

Service classes correspond to `Shared` RPC service interfaces. Behavior classes correspond one-to-one with Actor classes and run inside actor turns.

Development hotfix flow:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

The development server reloads after a successful Hotfix build signal. Production hotfixes use `lakona-tool hotfix pack`, node-local `install`, and explicit loopback `activate`.
