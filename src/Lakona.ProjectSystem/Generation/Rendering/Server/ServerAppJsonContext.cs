using System.Text.Json.Serialization;

namespace Lakona.ProjectSystem.Generation.Rendering.Server;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, object?>), TypeInfoPropertyName = "AppSettings")]
[JsonSerializable(typeof(Dictionary<string, object?>[]), TypeInfoPropertyName = "AppSettingsArray")]
[JsonSerializable(typeof(string[]), TypeInfoPropertyName = "StringArray")]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
internal sealed partial class ServerAppJsonContext : JsonSerializerContext;
