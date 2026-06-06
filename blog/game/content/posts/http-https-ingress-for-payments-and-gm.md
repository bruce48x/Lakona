---
title: "Adding HTTP/HTTPS Webhooks And GM APIs To A Lakona.Game Server"
date: 2026-06-04T14:30:00+08:00
summary: "Use standard ASP.NET Core HTTP endpoints beside Lakona.Rpc for payment notifications, GM commands, and operations integrations."
tags:
  - lakona-game
  - dotnet
  - aspnetcore
  - deployment
  - tutorial
categories:
  - Tutorial
---

Lakona.Game relies on Lakona.Rpc for game client communication. That does not mean every integration should become an RPC transport.

Payment platforms, operations dashboards, and internal GM tools usually speak plain HTTP or HTTPS. Keep those integrations as a separate ingress layer in your Gateway process or in a dedicated management process, then call your own services or actors from there.

The recommended boundary is:

```text
Lakona.Rpc
  game client login, control messages, realtime endpoint, reliable push callbacks

HTTP/HTTPS
  payment platform notifications, GM commands, health probes, operations tools

Your game server code
  validates the HTTP request, converts it into a business command, then calls services or actors
```

Lakona.Game does not provide built-in Alipay, WeChat Pay, Stripe, Steam, Google Play, App Store, or GM command modules. Those integrations are business-specific and should live in your project.

## Why HTTP Is Not A Lakona.Rpc Transport Here

External payment platforms will not call your generated Lakona.Rpc client. They will send normal HTTP requests with their own signature, retry, timestamp, response-code, and idempotency rules.

Trying to hide that behind Lakona.Rpc makes the boundary worse:

- payment callbacks are platform-to-server webhooks, not game-client RPC calls
- GM commands need admin authentication, audit logging, and rate limits, not player session callbacks
- HTTPS termination, WAF rules, and IP allowlists are operations concerns
- each game has different order state, refund policy, inventory rules, and compensation workflows

Treat HTTP as an adapter. After validation, the adapter should call your domain service or actor runtime.

## Minimal Project Shape

If your Gateway project currently uses `Host.CreateApplicationBuilder`, you can switch the entry point to ASP.NET Core's `WebApplication` host. Lakona.Game hosted services still run normally because `WebApplication` is also a .NET host.

Add the ASP.NET Core shared framework to the Gateway project if it does not already use the Web SDK:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

Then wire HTTP routes beside your existing Lakona.Game services:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Gateway.Features;
using Gateway.Payments;
using Gateway.Admin;
using Lakona.Game.Server.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddFeatures(builder.Configuration, features =>
{
    features.FromAssembly(typeof(GatewayRole).Assembly);
});

builder.Services.AddSingleton<PaymentWebhookHandler>();
builder.Services.AddSingleton<GmCommandHandler>();

var app = builder.Build();

app.MapPost("/webhooks/payments/{provider}", async (
    string provider,
    HttpRequest request,
    PaymentWebhookHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(provider, request, cancellationToken);
    return result.Accepted ? Results.Ok(result.ResponseBody) : Results.BadRequest(result.ResponseBody);
});

app.MapPost("/admin/gm/{command}", async (
    string command,
    HttpRequest request,
    GmCommandHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(command, request, cancellationToken);
    return result.Success ? Results.Ok(result.Body) : Results.BadRequest(result.Body);
});

await app.RunAsync();
```

This does not replace Lakona.Rpc. Your existing `IULinkRpcServerConfigurator` registrations still choose WebSocket, KCP, TCP, serializer, and generated RPC binders.

## Payment Webhook Flow

A payment callback should be boring and defensive:

```text
HTTP request arrives
  read the raw body
  validate provider, timestamp, nonce, and signature
  parse the provider payload
  check idempotency by provider transaction id
  record the notification
  submit a domain command such as GrantPurchaseAsync
  return the provider's expected success response
```

Keep provider-specific parsing separate from game state changes:

```csharp
public sealed class PaymentWebhookHandler
{
    private readonly PaymentSignatureVerifier _signatures;
    private readonly PaymentNotificationStore _notifications;
    private readonly PurchaseGrantService _purchases;

    public PaymentWebhookHandler(
        PaymentSignatureVerifier signatures,
        PaymentNotificationStore notifications,
        PurchaseGrantService purchases)
    {
        _signatures = signatures;
        _notifications = notifications;
        _purchases = purchases;
    }

