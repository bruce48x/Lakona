# Editing Guide

Edit `Shared/Contracts/` for RPC contracts, callback contracts, reliable push DTOs, and named contract ids.

Edit `Server/App/` for stable actor fields, host binding, Feature startup adapters, framework setup calls, and the hotfix `BuildTag`.

Edit `Server/Hotfix/` for replaceable Services, session lifecycle behavior, and Actor Behaviors.

Edit `Server/Hotfix/Chat/ChatSessionLifecycle.cs` for session lifecycle behavior such as presence cleanup after session expiration. `Server/App` only enables the framework bridge and keeps stable actor state.

Service classes correspond to `Shared` RPC service interfaces. Behavior classes correspond one-to-one with Actor classes and run inside actor turns.

Development hotfix flow:

```powershell
dotnet build Server/Hotfix/Server.Hotfix.csproj
```

The development server reloads after a successful Hotfix build signal. Production hotfixes use `lakona-tool hotfix pack`, node-local `install`, and explicit loopback `activate`.
