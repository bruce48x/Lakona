namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Reports the result of a server-to-client notification attempt.
/// </summary>
public enum ClientNotificationStatus
{
    /// <summary>
    /// The notification was accepted for delivery to the target session.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The framework could not find a route for the target session.
    /// </summary>
    RouteNotFound = 1,

    /// <summary>
    /// The target session exists, but the requested callback contract is not currently bound.
    /// </summary>
    CallbackUnavailable = 2,

    /// <summary>
    /// Delivery failed after the route and callback were resolved.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The framework's bounded queue for the target session is full.
    /// </summary>
    Backpressure = 4
}
