namespace Lakona.Game.Server.Health;

public interface ILakonaHealthHttpRoute
{
    string Method { get; }

    string Path { get; }

    ValueTask<LakonaHealthHttpResponse> HandleAsync(
        LakonaHealthHttpRequest request,
        CancellationToken cancellationToken = default);
}