    public async ValueTask<PaymentWebhookResult> HandleAsync(
        string provider,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        var verified = await _signatures.VerifyAsync(provider, request.Headers, rawBody, cancellationToken);
        if (!verified.Valid)
        {
            return PaymentWebhookResult.Reject("invalid signature");
        }

        var notification = verified.Notification;
        var firstSeen = await _notifications.TryRecordAsync(notification, cancellationToken);
        if (!firstSeen)
        {
            return PaymentWebhookResult.Accept("ok");
        }

        await _purchases.GrantPurchaseAsync(notification, cancellationToken);
        return PaymentWebhookResult.Accept("ok");
    }
}
```

The idempotency check is not optional. Payment platforms retry notifications. Your grant path must tolerate duplicate, delayed, and out-of-order messages.

## Calling Actors Or Services

After validation, call your normal server-side code. That might be a domain service:

```csharp
public sealed class PurchaseGrantService
{
    private readonly IPlayerInventoryRepository _inventory;

    public async ValueTask GrantPurchaseAsync(
        PaymentNotification notification,
        CancellationToken cancellationToken)
    {
        await _inventory.GrantOrderAsync(
            notification.PlayerId,
            notification.OrderId,
            notification.Sku,
            cancellationToken);
    }
}
```

Or it might be an actor command:

```csharp
public sealed class PurchaseGrantService
{
    private readonly IActorRuntime _actors;

    public PurchaseGrantService(IActorRuntime actors)
    {
        _actors = actors;
    }

    public ValueTask GrantPurchaseAsync(
        PaymentNotification notification,
        CancellationToken cancellationToken)
    {
        return _actors.TellAsync<PlayerActor>(
            ActorId.From($"player/{notification.PlayerId}"),
            (actor, ct) => actor.GrantPurchaseAsync(notification.OrderId, notification.Sku, ct),
            cancellationToken);
    }
}
```

Do not bind payment callbacks to a game client's session endpoint. A purchase can complete while the player is offline. If the online client must be notified, publish a reliable business push after the authoritative grant succeeds.

## GM Command Flow

GM endpoints are more dangerous than player RPC endpoints. They should have a separate security model:

```text
HTTP request arrives
  authenticate the admin caller
  authorize the specific command and target scope
  validate request shape and limits
  write an audit record before execution
  execute the command
  write the result audit record
```

Start with a small command surface:

```csharp
app.MapPost("/admin/gm/grant-currency", async (
    GrantCurrencyRequest body,
    GmCommandHandler handler,
    CancellationToken cancellationToken) =>
{
    var result = await handler.GrantCurrencyAsync(body, cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});
```

Recommended rules:

- require strong authentication, such as mTLS, signed internal tokens, or an identity-aware proxy
- allowlist source networks at the load balancer or reverse proxy
- authorize by command, environment, game shard, and target player
- audit who did what, when, from where, and why
- make every command idempotent when it can be retried
- put strict limits on batch size and target count
- keep destructive commands behind an extra approval flow

Do not expose GM endpoints on the same public surface as the player WebSocket path unless your operations team has explicitly secured that route.

## Deployment

For production, terminate HTTPS at a reverse proxy or cloud load balancer when possible:

```text
internet
  -> HTTPS load balancer / WAF
  -> Gateway HTTP port for webhooks and GM API
  -> Gateway Lakona.Rpc port for game clients
```

Keep paths explicit:

```text
wss://game.example.com/ws
https://game.example.com/webhooks/payments/wechat
https://admin.example.com/admin/gm/grant-currency
```

Use separate host names when you can. `admin.example.com` should have stricter firewall, identity, and audit policy than `game.example.com`.

If you run HTTP endpoints in a separate process, keep that process on the private network and have it call state services or shared infrastructure through your own application boundary. That is often a good production shape when payment or GM operations have different scaling, security, or release needs than player Gateway traffic.

## What Belongs In Lakona.Game

Lakona.Game owns game-server infrastructure:

- session lifecycle
- reliable business push mechanics
- actor runtime integration
- Lakona.Rpc hosting helpers
- cluster routing contracts

Your game owns external HTTP integrations:

- payment provider SDKs and signatures
- order state machines
- refund and compensation policy
- GM command definitions
- admin authentication and authorization
- audit log retention

This keeps the framework small and lets each game meet its own business and operations requirements.

## Common Mistakes

Do not skip idempotency. Webhooks retry.

Do not grant purchases before signature validation succeeds.

Do not trust client-submitted order data. Use server-side order records and provider transaction ids.

Do not make GM commands unaudited convenience endpoints.

Do not route external payment providers through Lakona.Rpc.

Do not put payment secrets in `appsettings.json` committed to source control. Use environment variables, locked-down files, or a secret manager.

## Summary

Use Lakona.Rpc for game communication. Use HTTP/HTTPS for external systems.

The clean integration model is:

1. Add ASP.NET Core endpoints to your Gateway or a dedicated management process.
2. Validate signatures, authentication, authorization, idempotency, and limits at the HTTP boundary.
3. Convert accepted requests into normal domain service or actor commands.
4. Use reliable push only after authoritative state changes when the player client must be notified.

Lakona.Game does not need a payment or GM plugin for this. The safest default is a clear boundary and ordinary .NET HTTP hosting in the user's project.
