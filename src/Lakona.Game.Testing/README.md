# Lakona.Game.Testing

```powershell
dotnet add package Lakona.Game.Testing
```

`Lakona.Game.Testing` hosts multiple real Lakona server nodes in one test
process. Each node has its own Generic Host and dependency-injection container,
while the nodes share an in-memory Membership Table and communicate through an
in-memory cluster transport.

Use it for integration tests which need the real Membership, Actor Directory,
activation catalog, routing, and node lifecycle without starting several OS
processes:

```csharp
await using var cluster = new LakonaTestClusterBuilder()
    .AddNode("data-1", "data")
    .AddNode("battle-1", "battle")
    .ConfigureNodes(node =>
    {
        node.UseHotfixAssembly(typeof(GameHotfixStartup).Assembly);

        if (node.HasRole("data"))
        {
            node.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GameDatabase"] = database.ConnectionString
                }));
        }
    })
    .Build();

await cluster.StartAsync();
await cluster.WaitForMembershipAsync();

cluster.Network.Partition("data-1", "battle-1");
cluster.Network.Heal("data-1", "battle-1");

await cluster.RestartNodeAsync("battle-1");
await cluster.WaitForMembershipAsync();
```

`UseHotfixAssembly` loads the generated Actor API and Hotfix behavior table into
each real test host. Node roles still decide which Actor types a node advertises,
so calls issued through `ActorAccess` exercise normal placement, Directory,
cluster RPC, mailbox dispatch, and lifecycle behavior.

The test fixture owns application dependencies such as PostgreSQL, MySQL, and
Redis. Start a container or other disposable test resource in the fixture,
then inject its connection string only into the node roles which use it. This
package deliberately does not start those products or pretend to implement
their behavior.

The in-memory table and transport make tests fast and deterministic, but they
do not test PostgreSQL SQL behavior, real sockets, TLS, container networking,
or separate-process crashes. Keep provider contract tests and a smaller number
of process/container E2E tests for those boundaries.
