namespace Lakona.Game.Server.Actors;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActorNameAttribute : Attribute
{
    public ActorNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}
