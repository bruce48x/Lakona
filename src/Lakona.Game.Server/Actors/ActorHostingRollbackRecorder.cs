namespace Lakona.Game.Server.Actors;

internal sealed class ActorHostingRollbackRecorder
{
    private readonly AsyncLocal<Scope?> _current = new();

    public Scope BeginScope()
    {
        var scope = new Scope(this, _current.Value);
        _current.Value = scope;
        return scope;
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
        private readonly Scope? _parent;
        private readonly List<Record> _created = [];
        private bool _disposed;

        public Scope(ActorHostingRollbackRecorder owner, Scope? parent)
        {
            _owner = owner;
            _parent = parent;
        }

        public IReadOnlyList<Record> Created => _created;

        public void RecordCreated(Type actorType, ActorId actorId)
        {
            _created.Add(new Record(actorType, actorId));
        }

        public void RecordDestroyed(Type actorType, ActorId actorId)
        {
            _created.RemoveAll(record => record.ActorType == actorType && record.ActorId == actorId);
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _owner._current.Value = _parent;
                _disposed = true;
            }

            return default;
        }
    }

    internal readonly record struct Record(Type ActorType, ActorId ActorId);
}
