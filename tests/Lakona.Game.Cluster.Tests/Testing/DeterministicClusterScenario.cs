using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests.Testing;

internal sealed class DeterministicClusterScenario(int seed)
{
    private readonly Random random = new(seed);
    private readonly List<string> events = [$"seed={seed}"];

    public int Seed { get; } = seed;

    public int Next(int minimum, int maximum) => random.Next(minimum, maximum);

    public void Record(string value) => events.Add(value);

    public void AssertOneLiveIncarnationPerNode(MembershipTableSnapshot snapshot)
    {
        var duplicates = snapshot.Entries
            .Where(static entry => entry.Status != MembershipTableStatus.Dead)
            .GroupBy(static entry => entry.Reference.Node)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"{group.Key.Value}={group.Count()}")
            .ToArray();
        Assert.True(duplicates.Length == 0, Describe($"duplicate live incarnations: {string.Join(", ", duplicates)}"));
    }

    public void AssertConverged(params ClusterMembershipState[] states)
    {
        var identities = states
            .Select(static state =>
                $"{state.Current.View.Value}:" + string.Join(
                    ",",
                    state.Current.Members.Select(member => member.Reference.ToString()).Order(StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(identities.Length == 1, Describe($"membership did not converge: {string.Join(" | ", identities)}"));
    }

    public string Describe(string failure) => $"{failure}; trace=[{string.Join("; ", events)}]";
}
