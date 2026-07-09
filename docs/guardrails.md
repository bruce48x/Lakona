# Guardrails

Lakona guardrails validate runtime configuration and generated project shape
before a server starts. They are runtime safety checks, not a compatibility
layer for removed concepts.

## Readiness Scope

Readiness validation checks:

- node id and advertised endpoint shape
- endpoint transport, serializer, host, port, and WebSocket path
- duplicate endpoint transports and duplicate RPC service names
- cluster endpoint URI and cluster serializer
- actor host names and duplicate actor host entries
- startup actor names and duplicate startup actor entries
- heartbeat interval and timeout
- hotfix assembly source
- observability configuration and required integrations

Run readiness validation with:

```bash
dotnet run --project "Server/App/Server.App.csproj" -- --readiness-check
```

Use `--json` when automation needs structured diagnostics.

## Diagnostics Ranges

- `ULINK001-ULINK019`: node identity and common runtime shape
- `ULINK020-ULINK039`: endpoint transport and RPC service configuration
- `ULINK040-ULINK069`: cluster endpoint, serializer, node directory, route directory
- `ULINK070-ULINK089`: hotfix source and reload readiness
- `ULINK090-ULINK099`: heartbeat policy
- `ULINK101-ULINK109`: actor host and startup actor configuration
- `ULINK130-ULINK149`: observability and local diagnostics exposure

## Production Boundary

Production processes should fail before opening listeners when configuration is
ambiguous or unsafe. In particular:

- cluster serializers must match across communicating nodes
- WebSocket endpoints require a path
- KCP and TCP endpoints must not use HTTP paths
- actor host and startup actor names must be non-empty and unique
- observability exports require their integration services to be registered
- local admin diagnostics must remain loopback-only unless explicitly designed otherwise

## Generated Projects

Generated starter projects should keep `appsettings.json` compact. Derived
runtime state is shown by readiness output rather than copied into generated
configuration. When a generated project is split across nodes, use
`Lakona:ActorHosts`, `Lakona:StartupActors`, `Lakona:Endpoints[]`, endpoint
`RpcServices`, and `Lakona:Cluster`.
