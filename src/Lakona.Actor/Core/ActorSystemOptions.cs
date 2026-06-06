namespace Lakona.Actor;

public sealed class ActorSystemOptions
{
    public int MailboxCapacity { get; init; } = 1024;

    public TimeSpan? SlowMessageThreshold { get; init; }

    public IActorMessageInterceptor? MessageInterceptor { get; init; }
}
