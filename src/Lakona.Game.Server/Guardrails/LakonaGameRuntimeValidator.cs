using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails.Rules;

namespace Lakona.Game.Server.Guardrails;

/// <summary>Validates the framework's effective configuration using its built-in checks.</summary>
public sealed class LakonaGameRuntimeValidator
{
    public LakonaGameValidationResult Validate(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions? clusterOptions = null,
        string? hotfixAssemblyPath = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        hotfixAssemblyPath ??= Path.Combine(AppContext.BaseDirectory, "hotfix", "Server.Hotfix.dll");
        return new LakonaGameValidationResult(
        [
            .. NodeIdentityRule.Validate(clusterOptions?.NodeId ?? runtime.Node.Id),
            .. EndpointRule.Validate(runtime),
            .. ClusterEndpointRule.Validate(runtime),
            .. HotfixSourceRule.Validate(hotfixAssemblyPath),
            .. HeartbeatRule.Validate(runtime),
            .. NodeRoleConfigurationRule.Validate(runtime),
            .. ManagementAdminRule.Validate(runtime)
        ]);
    }
}
