namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedEndpoint(
    LakonaGameResolvedValue<string> Transport,
    LakonaGameResolvedValue<string> Serializer,
    LakonaGameResolvedValue<string> Host,
    LakonaGameResolvedValue<int> Port,
    LakonaGameResolvedValue<string> Path,
    LakonaGameResolvedValue<string> AdvertisedHost,
    LakonaGameResolvedValue<string> AdvertisedEndpoint,
    IReadOnlyList<string> RpcServices)
{
    public LakonaGameResolvedValue<int> MaxActiveConnections { get; init; } =
        new(
            Configuration.LakonaGameEndpointConnectionLimitsOptions.DefaultMaxActiveConnections,
            LakonaGameValueSource.Default);

    public LakonaGameResolvedValue<int> MaxPendingHandshakes { get; init; } =
        new(
            Configuration.LakonaGameEndpointConnectionLimitsOptions.DefaultMaxPendingHandshakes,
            LakonaGameValueSource.Default);

    public LakonaGameResolvedValue<TimeSpan> HandshakeTimeout { get; init; } =
        new(
            Configuration.LakonaGameEndpointConnectionLimitsOptions.DefaultHandshakeTimeout,
            LakonaGameValueSource.Default);
}
