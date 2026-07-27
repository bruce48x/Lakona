using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

/// <summary>
/// Identifies a typed request context accepted by a hotfix service method.
/// </summary>
/// <typeparam name="TRequest">The shared RPC request type.</typeparam>
public interface IHotfixServiceCall<out TRequest> : IHotfixCallContext
{
    /// <summary>
    /// Gets the shared RPC request for the current dispatch.
    /// </summary>
    TRequest Request { get; }
}
