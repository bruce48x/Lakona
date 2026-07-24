# Application Modules

Lakona application modules own stable, process-scoped resources that must be
usable before a node accepts business work. Typical examples are database
clients, Redis connections, application caches, and product-owned background
workers.

Modules are a process resource lifecycle seam. They are not Actors, Hotfix
plugins, dependency health monitors, or an alternative dependency-injection
container.

Framework bootstrap and host execution are also outside this abstraction.
Lakona keeps those fixed orchestration steps in its internal bootstrapper and
runner; `ILakonaModule` remains an application extension point for resources
owned by `Server.App`.

## Interface

```csharp
public interface ILakonaModule
{
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration);

    Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}

public interface ILakonaModuleContext
{
    IConfiguration Configuration { get; }

    IServiceProvider Services { get; }
}
```

The interface has two phases:

- `ConfigureServices` declares the stable object graph before the root provider
  is built.
- `StartAsync` and `StopAsync` manage operational resources after the final
  provider exists.

Registration is deliberately synchronous. `ConfigureServices` must not perform
network I/O, run migrations, start background work, resolve services, or build a
temporary provider. Asynchronous work belongs in `StartAsync`.

`ILakonaModuleContext.Services` is the single final root provider. It is a
lifecycle facility for resolving module-level handles during startup, not a
replacement for constructor injection in business adapters.

## Discovery

`LakonaGameServer.RunAsync` automatically discovers module types from stable
application assemblies. Production discovery excludes test and Hotfix
assemblies.

A module must be:

- public and sealed;
- non-abstract and non-generic;
- assignable to `ILakonaModule`;
- constructible through a public parameterless constructor.

Lakona creates exactly one instance of each module. It registers that same
instance under its concrete type and `ILakonaModule`, then invokes
`ConfigureServices` once. Modules are ordered by `Type.FullName` with ordinal
comparison. Duplicate module identities or invalid module shapes fail startup.

Applications do not manually register discovered modules:

```csharp
return await LakonaGameServer.RunAsync(args, static server => server
    .UseClusterRpc(transport, serializer)
    .RegisterEndpointTransport("websocket", CreateWebSocket)
    .RegisterEndpointSerializer("memorypack", CreateMemoryPack));
```

## Startup

Lakona starts application modules sequentially in discovery order before
loading the initial Hotfix generation or starting framework listeners,
membership publication, Startup Actors, and business work.

`StartAsync` must not return until the module can serve its consumers. It may:

- connect to a database or Redis;
- verify or initialize application schema;
- load required application data;
- start application-owned background work.

If `StartAsync` creates a partial resource and then fails, the failing module
must clean up that partial resource before throwing. Lakona then stops every
previously started module in reverse order, continues rollback after individual
stop failures, preserves the original startup exception, and leaves the process
NotReady.

If Hotfix or framework startup fails after modules have started, Lakona stops
the framework before stopping application modules.

## Readiness

Readiness is owned by Lakona. Modules do not publish Ready or NotReady.

The process remains NotReady while modules, initial Hotfix loading, and
framework hosted facilities start. It becomes Ready only after every module
and framework startup callback succeeds.

On shutdown Lakona enters NotReady before business consumers stop. Module
startup failures are reported through `LAKONA151`; startup pending and shutdown
states use `LAKONA150` and `LAKONA152`.

Successful startup validation is not continuous dependency health monitoring.
Database and Redis clients may reconnect according to their own policies.
Runtime degradation reporting, if added later, is separate from resource
ownership.

## Shutdown

Normal shutdown is:

1. enter NotReady;
2. stop accepting distributed and client work;
3. stop framework listeners, Actor work, Hotfix consumers, and other hosted
   facilities;
4. stop application modules in reverse startup order;
5. dispose the final root provider.

`StopAsync` should tolerate partial initialization and repeated calls. Lakona
continues stopping remaining modules after a failure.

## Resource Ownership

Every runtime resource has exactly one disposal owner.

### DI-owned resources

A resource created by an implementation type or factory registered in
`ConfigureServices` belongs to the final root provider:

```csharp
public sealed class PostgresModule : ILakonaModule
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ =>
            NpgsqlDataSource.Create(
                configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException(
                    "PostgreSQL connection string is missing.")));
        services.AddSingleton<IUserStore, PostgresUserStore>();
    }

    public async Task StartAsync(
        ILakonaModuleContext context,
        CancellationToken cancellationToken)
    {
        var dataSource =
            context.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT 1;",
                cancellationToken: cancellationToken));
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

Consumers may inject the resource normally. The module validates it but does
not dispose it. Lakona disposes the final provider after modules stop.

Do not create a disposable runtime object and pass it to
`AddSingleton(instance)`: the built-in container does not own disposal for that
instance.

### Module-owned resources

A resource created asynchronously in `StartAsync` belongs to its module. The
module publishes it only after successful initialization and releases it in
`StopAsync`.

This is appropriate for `ConnectionMultiplexer`, whose connected construction
is asynchronous. A stable adapter may receive the owning module through
constructor injection and access an internal ready-resource seam. Hotfix code
continues to depend only on the stable business interface.

The following ownership shapes are invalid:

- both DI and a module dispose the same resource;
- a business adapter disposes a module-owned client;
- Hotfix creates or disposes a stable runtime client;
- a module mutates the service collection after the provider is built;
- startup builds another provider to expose late registrations.

## Independence

Type-name ordering provides determinism and reverse cleanup, not dependencies
or priorities. A module must not assume another module has completed
`StartAsync`.

Resources with a strict startup relationship belong in one module. The first
version does not provide module dependencies, optional modules, priorities,
module hot reload, or automatic reconstruction after failure.

## Agar Example

`samples/Game.Unity.Agar` demonstrates both ownership forms:

- `AgarPostgresModule` registers the DI-owned `NpgsqlDataSource` and
  `IUserStore`, initializes the user schema, and probes PostgreSQL when its
  connection string is configured.
- `AgarRedisModule` creates and owns the asynchronously connected Redis
  multiplexer and registers the stable `ILeaderboardStore` adapter when its
  connection string is configured.

An absent connection string is an application-level decision that makes the
corresponding Agar module skip external client creation and connection; it is
not a framework-level optional-module contract. The module still registers a
fail-fast Store adapter so Hotfix constructor validation succeeds, while an
incorrectly local persistence call reports a topology error. In the three-node
topology only `data-1` receives both connection strings. Once configured,
connection or initialization failure still fails startup and keeps that node
NotReady.

`Server.Hotfix` sees only `IUserStore` and `ILeaderboardStore`. It does not
reference Npgsql, StackExchange.Redis, or either module type.
