namespace Lakona.Game.Server.Actors;

internal sealed class ActorActivationRollbackRecorder
{
    private readonly AsyncLocal<Scope?> current = new();

    public Scope BeginScope()
    {
        var scope = CreateScope();
        scope.ActivateUntilDisposed();
        return scope;
    }

    public Scope CreateScope() => new(this);

    public void RecordCreated(Type actorType, ActorId actorId) =>
        current.Value?.RecordCreated(actorType, actorId);

    public void RecordDestroyed(Type actorType, ActorId actorId) =>
        current.Value?.RecordDestroyed(actorType, actorId);

    internal sealed class Scope(ActorActivationRollbackRecorder owner) : IAsyncDisposable
    {
        private readonly List<Record> created = [];
        private readonly object gate = new();
        private IDisposable? activation;
        private bool disposed;

        public IReadOnlyList<Record> Created
        {
            get { lock (gate) return created.ToArray(); }
        }

        public void RecordCreated(Type actorType, ActorId actorId)
        {
            lock (gate) created.Add(new Record(actorType, actorId));
        }

        public void RecordDestroyed(Type actorType, ActorId actorId)
        {
            lock (gate) created.RemoveAll(record =>
                record.ActorType == actorType && record.ActorId == actorId);
        }

        public IDisposable Activate()
        {
            var parent = owner.current.Value;
            owner.current.Value = this;
            return new Activation(owner, parent);
        }

        internal void ActivateUntilDisposed() => activation = Activate();

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                activation?.Dispose();
                disposed = true;
            }

            return default;
        }

        private sealed class Activation(
            ActorActivationRollbackRecorder owner,
            Scope? parent) : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                owner.current.Value = parent;
                disposed = true;
            }
        }
    }

    internal readonly record struct Record(Type ActorType, ActorId ActorId);
}
