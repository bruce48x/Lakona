namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class FriendOfAttribute : Attribute
{
    public FriendOfAttribute(Type stateType)
    {
        StateType = stateType ?? throw new ArgumentNullException(nameof(stateType));
    }

    public Type StateType { get; }
}
