# Application HTTP

Lakona treats standard HTTP request/response ingress as a core game-server
capability. HTTP does not replace bidirectional RPC and is not an RPC
transport. It is a separate application ingress for callers such as payment
providers, internal operations tools, and other systems that do not participate
in a Game Session or share a long-lived callback channel.

## Traffic Planes

Lakona keeps four traffic planes explicit:

| Plane | Purpose | Game Session | Distributed admission |
| --- | --- | --- | --- |
| Application RPC | Bidirectional game-client calls, callbacks, recovery, reliable push | Optional and explicit | Required |
| Application HTTP | Product request/response work such as operations and payment notifications | None | Required |
| Management HTTP | Framework health, diagnostics, and Hotfix administration | None | Remains available while business admission is closed |
| Cluster RPC | Membership, Actor routing, notification relay, and recovery traffic | None | Governed by the cluster protocol |

An operations backend is application traffic even when operators call it an
admin system. If it reads or mutates product state, it runs through Application
HTTP and the current Hotfix generation. Management HTTP is reserved for
framework-owned operations under `/_lakona/**`.

## ASP.NET Core Host

`Lakona.Game.Server` owns the ASP.NET Core dependency directly through the
`Microsoft.AspNetCore.App` framework reference. The high-level host uses
`WebApplication.CreateBuilder`, one root service provider, and one Kestrel
server. It does not create a Generic Host and then start a nested
`WebApplication`.

Kestrel owns HTTP parsing, protocol versions, connection reuse, limits, TLS
integration, and request draining. Lakona must remove its bespoke management
HTTP listener, parser, router, and request tracker rather than retain them as a
second HTTP implementation.

The bootstrap order remains:

```text
configure the stable root graph
  -> start application modules
  -> load and validate the initial Hotfix generation
  -> bind Kestrel, RPC, and cluster listeners
  -> publish cluster Ready
  -> mark the process Ready
```

Listener bind failure fails startup. Shutdown marks the process NotReady and
closes business admission before Kestrel stops accepting application requests
and drains in-flight work.

Lakona configures Kestrel listeners explicitly. Ambient ASP.NET URL settings
must not open an undeclared listener or expose Management HTTP on another
address.

## Listener Configuration

`Lakona:Endpoints[]` remains the existing client-facing RPC listener
configuration. Application HTTP does not rename or reinterpret it.

