+++
title = "Design Boundaries"
date = 2026-05-12T09:05:00+08:00
+++

Lakona.Rpc is responsible for communication framework behavior, not for being a complete application server framework.

The framework handles:

- transport integration and frame I/O
- frame encoding, compression, encryption, and limits
- session lifetime
- request / response / push dispatch
- keepalive and connection shutdown semantics
- serializer boundaries

The application layer handles:

- user login, account systems, and token lifetime
- request-level authorization and business permissions
- business error codes and recoverable failures
- automatic reconnect, state recovery, and request replay
- DTO versioning strategy and gradual rollout
- Unity, Tuanjie, or Godot main-thread dispatch

## Authentication and Authorization

`TransportSecurityConfig` can provide frame-level compression and symmetric encryption, but it does not verify remote identity and does not include users, roles, tenants, resource ownership, or policy systems.

Authentication can be done at the application integration layer, such as through a login RPC, external token validation, or identity context injected by a gateway. Authorization should stay in business code because access rules depend on the domain model.

## Why Authorization Policies Are Not Built In

Request-level authorization may look like middleware, but real rules usually depend on business semantics:

- who owns this resource
- whether the current role can perform this action
- whether room, battle, party, or tenant state allows it
- whether the operation is idempotent or retryable
- whether the client version may still call an old method

This information does not belong in the low-level RPC runtime. Lakona.Rpc should deliver calls to service implementations correctly and safely, not bake business policy into the communication layer.

## Relationship to Production Guides

Before production, put these policies in the application layer or an upper framework:

- [Error Handling](/posts/error-handling/)
- [Security Model](/posts/security-model/)
- [DTO Versioning](/posts/dto-versioning/)
- [Connection Lifecycle](/posts/connection-lifecycle/)
- [Threading Model](/posts/threading-model/)
