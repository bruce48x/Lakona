using System.Reflection;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed record HotfixHttpEndpointMethodBinding(
    HotfixHttpEndpointDescriptor Endpoint,
    MethodInfo Method,
    Type ServiceType,
    Type ArgumentType,
    Type ResultType);
