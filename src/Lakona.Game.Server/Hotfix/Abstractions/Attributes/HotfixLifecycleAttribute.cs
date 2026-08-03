namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Binds a hotfix lifecycle implementation to a lifecycle contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixLifecycleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new lifecycle binding attribute.
    /// </summary>
    /// <param name="contractType">The lifecycle contract type implemented by the hotfix class.</param>
    public HotfixLifecycleAttribute(Type contractType)
    {
        ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
    }

    /// <summary>
    /// Gets the lifecycle contract type implemented by the hotfix class.
    /// </summary>
    public Type ContractType { get; }
}
