using System.ComponentModel;

namespace Lakona.Rpc.Server;

/// <summary>Framework cooperation boundary for publications which must precede a request's response.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcResponsePublicationScope : IDisposable
{
    private static readonly AsyncLocal<RpcResponsePublicationScope?> Ambient = new();
    private readonly RpcResponsePublicationScope? _previous;
    private readonly string _connectionId;
    private readonly object _gate = new();
    private readonly List<Func<string, CancellationToken, ValueTask>> _publications = new();
    private bool _closed;

    public static RpcResponsePublicationScope? Capture() => Ambient.Value;

    public static IDisposable Attach(RpcResponsePublicationScope? scope)
    {
        var previous = Ambient.Value;
        Ambient.Value = scope;
        return new Attachment(previous);
    }

    internal RpcResponsePublicationScope(string connectionId)
    {
        _connectionId = connectionId;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static void Register(Func<string, CancellationToken, ValueTask> publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var scope = Ambient.Value;
        if (scope is null) return;
        lock (scope._gate)
        {
            // Detached work outliving its request cannot extend that request's response boundary.
            if (!scope._closed) scope._publications.Add(publication);
        }
    }

    internal async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        Func<string, CancellationToken, ValueTask>[] publications;
        lock (_gate)
        {
            _closed = true;
            publications = _publications.ToArray();
        }
        foreach (var publication in publications)
            await publication(_connectionId, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate) _closed = true;
        Ambient.Value = _previous;
    }

    private sealed class Attachment(RpcResponsePublicationScope? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
