namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class HotfixActorContractAttribute : Attribute
{
    public HotfixActorContractAttribute(Type actorType)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
    }

    public Type ActorType { get; }
}
