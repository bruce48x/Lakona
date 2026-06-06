namespace Lakona.Game.Server.Actors;

public sealed class ActorRuntimeOptions
{
    public int MailboxCapacity { get; set; } = 4096;

    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan? SlowMessageThreshold { get; set; }

    public global::Lakona.Actor.IActorMessageInterceptor? MessageInterceptor { get; set; }

    public Action<ActorDeadLetterDiagnostic>? DeadLetterHandler { get; set; }

    public Action<ActorSlowMessageDiagnostic>? SlowMessageHandler { get; set; }

    public Action<ActorCallTimeoutDiagnostic>? CallTimeoutHandler { get; set; }
}
