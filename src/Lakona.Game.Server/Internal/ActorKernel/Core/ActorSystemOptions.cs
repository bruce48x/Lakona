namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorSystemOptions
{
    public int MailboxCapacity { get; init; } = 1024;

    public TimeSpan? SlowMessageThreshold { get; init; }

    public IActorMessageInterceptor? MessageInterceptor { get; init; }
}
