using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix;

public sealed record HotfixFeatureCommandDescriptor(
    string Key,
    string FeatureName,
    FeatureCommandId CommandId,
    Type RequestType,
    Type ReplyType);
