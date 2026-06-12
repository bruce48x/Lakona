namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixServiceAttribute : Attribute
{
    public HotfixServiceAttribute(Type contractType)
    {
        ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
    }

    public Type ContractType { get; }
}
