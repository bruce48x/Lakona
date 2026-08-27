using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

[Trait("Category", "InMemoryIntegration")]
public sealed class InMemoryMembershipTableTests : MembershipTableContractTests
{
    private protected override ValueTask<IMembershipTable?> CreateTableAsync() =>
        new(new InMemoryMembershipTable());
}
