# Application HTTP

Status: active architecture. The Kestrel host, listener isolation, bounded
request snapshot, admission, and generation-pinned Hotfix dispatch are
implemented together with generated stable contract binding.

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

Application HTTP uses a separate collection:

```json
{
  "Lakona": {
    "Endpoints": [
      {
        "Transport": "websocket",
        "Serializer": "memorypack",
        "Host": "0.0.0.0",
        "Port": 20000,
        "Path": "/ws",
        "RpcServices": [ "login", "player" ]
      }
    ],
    "Http": {
      "Listeners": [
        {
          "Id": "operations",
          "Host": "10.0.0.10",
          "Port": 21000,
          "Exposure": "Internal",
          "Services": [ "operations" ],
          "MaximumBodyBytes": 1048576,
          "RequestTimeoutSeconds": 30
        },
        {
          "Id": "payments",
          "Host": "0.0.0.0",
          "Port": 21001,
          "Exposure": "Public",
          "Services": [ "payment-webhooks" ],
          "MaximumBodyBytes": 262144,
          "RequestTimeoutSeconds": 15
        }
      ]
    },
    "Management": {
      "Http": {
        "Host": "127.0.0.1",
        "Port": 20080
      }
    }
  }
}
```

Each Application HTTP listener owns:

- an operator-facing `Id`;
- its bind host and port;
- an exposure classification used by validation and diagnostics;
- the HTTP service contracts exposed on that listener;
- its request-body limit and mandatory request timeout.

`Exposure` informs guardrails; it is not a firewall. Public and internal
network isolation still depends on the deployment network, reverse proxy,
security groups, and certificates. The first implementation binds the declared
Kestrel sockets directly and does not yet add per-listener TLS,
trusted-forwarder, authentication-mechanism, or concurrency configuration;
deploy public listeners behind an appropriately configured trusted edge.

One Kestrel server may bind any number of configured listener sockets. Listener
selection uses the actual accepted local socket, not the client-controlled
`Host` header. The listener route key is:

```text
listener id + HTTP method + route pattern
```

Different listeners may therefore expose the same method and path without
sharing the same handler contract. Duplicate ids, conflicting bind addresses,
and duplicate route keys fail validation before any listener opens.

## Stable Contracts And Generated Binding

HTTP method, route pattern, stable method id, request shape, response shape, and
body encoding are protocol contracts. They live in a stable application or
contract assembly. External HTTP callers do not need to consume the .NET
assembly, but the stable contract lets generated code validate and bind the
server consistently.

An HTTP contract declares a stable service name. A listener's `Services`
collection selects which generated contracts are reachable on that socket.
Unknown service names and missing required Hotfix implementations fail startup
or candidate-generation validation.

The normal binding path is:

```text
stable HTTP contract
  -> generated stable ASP.NET endpoint binder
  -> one current Hotfix generation lease
  -> generated HTTP call value
  -> current Server.Hotfix handler
  -> stable response value
  -> ASP.NET response adapter
```

Author the route contract in the stable `Server.App` assembly:

```csharp
using Lakona.Game.Server.Http;

[LakonaHttpService("payment-webhooks")]
public interface IPaymentWebhookService
{
    [LakonaHttpEndpoint(17, "POST", "/payments/notify")]
    ValueTask<LakonaHttpResponse> NotifyAsync(LakonaHttpRequest request);
}
```

Implement only the product behavior in `Server.Hotfix`:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;

[HotfixService(typeof(IPaymentWebhookService))]
public sealed class PaymentWebhookService
{
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

The contract method takes `LakonaHttpRequest`; the corresponding Hotfix method
has the same name and return type but takes `LakonaHttpCall`. The generated
stable registration binds the service name, method, route, and numeric method
id, while the generated required-contract provider makes a missing or duplicate
Hotfix implementation fail validation before publication.

Generated projects do not teach users to write `MapGet`, `MapPost`, custom
`RequestDelegate` handlers, or product middleware in `Server.App`.
`Program.cs` remains an infrastructure composition root.

Adding or removing a route, changing a method or path, or changing a request or
response shape is a stable protocol change rather than a Hotfix. Dynamic
`EndpointDataSource` publication and Hotfix-defined route shapes are outside
the first implementation.

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
call value provides bounded snapshots of the data needed by product behavior,
including the stable request value, exact raw body, headers, query, route
values, authenticated identity, remote endpoint, trace identity, cancellation,
Actor access, game-server access, and explicitly available stable dependencies.

The listener id is operational metadata for logs, metrics, traces, and binding.
Product behavior must not branch on listener names. Different product semantics
belong in different HTTP service contracts.

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

HTTP request timeouts are mandatory so an abandoned request cannot retain a
generation indefinitely. Nested framework dispatch uses the active execution
scope and must not reacquire an unrelated newer generation in the middle of
one request.

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
- missing required generated binders or Hotfix handlers;
- attempts to expose `/_lakona/**` from an application contract;
- Management and Application listener collisions.

Diagnostics use the listener id and service name as bounded tags. They never
tag request paths containing values, route parameters, headers, payloads,
payment ids, player ids, or other unbounded business data.

## Non-Goals

The first Application HTTP implementation does not:

- rename or reinterpret `Lakona:Endpoints[]`;
- model HTTP as an RPC transport;
- create a Game Session for an HTTP request;
- provide callbacks, resume, or reliable push over ordinary HTTP;
- expose product routes through Management HTTP;
- make route contracts or the ASP.NET middleware graph Hotfix-defined;
- provide cluster-wide atomic Hotfix activation;
- promise durable webhook acceptance without an application-owned durable
  inbox or transaction;
- configure per-listener TLS, trusted-forwarder processing, authentication, or
  concurrency limits;
- support streaming responses, server-sent events, or application WebSockets.
