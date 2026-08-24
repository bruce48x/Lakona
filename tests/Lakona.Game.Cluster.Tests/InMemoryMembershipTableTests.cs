using Lakona.Game.Cluster.Membership;

namespace Lakona.Game.Cluster.Tests;

public sealed class InMemoryMembershipTableTests : MembershipTableContractTests
{
    private protected override ValueTask<IMembershipTable?> CreateTableAsync() =>
        new(new InMemoryMembershipTable());
}
