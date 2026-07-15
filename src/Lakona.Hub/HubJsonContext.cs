using System.Text.Json.Serialization;
using Lakona.Hub.Updates;

namespace Lakona.Hub;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "ApplicationPaths")]
[JsonSerializable(typeof(HubReleaseManifest))]
[JsonSerializable(typeof(HubUpdateLaunchPlan))]
[JsonSerializable(typeof(HubPackageManifest))]
[JsonSerializable(typeof(HubDeltaManifest))]
internal sealed partial class HubJsonContext : JsonSerializerContext;
