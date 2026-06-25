namespace Agar.Sample.State.Contracts;

public readonly record struct UserId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RoomId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MatchmakingQueueId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LeaderboardId(string Value)
{
    public override string ToString() => Value;
}
