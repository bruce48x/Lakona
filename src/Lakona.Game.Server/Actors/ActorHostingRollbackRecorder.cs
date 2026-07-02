namespace Lakona.Game.Server.Actors;

internal sealed class ActorHostingRollbackRecorder
{
    private readonly AsyncLocal<Scope?> _current = new();

    public Scope BeginScope()
    {
        var scope = CreateScope();
        scope.ActivateUntilDisposed();
        return scope;
    }

    public Scope CreateScope()
    {
        return new Scope(this);
    }

    public void RecordCreated(Type actorType, ActorId actorId)
    {
        _current.Value?.RecordCreated(actorType, actorId);
    }

    public void RecordDestroyed(Type actorType, ActorId actorId)
    {
        _current.Value?.RecordDestroyed(actorType, actorId);
    }

    internal sealed class Scope : IAsyncDisposable
    {
        private readonly ActorHostingRollbackRecorder _owner;
        private readonly List<Record> _created = [];
        private readonly object _gate = new();
        private IDisposable? _activation;
        private bool _disposed;

        public Scope(ActorHostingRollbackRecorder owner)
        {
            _owner = owner;
        }

        public IReadOnlyList<Record> Created
        {
            get
            {
                lock (_gate)
                {
                    return _created.ToArray();
                }
            }
        }

        public void RecordCreated(Type actorType, ActorId actorId)
        {
            lock (_gate)
            {
                _created.Add(new Record(actorType, actorId));
            }
        }

        public void RecordDestroyed(Type actorType, ActorId actorId)
        {
            lock (_gate)
            {
                _created.RemoveAll(record => record.ActorType == actorType && record.ActorId == actorId);
            }
        }

        public IDisposable Activate()
        {
            var parent = _owner._current.Value;
            _owner._current.Value = this;
            return new Activation(_owner, parent);
        }

        internal void ActivateUntilDisposed()
        {
            _activation = Activate();
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _activation?.Dispose();
                _disposed = true;
            }

            return default;
        }

        private sealed class Activation(ActorHostingRollbackRecorder owner, Scope? parent) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                owner._current.Value = parent;
                _disposed = true;
            }
        }
    }

    internal readonly record struct Record(Type ActorType, ActorId ActorId);
}
