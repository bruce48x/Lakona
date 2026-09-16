namespace Lakona.Game.Server.Hotfix.Dispatch;

/// <summary>A lifecycle interface and its generation-owned implementation type.</summary>
public sealed record HotfixLifecycleBinding(Type ContractType, Type ImplementationType);
