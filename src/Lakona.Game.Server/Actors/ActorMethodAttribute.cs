namespace Lakona.Game.Server.Actors;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ActorMethodAttribute : Attribute
{
    public ActorMethodAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}
