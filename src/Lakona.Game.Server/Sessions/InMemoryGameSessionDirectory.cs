using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class InMemoryGameSessionDirectory : IGameSessionDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OwnerSessionState> _owners = new(StringComparer.Ordinal);

    public ValueTask<GameSessionKey> StartNewSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var generation = _owners.TryGetValue(ownerKey, out var existing)
                ? existing.Session.Generation + 1
                : 1;

            var session = new GameSessionKey(ownerKey, Guid.NewGuid().ToString("N"), generation);
            _owners[ownerKey] = new OwnerSessionState(session);
            return new ValueTask<GameSessionKey>(session);
        }
    }

    public ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_owners.TryGetValue(session.OwnerKey, out var state) ||
                !state.Session.Equals(session))
            {
                return new ValueTask<SessionResumeDecision>(
                    SessionResumeDecision.StateLost("Session was not found or generation changed."));
            }

            if (state.IsTerminated)
            {
                return new ValueTask<SessionResumeDecision>(state.Termination is null
                    ? SessionResumeDecision.StateLost("Session was terminated.")
                    : SessionResumeDecision.Terminated(state.Termination));
            }

            state.LastSeenAt = DateTimeOffset.UtcNow;
            return new ValueTask<SessionResumeDecision>(SessionResumeDecision.Resumed(state.Session));
        }
    }

    public ValueTask BindEndpointAsync<TCallback>(
        SessionEndpointKey endpoint,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateEndpoint(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var state = GetCurrentSessionState(endpoint.Session);
            if (state.IsTerminated)
            {
                throw new InvalidOperationException("Session is terminated.");
            }

            var aggregate = state.GetOrAddEndpoint(endpoint.EndpointName.Value);
            aggregate.Bindings[typeof(TCallback)] = new EndpointBinding(
                connectionId,
                callback,
                typeof(TCallback),
                DateTimeOffset.UtcNow);
            state.LastSeenAt = DateTimeOffset.UtcNow;
        }

        return default;
    }

    public ValueTask MarkSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool keepForResume,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(notice);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var state = GetCurrentSessionState(session);
            state.IsTerminated = true;
            state.Termination = keepForResume ? notice : null;
            state.LastSeenAt = DateTimeOffset.UtcNow;
        }

        return default;
    }

    public ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>> MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var disconnectedAt = DateTimeOffset.UtcNow;
            var snapshots = new List<GameSessionEndpointSnapshot>();

            foreach (var state in _owners.Values)
            {
                foreach (var endpoint in state.Endpoints)
                {
                    var matched = endpoint.Value.Disconnect(connectionId, disconnectedAt);
                    if (matched.Count == 0)
                    {
                        continue;
                    }

                    snapshots.Add(new GameSessionEndpointSnapshot(
                        new SessionEndpointKey(state.Session, endpoint.Key),
                        connectionId,
                        matched));
                    state.LastSeenAt = disconnectedAt;
                }
            }

            return new ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>>(snapshots);
        }
    }

    public ValueTask MarkEndpointDisconnectedAsync(
        SessionEndpointKey endpoint,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_owners.TryGetValue(endpoint.Session.OwnerKey, out var state) ||
                !state.Session.Equals(endpoint.Session) ||
                !state.Endpoints.TryGetValue(endpoint.EndpointName.Value, out var aggregate))
            {
                return default;
            }

            var matched = aggregate.Disconnect(connectionId, DateTimeOffset.UtcNow);
            if (matched.Count != 0)
                state.LastSeenAt = DateTimeOffset.UtcNow;
        }

        return default;
    }

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        SessionEndpointKey endpoint,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateEndpoint(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_owners.TryGetValue(endpoint.Session.OwnerKey, out var state) ||
                !state.Session.Equals(endpoint.Session) ||
                !state.Endpoints.TryGetValue(endpoint.EndpointName.Value, out var aggregate) ||
                !aggregate.TryGetActiveBinding<TCallback>(out var binding))
            {
                return new ValueTask<TCallback?>((TCallback?)null);
            }

            return new ValueTask<TCallback?>(binding.Callback as TCallback);
        }
    }

    public ValueTask<GameSessionEndpointBinding<TCallback>?> GetEndpointBindingAsync<TCallback>(
        SessionEndpointKey endpoint,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ValidateEndpoint(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_owners.TryGetValue(endpoint.Session.OwnerKey, out var state) ||
                !state.Session.Equals(endpoint.Session) ||
                !state.Endpoints.TryGetValue(endpoint.EndpointName.Value, out var aggregate) ||
                !aggregate.TryGetActiveBinding<TCallback>(out var binding) ||
                binding.Callback is not TCallback callback)
            {
                return new ValueTask<GameSessionEndpointBinding<TCallback>?>(
                    (GameSessionEndpointBinding<TCallback>?)null);
            }

            return new ValueTask<GameSessionEndpointBinding<TCallback>?>(
                new GameSessionEndpointBinding<TCallback>(
                    endpoint,
                    binding.ConnectionId,
                    callback));
        }
    }

    public ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>> ExpireDisconnectedEndpointsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var snapshots = new List<GameSessionEndpointSnapshot>();
            var expiredOwners = new List<string>();
            foreach (var owner in _owners)
            {
                var expiredEndpoints = new List<string>();
                foreach (var endpoint in owner.Value.Endpoints)
                {
                    var expired = endpoint.Value.Expire(disconnectedBefore);
                    foreach (var group in expired.GroupBy(binding => binding.ConnectionId, StringComparer.Ordinal))
                    {
                        snapshots.Add(new GameSessionEndpointSnapshot(
                            new SessionEndpointKey(owner.Value.Session, endpoint.Key),
                            group.Key,
                            group.Select(binding => binding.CallbackType).ToArray()));
                    }

                    if (endpoint.Value.Bindings.Count == 0)
                    {
                        expiredEndpoints.Add(endpoint.Key);
                    }
                }

                foreach (var endpointName in expiredEndpoints)
                {
                    owner.Value.Endpoints.Remove(endpointName);
                }

                if (owner.Value.Endpoints.Count == 0 &&
                    owner.Value.LastSeenAt < disconnectedBefore)
                {
                    expiredOwners.Add(owner.Key);
                }
            }

            foreach (var ownerKey in expiredOwners)
            {
                _owners.Remove(ownerKey);
            }

            return new ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>>(snapshots);
        }
    }

    private OwnerSessionState GetCurrentSessionState(GameSessionKey session)
    {
        if (!_owners.TryGetValue(session.OwnerKey, out var state) ||
            !state.Session.Equals(session))
        {
            throw new InvalidOperationException("Session was not found or generation changed.");
        }

        return state;
    }

    private static void ValidateEndpoint(SessionEndpointKey endpoint)
    {
        ValidateSession(endpoint.Session);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.EndpointName.Value);
    }

    private static void ValidateSession(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(session), "Session generation must be positive.");
        }
    }

    private sealed class OwnerSessionState
    {
        public OwnerSessionState(GameSessionKey session)
        {
            Session = session;
            LastSeenAt = DateTimeOffset.UtcNow;
        }

        public GameSessionKey Session { get; }

        public DateTimeOffset LastSeenAt { get; set; }

        public bool IsTerminated { get; set; }

        public SessionTerminationNotice? Termination { get; set; }

        public Dictionary<string, EndpointAggregate> Endpoints { get; } = new(StringComparer.Ordinal);

        public EndpointAggregate GetOrAddEndpoint(string endpointName)
        {
            if (!Endpoints.TryGetValue(endpointName, out var aggregate))
            {
                aggregate = new EndpointAggregate();
                Endpoints.Add(endpointName, aggregate);
            }

            return aggregate;
        }
    }

    private sealed class EndpointAggregate
    {
        public Dictionary<Type, EndpointBinding> Bindings { get; } = new();

        public IReadOnlyList<Type> Disconnect(string? connectionId, DateTimeOffset disconnectedAt)
        {
            var matched = new List<Type>();
            foreach (var binding in Bindings.ToArray())
            {
                if (connectionId is not null &&
                    !string.Equals(binding.Value.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (binding.Value.DisconnectedAt is null)
                {
                    matched.Add(binding.Value.CallbackType);
                }

                Bindings[binding.Key] = binding.Value.Disconnect(disconnectedAt);
            }

            return matched;
        }

        public IReadOnlyList<EndpointBinding> Expire(DateTimeOffset disconnectedBefore)
        {
            var expired = Bindings
                .Where(binding => binding.Value.DisconnectedAt < disconnectedBefore)
                .Select(binding => binding.Value)
                .ToArray();

            foreach (var binding in expired)
            {
                Bindings.Remove(binding.CallbackType);
            }

            return expired;
        }

        public bool TryGetActiveBinding<TCallback>(out EndpointBinding binding)
            where TCallback : class
        {
            if (Bindings.TryGetValue(typeof(TCallback), out binding!) &&
                binding.DisconnectedAt is null)
            {
                return true;
            }

            foreach (var candidate in Bindings.Values)
            {
                if (candidate.DisconnectedAt is null &&
                    candidate.Callback is TCallback)
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null!;
            return false;
        }
    }

    private sealed record EndpointBinding(
        string ConnectionId,
        object Callback,
        Type CallbackType,
        DateTimeOffset BoundAt,
        DateTimeOffset? DisconnectedAt = null)
    {
        public EndpointBinding Disconnect(DateTimeOffset disconnectedAt)
        {
            return this with { DisconnectedAt = disconnectedAt };
        }
    }
}
