namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixSystemOfAttribute : Attribute
{
    public HotfixSystemOfAttribute(Type stateType)
    {
        StateType = stateType ?? throw new ArgumentNullException(nameof(stateType));
    }

    public Type StateType { get; }
}
