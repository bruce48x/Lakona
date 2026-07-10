using System.Net;

namespace Lakona.Game.Server.LocalAdmin;

internal interface ILakonaLocalAdminListener
{
    bool IsListening { get; }

    void AddPrefix(string prefix);

    void Start();

    Task<HttpListenerContext> GetContextAsync(CancellationToken cancellationToken);

    void Close();
}

internal sealed class SystemLakonaLocalAdminListener : ILakonaLocalAdminListener
{
    private readonly HttpListener _listener = new();

    public bool IsListening => _listener.IsListening;

    public void AddPrefix(string prefix)
    {
        _listener.Prefixes.Add(prefix);
    }

    public void Start()
    {
        _listener.Start();
    }

    public Task<HttpListenerContext> GetContextAsync(CancellationToken cancellationToken)
    {
        return _listener.GetContextAsync().WaitAsync(cancellationToken);
    }

    public void Close()
    {
        _listener.Close();
    }
}
