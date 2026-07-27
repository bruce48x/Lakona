using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed record HotfixServiceMethodBinding(
    string Key,
    int MethodId,
    MethodInfo Method,
    Type ServiceType,
    Type ContractType,
    Type ReturnType,
    IReadOnlyList<Type> ParameterTypes);
