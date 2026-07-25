using System.Net;

namespace Lakona.Game.Server.Http;

/// <summary>
/// Immutable, bounded HTTP request snapshot defined by the stable host.
/// </summary>
public sealed record LakonaHttpRequest(
    ReadOnlyMemory<byte> RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    IReadOnlyDictionary<string, string[]> Query,
    IReadOnlyDictionary<string, string> RouteValues,
    string? AuthenticatedName,
    IPEndPoint? RemoteEndpoint,
    string TraceIdentifier);
