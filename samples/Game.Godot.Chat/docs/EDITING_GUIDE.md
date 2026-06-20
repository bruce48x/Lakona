# Editing Guide

Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push DTOs, and named contract ids.

Edit `Server/App/` for stable actor fields, host binding, Feature startup adapters, runtime integration bridges, and the hotfix `BuildTag`.

Edit `Server/Hotfix/` for replaceable Services, runtime lifecycle handlers, and Actor Behaviors.

Session cleanup and presence behavior are hotfix logic. The stable App bridge
forwards lifecycle events to `Server.Hotfix`; change the hotfix runtime service
when changing what cleanup does.

Service classes correspond to `Shared` RPC service interfaces. Behavior classes correspond one-to-one with Actor classes and run inside actor turns.

Development hotfix flow:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

The development server reloads after a successful Hotfix build signal. Production hotfixes use `lakona-tool hotfix pack`, node-local `install`, and explicit loopback `activate`.
