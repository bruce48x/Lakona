# Lakona Module Resource Patterns

Use the pattern matching the resource's construction and node topology. Keep
all runtime objects in stable App assemblies.

## Synchronously constructible provider-owned client

Register the client and application adapter before provider construction:

```csharp
public void ConfigureServices(
    IServiceCollection services,
    IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException(
            "The PostgreSQL connection string is required.");

    services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
    services.AddSingleton<PostgresUserStore>();
    services.AddSingleton<IUserStore>(provider =>
        provider.GetRequiredService<PostgresUserStore>());
}
```

Resolve, initialize, and probe in `StartAsync`:

```csharp
public async Task StartAsync(
    ILakonaModuleContext context,
    CancellationToken cancellationToken)
{
    var store = context.Services.GetRequiredService<PostgresUserStore>();
    await store.InitializeAsync(cancellationToken).ConfigureAwait(false);

    var dataSource = context.Services.GetRequiredService<NpgsqlDataSource>();
    await using var connection = await dataSource
        .OpenConnectionAsync(cancellationToken)
        .ConfigureAwait(false);
    // Execute the smallest probe that proves the resource is usable.
}
```

The root provider owns final disposal. Keep `StopAsync` empty unless the client
has a distinct graceful-stop operation.

## Asynchronously created provider-owned client

Declare the DI identity synchronously, even though the object is connected
later:

```csharp
private ConnectionMultiplexer? connection;

public void ConfigureServices(
    IServiceCollection services,
    IConfiguration configuration)
{
    services.AddSingleton<ConnectionMultiplexer>(_ =>
        Volatile.Read(ref connection)
        ?? throw new InvalidOperationException(
            "Redis has not completed startup."));
    services.AddSingleton<IDatabase>(provider =>
        provider.GetRequiredService<ConnectionMultiplexer>().GetDatabase());
    services.AddSingleton<RedisLeaderboardStore>();
}
```

Connect, probe, publish, and force provider ownership in `StartAsync`:

```csharp
ConnectionMultiplexer? candidate = null;
var providerOwnsCandidate = false;
try
{
    cancellationToken.ThrowIfCancellationRequested();
    candidate = await ConnectionMultiplexer
        .ConnectAsync(options)
        .ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    _ = await candidate.GetDatabase()
        .PingAsync()
        .WaitAsync(cancellationToken)
        .ConfigureAwait(false);

    Volatile.Write(ref connection, candidate);
    var registered = context.Services
        .GetRequiredService<ConnectionMultiplexer>();
    providerOwnsCandidate = ReferenceEquals(candidate, registered);
    if (!providerOwnsCandidate)
    {
        throw new InvalidOperationException(
            "The DI singleton does not match the connected instance.");
    }
}
catch (Exception startupError)
{
    if (candidate is not null)
    {
        _ = Interlocked.CompareExchange(ref connection, null, candidate);
        try
        {
            try
            {
                await candidate.CloseAsync(false).ConfigureAwait(false);
            }
            finally
            {
                if (!providerOwnsCandidate)
                {
                    candidate.Dispose();
                }
            }
        }
        catch (Exception cleanupError)
        {
            throw new AggregateException(startupError, cleanupError);
        }
    }

    throw;
}
```

Gracefully close the published object during module shutdown:

```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
    var current = Interlocked.Exchange(ref connection, null);
    if (current is not null)
    {
        await current.CloseAsync(true).ConfigureAwait(false);
    }
}
```

Configure finite connection timeouts and retry limits in `options`. This example
awaits the non-cancellable connection operation to completion before observing
cancellation, so any successfully created connection is assigned and cleaned up.
Do not wrap resource creation in `WaitAsync(cancellationToken)` and abandon the
underlying task: it can later return a disposable resource nobody owns. If prompt
cancellation is required, explicitly own and drain the pending creation task.
The probe can use `WaitAsync` because the candidate is already owned.

The root provider performs final `Dispose` after ownership is established.
Before that point, startup failure disposes the candidate even if graceful close
throws. Cleanup errors retain the original startup error in an aggregate.

## Role-scoped resource

Declare role ownership on the module type:

```csharp
[NodeRole("data")]
public sealed class RedisModule : ILakonaModule
{
    // ConfigureServices, StartAsync, and StopAsync
}
```

Lakona constructs this module only when `data` appears in
`Lakona:Node:Roles`. On a selected node, require the connection string and
register the real client graph; missing configuration, authentication, schema,
migration, or probe failures remain startup failures. On every other node the
module is absent, so do not register a fake adapter merely to make the process
start. Place Actors and HTTP services which consume the resource on compatible
roles and test that topology explicitly.

## Review checklist

- `ConfigureServices` performs declarations only.
- The module declares exactly one `[NodeRole]` and selected nodes provide its
  required configuration.
- `StartAsync` proves the resource usable before returning.
- Partial startup cleans up its candidate.
- `StopAsync` is safe after partial or repeated execution.
- The final provider returns one resource instance.
- Business constructors depend on the resource or narrow adapter, not the
  lifecycle module.
- Missing configuration behavior matches the node topology.
- Hotfix constructor validation succeeds without creating unwanted external
  clients.
- Existing or new tests cover affected role inclusion, role exclusion, missing
  configuration on a selected node, unhealthy dependencies, and shutdown paths.
