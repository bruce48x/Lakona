using System.Text.Json.Serialization;

namespace Lakona.ProjectSystem.Packaging.Server;

[JsonSerializable(typeof(ServerPackageManifest))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
