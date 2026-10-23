using System.Diagnostics.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>Startup context with optional input for this activation only.</summary>
public sealed class ActorStartCall<TArgs>
{
    private readonly TArgs? _arguments;
    private readonly bool _hasArguments;

    public ActorStartCall(object actorId, IServiceProvider services, object? arguments,
        bool hasArguments, CancellationToken cancellationToken = default)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        CancellationToken = cancellationToken;
        if (hasArguments && arguments is not TArgs)
            throw new ArgumentException("Actor start arguments must match the declared type and cannot be null.", nameof(arguments));
        _hasArguments = hasArguments;
        _arguments = hasArguments ? (TArgs)arguments! : default;
    }

    public object ActorId { get; }
    public IServiceProvider Services { get; }
    public CancellationToken CancellationToken { get; }

    public bool TryGetArguments([MaybeNullWhen(false)] out TArgs arguments)
    {
        arguments = _arguments!;
        return _hasArguments;
    }
}
