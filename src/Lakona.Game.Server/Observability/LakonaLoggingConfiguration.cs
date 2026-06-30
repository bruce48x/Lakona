using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability;

internal static class LakonaLoggingConfiguration
{
    public static void Apply(ILoggingBuilder logging, LakonaLoggingObservabilityOptions options)
    {
        logging.ClearProviders();

        if (!options.Enabled)
        {
            return;
        }

        logging.SetMinimumLevel(options.MinimumLevel);

        foreach (var category in options.Categories)
        {
            logging.AddFilter(category.Key, ParseCategoryLevel(category.Value));
        }

        if (!options.Console.Enabled)
        {
            return;
        }

        logging.AddSimpleConsole(console =>
        {
            console.SingleLine = options.Console.Format.Equals(
                "Compact",
                StringComparison.OrdinalIgnoreCase);
            console.IncludeScopes = options.Console.IncludeScopes;
            console.TimestampFormat = "HH:mm:ss ";
        });
    }

    private static LogLevel ParseCategoryLevel(string value)
    {
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;
    }
}
