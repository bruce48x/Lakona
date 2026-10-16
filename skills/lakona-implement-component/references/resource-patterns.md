# Component Resource Patterns

## Contents

- [Project setup](#project-setup)
- [Root-owned synchronous client](#root-owned-synchronous-client)
- [State-owned asynchronous client](#state-owned-asynchronous-client)
- [Reload and shutdown](#reload-and-shutdown)

## Project setup

Use these patterns with the installed component-capable framework APIs. App
references `Lakona.Game.Server` with `LakonaProjectRole=ServerApp`; Hotfix
references App with `LakonaProjectRole=Hotfix`. Keep third-party client packages
in App's deployment dependency closure so all generations share their stable
types. These minimal examples use .NET clients and need no extra client package.
Replace `Example.App` / `Example.Hotfix` with the project's namespaces and actual
assembly names, including the friend-assembly declaration below.

Both components use `[NodeRole("data")]`: enable `data` in `Lakona:Node:Roles`
on the owning node and supply its configuration. Remove the marker for a tool
enabled on every node. Consumers constructor-inject the concrete component;
never register the component or State manually, alias it, or expose its State.

## Root-owned synchronous client

No App State class is needed. This component declares a dedicated root-owned
HttpClient for a remote catalog API with `health` and `catalog` endpoints.
Configure `Catalog:BaseUrl` as an absolute HTTP(S) URI ending in `/`.

```csharp
// Server.Hotfix/Catalog/RemoteCatalog.cs
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Example.Hotfix;

[HotfixComponent, NodeRole("data")]
public sealed class RemoteCatalog
{
    private readonly HttpClient _client;

    public RemoteCatalog(HttpClient client) => _client = client;

    [ComponentConfigureServices]
    public static void ConfigureServices(
        IServiceCollection services, IConfiguration configuration)
    {
        var address = new Uri(configuration["Catalog:BaseUrl"]
            ?? throw new InvalidOperationException("Catalog:BaseUrl is required."));
        services.TryAddSingleton(_ => new HttpClient
        {
            BaseAddress = address,
            Timeout = TimeSpan.FromSeconds(10)
        });
    }

    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync("health", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask<string> LoadAsync(CancellationToken cancellationToken)
        => await _client.GetStringAsync("catalog", cancellationToken).ConfigureAwait(false);
}
```

Registration is synchronous and performs no I/O. It accepts only unkeyed
Singleton registrations of closed stable types using types or factories, not
prebuilt instances. Root DI materializes resources before activation; the
framework then clears Hotfix factory delegates. **Resources stay alive** across
reload until root disposal. Do not retain Hotfix objects, delegates, tasks,
providers or collectible metadata inside a resource. In particular, do not add
a Hotfix HttpMessageHandler or long-lived Hotfix client callback. The framework
does not inspect arbitrary third-party object graphs for these references.

Here the factory captures only a stable Uri; Start performs the health probe.
No Stop is needed because HttpClient has no separate graceful-close protocol.
Root DI owns final Dispose. Do not share this unnamed HttpClient registration
with clients needing other settings; choose an appropriate dedicated stable
client type in that case. App `AddServices` remains valid for global host
composition. Registration and Start edits take effect after restart.

## State-owned asynchronous client

This outbound WebSocket tool connects on first Start and supports finite sends;
it owns no receive loop or background work. Configure `Feed:Uri` as a `ws`/`wss`
endpoint. Connection establishment includes the protocol handshake.

```csharp
// Server.App/Feed/FeedState.cs
using System;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using Lakona.Game.Server.Hotfix.Abstractions;

[assembly: InternalsVisibleTo("Example.Hotfix")]

namespace Example.App;

[ComponentState]
public sealed class FeedState : IDisposable
{
    internal ClientWebSocket? Connection;
    internal readonly SemaphoreSlim SendGate = new(1, 1);

    public void Dispose()
    {
        try { Interlocked.Exchange(ref Connection, null)?.Dispose(); }
        finally { SendGate.Dispose(); }
    }
}
```

```csharp
// Server.Hotfix/Feed/FeedSender.cs
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Example.App;
using Lakona.Game.Server;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Example.Hotfix;

[HotfixComponent, NodeRole("data")]
public sealed class FeedSender
{
    private readonly FeedState _state;
    private readonly IConfiguration _configuration;

    public FeedSender(FeedState state, IConfiguration configuration)
    {
        _state = state;
        _configuration = configuration;
    }

    [ComponentStart]
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_configuration["Feed:Uri"]
            ?? throw new InvalidOperationException("Feed:Uri is required."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        ClientWebSocket? candidate = new();
        try
        {
            await candidate.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            _state.Connection = candidate;
            candidate = null; // Ownership transfers only after successful initialization.
        }
        catch (Exception startupError)
        {
            try { candidate?.Dispose(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Feed startup and cleanup failed.",
                    startupError, cleanupError);
            }
            throw;
        }
    }

    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var connection = _state.Connection
            ?? throw new InvalidOperationException("Feed has not started.");
        await _state.SendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.SendAsync(payload, WebSocketMessageType.Binary,
                endOfMessage: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { _state.SendGate.Release(); }
    }

    [ComponentStop]
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_state.Connection is { State: WebSocketState.Open or WebSocketState.CloseReceived } connection)
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure,
                "Node stopping", cancellationToken).ConfigureAwait(false);
        // State/root DI performs final Dispose even if graceful close fails.
    }
}
```

State's implicit public parameterless constructor initializes only basic
structures. The framework registers and root-owns it; only FeedSender accesses
it.

FeedSender injects FeedState directly and stores it only in `_state`. Keep that
single storage location: do not add another field/auto-property, wrap State in
Func/Lazy/collections, or retain it as object/an interface. With a primary
constructor, choose direct parameter capture or one explicit storage member;
using both retains two copies of the reference. Stable resource dependencies
such as client interfaces and resource factories are still allowed.

Every custom App dependency injected into a component requires `[ComponentState]`,
even for reads or method-only use. Unmarked App classes/interfaces and wrappers
report `LAKONA20061`; manual Singleton registration is not a substitute. Keep only
data and actual disposal implementations in State; initialization, queries and
refresh helpers belong in the component (`LAKONA20062`). Framework services,
generated ActorAccess, third-party resources and ILogger<T> categories remain
valid. App DTO method arguments and new local values are unaffected. Checks use
project-role metadata and declarations, not names or arbitrary method effects.
FeedState's stable gate serializes sends across generation overlap because
ClientWebSocket permits only one send at a time. Ordinary component calls have
no mailbox. Callers must stop and await their sends before Stop closes the socket.

Start awaits acquisition completely and disposes failed local candidates. For
libraries without cancellable acquisition, configure finite native timeouts and
await completion before checking cancellation; do not abandon acquisition with
`WaitAsync(token)`. Preserve the original startup error if cleanup also fails.
Do not register the State-owned handle with another DI disposal owner. Other
constructors inject the component, not a factory that requires completed Start.

## Reload and shutdown

Reload does not reconnect clients, rerun initialization or change values captured
during Start.
Start and Stop can run on different Hotfix instances. Component names, effective
set (including stateless tools), State binding/layout and registration changes
require restart. Ensure custom callers have stopped before closing resources,
and verify affected resource/readiness failures against real dependencies.
