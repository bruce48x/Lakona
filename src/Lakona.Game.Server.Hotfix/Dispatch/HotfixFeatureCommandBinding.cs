using System.Reflection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal sealed record HotfixFeatureCommandBinding(
    string Key,
    string FeatureName,
    FeatureCommandId CommandId,
    Type FeatureType,
    Type RequestType,
    Type ReplyType,
    MethodInfo Method);
