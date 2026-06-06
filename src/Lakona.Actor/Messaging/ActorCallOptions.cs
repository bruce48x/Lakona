namespace Lakona.Actor;

public sealed record ActorCallOptions(TimeSpan QueueTimeout, TimeSpan ResponseTimeout);
