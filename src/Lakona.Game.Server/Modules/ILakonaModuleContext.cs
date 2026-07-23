using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Modules;

/// <summary>
/// Provides the final stable host context to an application module during startup.
/// </summary>
public interface ILakonaModuleContext
{
    /// <summary>
    /// Gets the final application configuration.
    /// </summary>
    IConfiguration Configuration { get; }

    /// <summary>
    /// Gets the single final root dependency-injection provider.
    /// </summary>
    IServiceProvider Services { get; }
}
