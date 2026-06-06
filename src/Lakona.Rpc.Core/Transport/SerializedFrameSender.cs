namespace Lakona.Rpc.Core;

internal sealed class SerializedFrameSender : IDisposable
{
    private readonly RpcKeepAliveState _keepAliveState;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ITransport _transport;

    public SerializedFrameSender(ITransport transport, RpcKeepAliveState keepAliveState)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _keepAliveState = keepAliveState ?? throw new ArgumentNullException(nameof(keepAliveState));
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _transport.SendFrameAsync(frame, ct).ConfigureAwait(false);
            _keepAliveState.MarkSent();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}
