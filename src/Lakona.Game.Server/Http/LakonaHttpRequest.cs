using System.Net;

namespace Lakona.Game.Server.Http;

/// <summary>
/// Bounded HTTP request snapshot detached from the ASP.NET request context.
/// </summary>
public sealed record LakonaHttpRequest(
    ReadOnlyMemory<byte> RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    IReadOnlyDictionary<string, string[]> Query,
    IReadOnlyDictionary<string, string> RouteValues,
    string? AuthenticatedName,
    IPEndPoint? RemoteEndpoint,
    string TraceIdentifier);
