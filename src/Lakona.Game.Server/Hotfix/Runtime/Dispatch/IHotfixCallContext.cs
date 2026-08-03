namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Common context exposed to hotfix-dispatched calls.
/// </summary>
public interface IHotfixCallContext
{
    /// <summary>
    /// Gets the current hotfix service provider.
    /// </summary>
    IServiceProvider Services { get; }
}
