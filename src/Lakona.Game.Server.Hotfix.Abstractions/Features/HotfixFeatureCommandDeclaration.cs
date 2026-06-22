namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixFeatureCommandDeclaration(
    Type RequestType,
    Type ReplyType,
    int CommandId,
    string MethodName);
