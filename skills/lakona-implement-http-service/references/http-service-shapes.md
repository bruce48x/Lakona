# Lakona Application HTTP Shapes

## Contents

- [Stable Contract](#stable-contract)
- [Hotfix Handler](#hotfix-handler)
- [Request And Response Surface](#request-and-response-surface)
- [Listener Exposure](#listener-exposure)
- [Signed And Retryable Requests](#signed-and-retryable-requests)

## Stable Contract

Place the contract in the stable App assembly and use IDs from the project's
established contract-ID owner:

```csharp
using Lakona.Game.Server.Http;

[LakonaHttpService("payment-webhooks")]
public interface IPaymentWebhookService
{
    [LakonaHttpEndpoint(17, "POST", "/payments/notify")]
    ValueTask<LakonaHttpResponse> NotifyAsync(LakonaHttpRequest request);
}
```

The method, path, numeric ID, request type, and response type are stable
protocol. Published changes need the same compatibility care as other external
contracts. Route patterns under `/_lakona/**` belong to Management HTTP.

## Hotfix Handler

Implement the matching method in the Hotfix assembly:

```csharp
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;

[HotfixService(typeof(IPaymentWebhookService))]
public sealed class PaymentWebhookService
{
    public ValueTask<LakonaHttpResponse> NotifyAsync(LakonaHttpCall call)
    {
        ReadOnlyMemory<byte> exactBody = call.Request.RawBody;

        // Verify, deduplicate, and route durable work to its owner.
        return new ValueTask<LakonaHttpResponse>(
            LakonaHttpResponse.Text("accepted"));
    }
}
```

The generated binding requires one implementation, the same method name and
return type, and `LakonaHttpCall` in place of the contract's
`LakonaHttpRequest`. Constructor-inject narrow stable dependencies. Use
`call.Actors` or `call.GameServer` only when the product behavior needs them.

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

Expose the stable service name on the intended physical listener:

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
