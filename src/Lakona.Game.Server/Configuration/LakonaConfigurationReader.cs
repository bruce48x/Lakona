using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Configuration;

internal static class LakonaConfigurationReader
{
    internal static bool ReadBool(IConfiguration section, string name, bool fallback)
    {
        return bool.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static int ReadInt(IConfiguration section, string name, int fallback)
    {
        return int.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static int ReadInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    internal static long ReadLong(IConfiguration section, string name, long fallback)
    {
        return long.TryParse(section[name], out var parsed) ? parsed : fallback;
    }

    internal static double ReadDouble(IConfiguration section, string name, double fallback)
    {
        return double.TryParse(
            section[name],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;
    }

    internal static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    internal static TimeSpan ReadSeconds(IConfiguration section, string key, TimeSpan fallback)
    {
        return double.TryParse(section[key], out var value) ? TimeSpan.FromSeconds(value) : fallback;
    }

    internal static TimeSpan? ReadNullableSeconds(IConfiguration section, string key, TimeSpan? fallback)
    {
        return double.TryParse(section[key], out var value) ? TimeSpan.FromSeconds(value) : fallback;
    }

    internal static LogLevel ReadLogLevel(IConfiguration section, string name, LogLevel fallback)
    {
        var value = section[name];
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }

    internal static IReadOnlyDictionary<string, string> ReadDictionary(
        IConfigurationSection section,
        IReadOnlyDictionary<string, string> fallback)
    {
        var children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            return fallback;
        }

        return children.ToDictionary(child => child.Key, child => child.Value ?? "");
    }
}
