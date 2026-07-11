namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimePublicationParticipant
{
    ValueTask<IHotfixRuntimePublicationTransaction> PrepareAsync(
        HotfixRuntimeSnapshot previous,
        HotfixRuntimeSnapshot candidate,
        CancellationToken cancellationToken = default);
}

public interface IHotfixRuntimePublicationTransaction : IAsyncDisposable
{
    ValueTask ActivateAsync(CancellationToken cancellationToken = default);

    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class NoopHotfixRuntimePublicationTransaction : IHotfixRuntimePublicationTransaction
{
    public static NoopHotfixRuntimePublicationTransaction Instance { get; } = new();

    private NoopHotfixRuntimePublicationTransaction() { }

    public ValueTask ActivateAsync(CancellationToken cancellationToken = default) => default;

    public ValueTask CommitAsync(CancellationToken cancellationToken = default) => default;

    public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => default;

    public ValueTask DisposeAsync() => default;
}
