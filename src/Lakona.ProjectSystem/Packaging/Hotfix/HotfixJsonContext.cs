using System.Text.Json.Serialization;

namespace Lakona.ProjectSystem.Packaging.Hotfix;

[JsonSerializable(typeof(HotfixPackageManifest))]
internal sealed partial class HotfixJsonContext : JsonSerializerContext;
