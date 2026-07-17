namespace Lakona.Hub;

internal sealed class HubWindowLifetime : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();

    public CancellationToken Token => cancellation.Token;

    public bool IsClosing { get; private set; }

    public void Close()
    {
        if (IsClosing)
        {
            return;
        }

        IsClosing = true;
        cancellation.Cancel();
    }

    public void Dispose()
    {
        Close();
        cancellation.Dispose();
    }
}
