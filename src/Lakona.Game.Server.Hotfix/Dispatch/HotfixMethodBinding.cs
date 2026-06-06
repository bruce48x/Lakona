using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed record HotfixMethodBinding(
    HotfixMethodKey Key,
    MethodInfo Method,
    Type StateType,
    Type ReturnType,
    IReadOnlyList<Type> ParameterTypes);
