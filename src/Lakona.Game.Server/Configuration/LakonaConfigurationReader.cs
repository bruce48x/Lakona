using System.Globalization;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Lakona.Game.Server.Configuration;

internal static class LakonaConfigurationReader
{
    internal static bool ReadBool(IConfiguration section, string name, bool fallback)
    {
        var value = section[name];
        return value is null ? fallback : bool.TryParse(value, out var parsed)
            ? parsed : throw InvalidValue(section, name, "true or false");
    }

    internal static int ReadInt(IConfiguration section, string name, int fallback)
    {
        var value = section[name];
        return value is null ? fallback : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw InvalidValue(section, name, "a 32-bit integer");
    }

    internal static string ReadString(IConfiguration section, string name, string fallback)
    {
        var value = section[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    internal static TimeSpan ReadSeconds(IConfiguration section, string key, TimeSpan fallback)
    {
        return ReadNullableSeconds(section, key, fallback)!.Value;
    }

    internal static TimeSpan? ReadNullableSeconds(IConfiguration section, string key, TimeSpan? fallback)
    {
        var text = section[key];
        if (text is null) return fallback;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds))
            throw InvalidValue(section, key, "a finite duration in seconds");
        try
        {
            return TimeSpan.FromSeconds(seconds);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException($"{section.GetSection(key).Path} exceeds the supported duration range.", exception);
        }
    }

    internal static IReadOnlyList<string> ReadStringArray(IConfigurationSection section)
    {
        if (section.Value is { } json)
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(json)
                    ?? throw new JsonException("Expected an array.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"{section.Path} must be a valid JSON array when configured as a string value.", exception);
            }
        }

        return section.GetChildren().Select(static child => child.Value ?? "").ToArray();
    }

    private static InvalidOperationException InvalidValue(IConfiguration section, string name, string expected) =>
        new($"{section.GetSection(name).Path} must be {expected}.");

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
