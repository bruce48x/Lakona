namespace Lakona.Game.Server.Hotfix.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixRpcServiceAttribute : Attribute
{
    public HotfixRpcServiceAttribute(Type contractType)
    {
        ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
    }

    public Type ContractType { get; }

    public string EndpointName { get; set; } = "default";

    public string BindingSetName { get; set; } = "default";
}
