using Lakona.Rpc.Server.Observability;

namespace Lakona.Game.Server.Observability;

/// <summary>
/// Names the OpenTelemetry-compatible instrumentation scopes emitted by the Lakona game server.
/// </summary>
/// <remarks>
/// Lakona emits telemetry through the .NET <c>Meter</c>, <c>ActivitySource</c>, and
/// <c>ILogger</c> contracts. Applications select and configure their OpenTelemetry SDK,
/// processors, exporters, and backends independently.
/// </remarks>
public static class LakonaGameServerTelemetry
{
    public const string ActorMeterName = "Lakona.Game.Actor";
    public const string ClusterMeterName = "Lakona.Game.Cluster";
    public const string ReliablePushMeterName = "Lakona.Game.ReliablePush";
    public const string RpcServerMeterName = LakonaRpcServerTelemetry.MeterName;
    public const string SessionMeterName = "Lakona.Game.Session";
    public const string TimerMeterName = "Lakona.Game.Timer";

    public const string ActorActivitySourceName = "Lakona.Game.Actor";
    public const string ClusterActivitySourceName = "Lakona.Game.Cluster";

    public static IReadOnlyList<string> MeterNames { get; } = Array.AsReadOnly(
    [
        ActorMeterName,
        ClusterMeterName,
        ReliablePushMeterName,
        RpcServerMeterName,
        SessionMeterName,
        TimerMeterName
    ]);

    public static IReadOnlyList<string> ActivitySourceNames { get; } = Array.AsReadOnly(
    [
        ActorActivitySourceName,
        ClusterActivitySourceName
    ]);
}
