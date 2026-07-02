namespace Lakona.Game.Server.Guardrails;

/// <summary>
/// Selects framework defaults and guardrails for a server runtime environment.
/// </summary>
public enum LakonaGameRuntimeProfile
{
    /// <summary>
    /// Local development defaults, including developer diagnostics where safe.
    /// </summary>
    Development,

    /// <summary>
    /// Local multi-process or container-compose defaults.
    /// </summary>
    Compose,

    /// <summary>
    /// Production defaults with stricter diagnostics and exposure guardrails.
    /// </summary>
    Production
}
