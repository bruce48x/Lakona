namespace Lakona.Game.Server.Http;

/// <summary>
/// Declares a stable Application HTTP service contract.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class LakonaHttpServiceAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Declares the stable method id, HTTP method, and route for one contract method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class LakonaHttpEndpointAttribute(
    int methodId,
    string method,
    string routePattern) : Attribute
{
    public int MethodId { get; } = methodId;

    public string Method { get; } = method;

    public string RoutePattern { get; } = routePattern;
}
