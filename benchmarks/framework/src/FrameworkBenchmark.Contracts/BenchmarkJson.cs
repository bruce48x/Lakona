using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameworkBenchmark.Contracts;

public static class BenchmarkJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Read<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options) ??
            throw new InvalidDataException($"JSON file '{path}' contains null instead of {typeof(T).Name}.");
    }

    public static void Write<T>(string path, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, value, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