Application HTTP uses the separate `Lakona:Http:Listeners[]` collection. Its
exact configuration shape and defaults belong to
[Configuration](./configuration.md#application-http).

Each Application HTTP listener owns:

- an operator-facing `Id`;
- its bind host and port;
- the HTTP service contracts exposed on that listener;
- its request-body limit and mandatory request timeout.

Network exposure is deployment policy, not passive Lakona metadata. Isolation
depends on the bind address, deployment network, reverse proxy, security
groups, and certificates. The first implementation binds the declared Kestrel
sockets directly and does not yet add per-listener TLS, trusted-forwarder,
authentication-mechanism, or concurrency configuration; deploy public
listeners behind an appropriately configured trusted edge.

One Kestrel server may bind any number of configured listener sockets. Listener
selection uses the actual accepted local socket, not the client-controlled
`Host` header. The listener route key is:

```text
listener id + HTTP method + route pattern
```

Different listeners may therefore expose the same method and path without
sharing the same handler contract. Duplicate ids, conflicting bind addresses,
and duplicate route keys fail validation before any listener opens.
Physical-listener selection happens before ASP.NET route matching, so a literal,
parameterized, catch-all, or differently cased route exposed on one listener
cannot shadow or make routing ambiguous on another listener. Method and route
comparisons are case-insensitive within one listener.

## Hotfix-Owned Contracts And Stable Hosting

HTTP service name, method, route pattern, request shape, response shape, and
body encoding are protocol contracts, but their declarations live beside their
handlers in `Server.Hotfix`. External HTTP callers consume the HTTP protocol,
not a parallel stable .NET interface. Generated diagnostics and runtime
candidate validation keep the declaration and implementation consistent.

An HTTP service class declares its service name. A listener's `Services`
collection selects which Hotfix-owned services are reachable on that socket.
Unknown service names, invalid handler shapes, reserved management routes, and
duplicate listener routes fail the initial Hotfix load before any listener
opens.

The normal binding path is:

```text
initial Hotfix HTTP manifest
  -> stable ASP.NET endpoint slot
  -> one current Hotfix generation lease
  -> cached typed Hotfix handler
  -> stable response value
  -> ASP.NET response adapter
```

Author the route and product behavior together in `Server.Hotfix`:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;

[LakonaHttpService("payment-webhooks")]
public sealed class PaymentWebhookService
{
    [LakonaHttpEndpoint("POST", "/payments/notify")]
    public ValueTask<LakonaHttpResponse> NotifyAsync(LakonaHttpCall call)
    {
        ReadOnlyMemory<byte> exactBody = call.Request.RawBody;

        // Verify the signature, apply idempotency policy, and route durable
        // state changes through call.Actors here.
        return new ValueTask<LakonaHttpResponse>(
            LakonaHttpResponse.Text("accepted"));
    }
}
```

An HTTP handler is a public instance method that takes `LakonaHttpCall` and
returns exactly `ValueTask<LakonaHttpResponse>`. Application HTTP has no
user-authored numeric method id. Its protocol identity is the service name,
HTTP method, and route pattern. The stable host assigns deterministic internal
endpoint slots when the initial Hotfix generation loads; those slots are
runtime implementation details and never appear in application source,
configuration, logs, or external traffic.

The initial generation freezes the process-local HTTP manifest. Later Hotfix
candidates must declare the same service names, methods, and route patterns.
Both management pre-validation and publication validation reject an
incompatible manifest. A valid candidate aligns its cached typed handlers with
the existing endpoint slots before publication. Changing handler names or
implementation details is allowed. Adding, removing, or changing a route
requires a process restart so ASP.NET routing never selects an endpoint from
one generation and dispatches it through another.

Generated projects do not teach users to write `MapGet`, `MapPost`, custom
`RequestDelegate` handlers, or product middleware in `Server.App`.
`Program.cs` remains an infrastructure composition root.

Adding or removing a route, changing a method or path, or changing a request or
response shape is a protocol change. A new initial Hotfix generation may define
that shape when the process restarts; an in-process reload may not mutate the
active route manifest. Fully dynamic route-shape publication is outside the
first implementation.

## Hotfix Owns Product Behavior

All product behavior belongs in `Server.Hotfix`, including:

- product validation and authorization decisions;
- payment signature verification against exact raw request bytes;
- idempotency and durable-acceptance policy;
- Actor selection and calls;
- persistence orchestration through stable application adapters;
- product status codes, headers, and response content;
- operations policy such as whether an operator may mutate a player.

The stable host owns mechanisms rather than product policy: Kestrel, bounded
request reads, request deadlines, trace identity, admission, generated binding,
stable serialization, and response writing. TLS, trusted-forwarder processing,
and authentication belong in the stable hosting layer when their explicit
configuration is added; they never move into Hotfix product handlers.

Hotfix code does not receive or retain `HttpContext`. The generated readonly
call value provides a bounded, detached request snapshot containing the stable
request value, exact raw body, headers, query, route values, authenticated
identity, remote endpoint, trace identity, cancellation, Actor access,
game-server access, and explicitly available stable dependencies. Snapshot
collections and buffers are request-owned copies with no link back to
`HttpContext`; they are not a deep immutability or hostile-code boundary, and
Hotfix handlers must treat them as read-only.

The listener id is stable-host binding and validation metadata. It is not
exposed to product behavior, which must not branch on listener names. Different
product semantics belong in different HTTP service contracts.

Hotfix handlers return stable, materialized response values. They do not write
to `HttpResponse`, capture request streams, return Hotfix-defined lazy
enumerables, or start fire-and-forget work that can outlive the generation.
Streaming responses, server-sent events, and WebSocket upgrades require a
separate generation-lifetime design and are not part of the first Application
HTTP contract.

## Generation Semantics

Each admitted HTTP request acquires exactly one
`HotfixRuntimeSnapshotLease`. A request already executing keeps its selected
generation; the next request after successful publication sees the new
generation. The old generation retires only after all of its in-flight HTTP,
RPC, lifecycle, timer, and Actor calls drain.

HTTP request deadlines are mandatory and cooperative. At the deadline the
stable host cancels `LakonaHttpCall.CancellationToken`; framework operations and
Hotfix handlers must observe that token and unwind so the host can return
`504 Gateway Timeout` and release the generation lease. A handler that ignores
cancellation is invalid application behavior: .NET cannot safely abort it or
unload its generation while its code is still executing. Nested framework
dispatch uses the active execution scope and must not reacquire an unrelated
newer generation in the middle of one request.

Hotfix activation remains process-local. Lakona does not implement a
cluster-wide activation transaction or generation fence. Rolling activation
may temporarily run adjacent generations on different nodes. Cross-node DTOs,
numeric ids, stable state shapes, and call semantics must therefore remain
compatible during a rollout.

Authoritative state decisions belong on the Actor owner. An HTTP Gate running a
newer generation may validate and normalize a request before routing a stable
command, but the generation currently executing on the state-owning Actor makes
the final mutation decision. Incompatible changes use an expand/roll
out/contract sequence rather than cluster-wide atomic activation.

Every node exposes its current Hotfix source version, build tag, and activation
time through diagnostics. Version skew is observable in logs and traces but
does not participate in routing or reject otherwise compatible calls.

## Admission And Failure Mapping

Application HTTP is business work. A request is rejected before Hotfix
dispatch when the process is NotReady, stopping, overloaded, or lacks
distributed-work authority. The default framework mapping is:

| Framework outcome | HTTP result |
| --- | --- |
| Listener or service not exposed | `404 Not Found` |
| Invalid framework-level request shape | `400 Bad Request` |
| Authentication mechanism rejected credentials | `401 Unauthorized` |
| Stable admission limit exhausted | `429 Too Many Requests` |
| Node NotReady, stopping, or distributed admission closed | `503 Service Unavailable` |
| Framework request deadline expired | `504 Gateway Timeout` |

Business rejection remains a product response chosen by the Hotfix handler.
Lakona does not turn a business failure into a framework status automatically.

Management HTTP remains reachable when distributed admission closes so
operators and orchestration systems can observe and repair the node.

## Security And Isolation

Management and Application HTTP share the ASP.NET Core host but not their
exposure:

- `/_lakona/**` is reserved for the Management listener;
- Management routes are never mapped on Application HTTP listeners;
- a listener exposes only its configured HTTP service contracts;
- operations routes on an internal listener are absent from a public listener;
- physical connection information, not forwarded `Host`, selects the listener;
- forwarded headers are accepted only from explicitly trusted proxies;
- exact raw body bytes remain available for signature verification without
  normalization before the Hotfix handler sees them.

An unexposed service returns `404` rather than revealing that the same process
serves it on another listener.

## Validation Requirements

Before opening Kestrel, validation rejects:

- empty or duplicate listener ids;
- invalid hosts, ports, timeouts, or request limits;
- wildcard and specific-address bindings that conflict;
- unknown or duplicate HTTP service names;
- duplicate listener/method/route combinations;
- listener references to missing HTTP services;
- Hotfix HTTP handlers whose call shape or return type is invalid;
- Hotfix candidates whose HTTP manifest differs from the initial generation;
- attempts to expose `/_lakona/**` from an application contract;
- Management and Application listener collisions.

Configuration and validation diagnostics identify listeners by their bounded
listener id and stable service name. They never include request paths
containing values, route parameters, headers, payloads, payment ids, player
ids, or other unbounded business data.

## Non-Goals

The first Application HTTP implementation does not:

- rename or reinterpret `Lakona:Endpoints[]`;
- model HTTP as an RPC transport;
- create a Game Session for an HTTP request;
- provide callbacks, resume, or reliable push over ordinary HTTP;
- expose product routes through Management HTTP;
- make route contracts or the ASP.NET middleware graph Hotfix-defined;
- provide cluster-wide atomic Hotfix activation;
- add a passive public/internal exposure classification;
- promise durable webhook acceptance without an application-owned durable
  inbox or transaction;
- configure per-listener TLS, trusted-forwarder processing, authentication, or
  concurrency limits;
- support streaming responses, server-sent events, or application WebSockets.
