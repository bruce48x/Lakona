namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixBehaviorOfAttribute : Attribute
{
    public HotfixBehaviorOfAttribute(Type actorType)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
    }

    public Type ActorType { get; }
}
