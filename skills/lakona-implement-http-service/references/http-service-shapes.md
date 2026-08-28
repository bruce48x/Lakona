# Lakona Application HTTP Shapes

## Contents

- [Hotfix Service](#hotfix-service)
- [Reload Eligibility](#reload-eligibility)
- [Request And Response Surface](#request-and-response-surface)
- [Listener Exposure](#listener-exposure)
- [Signed And Retryable Requests](#signed-and-retryable-requests)

## Hotfix Service

Declare the route and implement the handler together in the Hotfix assembly:

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

        // Verify and deduplicate through an application-owned durable Store.
        return new ValueTask<LakonaHttpResponse>(
            LakonaHttpResponse.Text("accepted"));
    }
}
```

The generated validation requires a top-level public sealed non-generic class
and public instance handlers returning exactly
`ValueTask<LakonaHttpResponse>`. Constructor-inject narrow stable dependencies.
Use `call.Actors` or `call.GameServer` only when the product behavior needs
them.

Application HTTP has no user-authored numeric method id. Service name, HTTP
method, and route pattern define protocol identity. The initial generation
freezes the process-local route manifest; adding, removing, or changing a route
requires a process restart. Route patterns under `/_lakona/**` belong to
Management HTTP.

## Reload Eligibility

A behavior-only change preserves the service name, HTTP method, and route
pattern. Handler method names, implementation logic, constructor dependencies,
and response behavior may change without changing the manifest, so the
candidate remains eligible for in-process Hotfix reload.

Adding or removing an endpoint, or changing its service name, HTTP method, or
route pattern, changes the manifest and requires a process restart. A response
schema change is not blocked by manifest validation, but it remains an external
protocol compatibility decision.

Do not work around the frozen manifest with application-owned dynamic endpoint
publication, a catch-all router, or relaxed validation. Preserve the current
framework boundary until Lakona adopts a generation-consistent dynamic routing
design.

## Request And Response Surface

`call.Request` is a bounded snapshot detached from ASP.NET and provides:

- `RawBody`
- `Headers`
- `Query`
- `RouteValues`
- `AuthenticatedName`
- `RemoteEndpoint`
- `TraceIdentifier`

The call also provides `CancellationToken`, `Services`, `Actors`, and
`GameServer`. Treat snapshot buffers and collections as request-owned,
read-only data.

Return `LakonaHttpResponse.Text(...)`, `LakonaHttpResponse.Json(...)`, or a
fully materialized `LakonaHttpResponse` when custom headers or bytes are
required. Product validation selects product status codes; framework admission
and hosting failures retain framework-owned mappings.

## Listener Exposure

Expose the service name on the intended physical listener:

```json
{
  "Lakona": {
    "Http": {
      "Listeners": [
        {
          "Id": "payments",
          "Host": "0.0.0.0",
          "Port": 21001,
          "Services": [ "payment-webhooks" ],
          "MaximumBodyBytes": 262144,
          "RequestTimeoutSeconds": 15
        }
      ]
    }
  }
}
```

Listener selection uses the accepted local socket. A listener exposes only its
configured services. Validate bind addresses and trusted-edge assumptions as
deployment policy rather than inferring security from names such as `internal`.

## Signed And Retryable Requests

For a signed webhook:

1. Read the signature and timestamp from the bounded headers.
2. Verify against `RawBody` before decoding or normalizing it.
3. Enforce the provider's replay window and secret-rotation policy.
4. Derive an idempotency key from a trusted provider event identifier.
5. Atomically record acceptance or route it to the authoritative actor/store.
6. Return success only at the durability level promised to the caller.
7. Pass `call.CancellationToken` through every asynchronous dependency.

Keep provider-specific policy in Hotfix and secret/resource lifecycle behind a
stable App interface.
