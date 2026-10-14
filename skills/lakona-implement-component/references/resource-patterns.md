# Component Resource Patterns

## Root-owned synchronous client

Use optional ComponentConfigureServices on the HotfixComponent before root
Build; its NodeRole filters registration automatically. The exact method is
public static non-generic void(IServiceCollection, IConfiguration). It needs
no State. Register a provider-owned client
factory, for example NpgsqlDataSource. Inject the client into a concrete Hotfix
Store, initialize and probe in ComponentStart. Omit ComponentStop unless a
separate graceful closure is needed; root DI performs final disposal. SQL and
business rules remain in Hotfix. Use closed stable types and unkeyed singleton
type/factory registrations only. Root DI creates factory resources before first
activation, then the framework drops Hotfix factory delegates. Never retain
Hotfix objects, callbacks or metadata in the resulting resource; registration
edits take effect after restart. The first assembly is reused by HotfixManager.

## State-owned asynchronous client

Declare ComponentState in App holding stable client handles and startup values
needed by later generations. Its constructor initializes basic structures only;
root DI creates and owns the State. Mechanical Dispose releases its handle once.

Start awaits connection creation/probe completely, checks cancellation and
transfers the successful candidate to State. Dispose failed local candidates.
Configure finite client timeouts; do not abandon creation with WaitAsync(token).
Preserve the original startup error if cleanup fails too.

Other constructors inject the component, never a gated client factory which
requires Start to have finished. Methods obtain the handle from State. Latest
generation Stop gracefully closes it; State/root performs final Dispose. Do not
register another DI disposal owner for a State-owned handle.

## Agar

PostgresUserStore in Hotfix injects the App-registered NpgsqlDataSource, initializes
the schema and probes on first Start, then owns user SQL. Its own
ComponentConfigureServices declares the stable pool registration, with no Program
call or extra State. RedisLeaderboardStore connects/probes in Start, performs operations and
closes in Stop. App RedisLeaderboardState retains the connection and key prefix
across reload and disposes it finally. Both stores use NodeRole(data); consumers
inject concrete Store types. Matchmaking/room timers remain Actor + Behavior.

Reload does not reconnect clients, rerun migrations or change startup prefixes.
Component set or State shape changes require restart. Ensure custom callers have
stopped before closing resources, and test real persistence/readiness failures.
