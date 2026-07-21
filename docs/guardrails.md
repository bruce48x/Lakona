# Guardrails

Lakona guardrails validate runtime configuration and generated project shape
before a server starts. They are runtime safety checks, not a compatibility
layer for removed concepts.

## Readiness Scope

Readiness validation checks:

- node id and advertised endpoint shape
- endpoint transport, serializer, host, port, and WebSocket path
- duplicate endpoint transports and duplicate RPC service names
- cluster endpoint URI
- actor host names and duplicate actor host entries
- startup actor names and duplicate startup actor entries
- heartbeat interval and timeout
- hotfix assembly source
- observability configuration and required integrations

Run readiness validation through the health route on the management HTTP listener:

```bash
curl http://127.0.0.1:20080/_lakona/health/ready
```

The endpoint returns JSON and uses HTTP 200 when ready or HTTP 503 when any
guardrail diagnostic is fatal.

## Diagnostics Ranges

- `LAKONA001-LAKONA019`: node identity and common runtime shape
- `LAKONA020-LAKONA039`: endpoint transport and RPC service configuration
- `LAKONA040-LAKONA069`: cluster endpoint, node directory, route directory
- `LAKONA070-LAKONA089`: hotfix source and reload readiness
- `LAKONA090-LAKONA099`: heartbeat policy
- `LAKONA101-LAKONA109`: actor host and startup actor configuration
- `LAKONA130-LAKONA149`: observability and local diagnostics exposure

## Production Boundary

Production processes should fail before opening listeners when configuration is
ambiguous or unsafe. In particular:

- cluster endpoint schemes must match the transport selected by `UseClusterRpc`
- cluster peers must negotiate the same serializer protocol before RPC starts
- WebSocket endpoints require a path
- KCP and TCP endpoints must not use HTTP paths
- actor host and startup actor names must be non-empty and unique
- observability exports require their integration services to be registered
- local admin diagnostics must remain loopback-only unless explicitly designed otherwise

## Generated Projects

Generated starter projects should keep `appsettings.json` compact. Derived
runtime state is shown by the readiness endpoint rather than copied into
generated configuration. When a generated project is split across nodes, use
`Lakona:ActorHosts`, `Lakona:StartupActors`, `Lakona:Endpoints[]`, endpoint
`RpcServices`, and `Lakona:Cluster`.
