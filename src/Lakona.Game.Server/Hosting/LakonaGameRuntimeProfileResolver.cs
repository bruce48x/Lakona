using Lakona.Game.Server.Guardrails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameRuntimeProfileResolver
{
    public static LakonaGameRuntimeProfile Resolve(IConfiguration configuration, string? environmentName)
    {
        var configuredProfile = configuration["Lakona:Profile"];
        if (!string.IsNullOrWhiteSpace(configuredProfile))
        {
            return ParseConfiguredProfile(configuredProfile);
        }

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return LakonaGameRuntimeProfile.Development;
        }

        if (string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return LakonaGameRuntimeProfile.Development;
        }

        if (string.Equals(environmentName, "Compose", StringComparison.OrdinalIgnoreCase))
        {
            return LakonaGameRuntimeProfile.Compose;
        }

        return LakonaGameRuntimeProfile.Production;
    }

    private static LakonaGameRuntimeProfile ParseConfiguredProfile(string value)
    {
        if (Enum.TryParse<LakonaGameRuntimeProfile>(value, ignoreCase: true, out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException(
            $"Lakona:Profile value '{value}' is invalid. Set Lakona:Profile to Development, Compose, or Production.");
    }
}
