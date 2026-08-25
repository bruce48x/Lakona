namespace Lakona.Game.Server.Configuration;

/// <summary>Configures node-to-node cluster infrastructure under <c>Lakona:Cluster</c>.</summary>
public sealed class LakonaGameClusterOptions
{
    public const string DefaultEndpoint = "tcp://127.0.0.1:21001";

    public string Endpoint { get; init; } = DefaultEndpoint;
    public LakonaGameMembershipOptions Membership { get; init; } = new();

    public static LakonaGameClusterOptions Defaults() => new();
}

public sealed class LakonaGameMembershipOptions
{
    public const string MemoryProvider = "Memory";
    public const string PostgresProvider = "Postgres";

    public string Provider { get; init; } = MemoryProvider;
    public string ConnectionStringName { get; init; } = "LakonaClusterPostgres";
    public int ProbeIntervalSeconds { get; init; } = 10;
    public int ProbeTimeoutSeconds { get; init; } = 2;
    public int FailedProbesBeforeSuspect { get; init; } = 3;
    public int MonitoredNodes { get; init; } = 3;
    public int IndirectProbes { get; init; } = 2;
    public int VotesForDeath { get; init; } = 2;
    public int SuspectVoteLifetimeSeconds { get; init; } = 180;
    public int TableRefreshSeconds { get; init; } = 5;
    public int IAmAliveSeconds { get; init; } = 30;
    public int AllowedIAmAliveMissSeconds { get; init; } = 600;
    public int DefunctEntryRetentionSeconds { get; init; } = 604800;
    public int DefunctEntryCleanupIntervalSeconds { get; init; } = 3600;
    public int DefunctEntryCleanupBatchSize { get; init; } = 1000;

    internal void Validate()
    {
        if (!string.Equals(Provider, MemoryProvider, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Provider, PostgresProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Lakona:Cluster:Membership:Provider '{Provider}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(ConnectionStringName))
        {
            throw new InvalidOperationException("Lakona:Cluster:Membership:ConnectionStringName is required.");
        }

        var positive = new Dictionary<string, int>
        {
            [nameof(ProbeIntervalSeconds)] = ProbeIntervalSeconds,
            [nameof(ProbeTimeoutSeconds)] = ProbeTimeoutSeconds,
            [nameof(FailedProbesBeforeSuspect)] = FailedProbesBeforeSuspect,
            [nameof(MonitoredNodes)] = MonitoredNodes,
            [nameof(IndirectProbes)] = IndirectProbes,
            [nameof(VotesForDeath)] = VotesForDeath,
            [nameof(SuspectVoteLifetimeSeconds)] = SuspectVoteLifetimeSeconds,
            [nameof(TableRefreshSeconds)] = TableRefreshSeconds,
            [nameof(IAmAliveSeconds)] = IAmAliveSeconds,
            [nameof(AllowedIAmAliveMissSeconds)] = AllowedIAmAliveMissSeconds,
            [nameof(DefunctEntryRetentionSeconds)] = DefunctEntryRetentionSeconds,
            [nameof(DefunctEntryCleanupIntervalSeconds)] = DefunctEntryCleanupIntervalSeconds,
            [nameof(DefunctEntryCleanupBatchSize)] = DefunctEntryCleanupBatchSize
        };
        foreach (var value in positive)
        {
            if (value.Value <= 0)
            {
                throw new InvalidOperationException($"Lakona:Cluster:Membership:{value.Key} must be positive.");
            }
        }

        if (AllowedIAmAliveMissSeconds <= IAmAliveSeconds)
        {
            throw new InvalidOperationException(
                "Lakona:Cluster:Membership:AllowedIAmAliveMissSeconds must be greater than IAmAliveSeconds.");
        }

        if (IAmAliveSeconds <= TableRefreshSeconds)
        {
            throw new InvalidOperationException(
                "Lakona:Cluster:Membership:IAmAliveSeconds must be greater than TableRefreshSeconds.");
        }
        if (DefunctEntryRetentionSeconds <= AllowedIAmAliveMissSeconds)
        {
            throw new InvalidOperationException(
                "Lakona:Cluster:Membership:DefunctEntryRetentionSeconds must be greater than AllowedIAmAliveMissSeconds.");
        }
    }
}
