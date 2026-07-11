namespace Lakona.Game.Server.InternalHttp;

public interface ILakonaHttpRoute
{
    string Method { get; }

    string Path { get; }

    bool RequireLoopback { get; }

    ValueTask<LakonaHttpResponse> HandleAsync(
        LakonaHttpRequest request,
        CancellationToken cancellationToken = default);
}
