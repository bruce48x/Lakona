namespace Lakona.Game.Server.Hosting;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LakonaRpcServiceAttribute : Attribute
{
    public LakonaRpcServiceAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("RPC service name is required.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}
