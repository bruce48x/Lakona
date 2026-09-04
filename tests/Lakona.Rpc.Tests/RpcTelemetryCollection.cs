using Xunit;

namespace Lakona.Rpc.Tests;

internal static class RpcTelemetryCollectionNames
{
    public const string Diagnostics = "RPC telemetry";
}

[CollectionDefinition(RpcTelemetryCollectionNames.Diagnostics, DisableParallelization = true)]
public sealed class RpcTelemetryCollection;
