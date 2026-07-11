# Unified Local HTTP Host Design

## Goal

Serve Lakona liveness, readiness, diagnostics, and hotfix-admin HTTP routes
through one process-local listener, using the health endpoint port (default
`20080`).

## Decision

Retain the existing lightweight `TcpListener` host used by health endpoints
and extend its routing surface to host local-admin routes. Do not introduce an
ASP.NET Core dependency or replace the listener with `HttpListener`.

The default listener remains `Lakona:Health:Http` on `127.0.0.1:20080`.
`Lakona:Observability:LocalAdmin` no longer owns a host, port, or hosted
listener. It only controls whether local-admin routes are registered and
whether those routes require a loopback client.

## Architecture

Introduce one internal HTTP request, response, route, and router model in the
game-server package. Each route declares its method, path, handler, and access
policy. The common `LakonaHealthHttpHostedService` remains the sole listener
and passes every valid request to that router.

Health routes retain the health loopback policy from
`Lakona:Health:Http:RequireLoopback`. Diagnostics and hotfix-admin routes retain
their stricter local-admin policy from
`Lakona:Observability:LocalAdmin:RequireLoopback`, independently of the health
policy. Therefore binding health to a non-loopback address never exposes local
administration accidentally.

When local admin is disabled, its routes are absent and return `404` through
the common router. Liveness and readiness remain available whenever the health
listener is enabled.

## Configuration and Compatibility

This is an intentional breaking configuration change:

- The separate `Lakona:Observability:LocalAdmin:Host` and `Port` settings are
  removed.
- `Lakona:Observability:LocalAdmin:Enabled` continues to enable diagnostics and
  hotfix-admin route registration.
- All generated-project documentation and Lakona.Tool hotfix command defaults
  point to `http://127.0.0.1:20080`.

No compatibility listener remains on port `20090`; a process opens one local
HTTP listener at most.

## Error Handling and Lifecycle

The listener preserves current readiness semantics: startup completes only
after its socket binds; bind failures fault startup; shutdown stops accepting
connections and drains tracked requests. Route failures are logged and produce
a JSON error response. Existing status behaviour for health endpoints remains
unchanged.

## Verification

Focused tests will prove that:

1. health and local-admin routes are dispatched by one router;
2. the unified hosted service serves both route families on one port;
3. local-admin routes remain forbidden for remote clients even if health is
   configured for remote access;
4. disabling local-admin removes only local-admin routes; and
5. generated tool output and hotfix command defaults use port `20080`.
