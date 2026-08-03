namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixHttpEndpointDescriptor(
    string Service,
    string Method,
    string RoutePattern);
