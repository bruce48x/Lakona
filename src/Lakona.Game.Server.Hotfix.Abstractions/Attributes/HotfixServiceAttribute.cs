namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Binds a hotfix service implementation to a shared RPC service contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixServiceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new hotfix service attribute.
    /// </summary>
    /// <param name="contractType">The shared RPC service contract implemented by the hotfix service.</param>
    public HotfixServiceAttribute(Type contractType)
    {
        ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
    }

    /// <summary>
    /// Gets the shared RPC service contract type.
    /// </summary>
    public Type ContractType { get; }
}
