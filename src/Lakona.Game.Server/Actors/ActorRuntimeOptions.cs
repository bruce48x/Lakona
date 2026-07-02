namespace Lakona.Game.Server.Actors;

/// <summary>
/// Configures the process-local actor runtime.
/// </summary>
public sealed class ActorRuntimeOptions
{
    /// <summary>
    /// Gets or sets the bounded mailbox capacity for each actor.
    /// </summary>
    public int MailboxCapacity { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the default timeout for actor request/reply calls.
    /// </summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the duration after which actor message handling is reported as slow.
    /// </summary>
    public TimeSpan? SlowMessageThreshold { get; set; }

    /// <summary>
    /// Gets or sets an interceptor that observes actor message dispatch.
    /// </summary>
    public IActorMessageInterceptor? MessageInterceptor { get; set; }

    /// <summary>
    /// Gets or sets a handler for dead-letter diagnostics.
    /// </summary>
    public Action<ActorDeadLetterDiagnostic>? DeadLetterHandler { get; set; }

    /// <summary>
    /// Gets or sets a handler for slow-message diagnostics.
    /// </summary>
    public Action<ActorSlowMessageDiagnostic>? SlowMessageHandler { get; set; }

    /// <summary>
    /// Gets or sets a handler for actor call timeout diagnostics.
    /// </summary>
    public Action<ActorCallTimeoutDiagnostic>? CallTimeoutHandler { get; set; }
}
