using System.Text.Json.Serialization;
using Lakona.Hub.Applications;
using Lakona.Hub.Updates;

namespace Lakona.Hub;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "ApplicationPaths")]
[JsonSerializable(typeof(StoredManualApplicationSettings))]
[JsonSerializable(typeof(HubReleaseManifest))]
internal sealed partial class HubJsonContext : JsonSerializerContext;
