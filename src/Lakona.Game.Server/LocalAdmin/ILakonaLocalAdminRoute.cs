namespace Lakona.Game.Server.LocalAdmin;

public interface ILakonaLocalAdminRoute
{
    string Method { get; }

    string Path { get; }

    ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default);
}
