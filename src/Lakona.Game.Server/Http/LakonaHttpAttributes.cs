namespace Lakona.Game.Server.Http;

/// <summary>
/// Declares one Hotfix-owned Application HTTP service.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LakonaHttpServiceAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Declares the HTTP method and route for one Hotfix handler method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class LakonaHttpEndpointAttribute(
    string method,
    string routePattern) : Attribute
{
    public string Method { get; } = method;

    public string RoutePattern { get; } = routePattern;
}
