using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.App.Services;

[HotfixRpcService(typeof(ILoginService), EndpointName = "control")]
internal static partial class LoginServiceEndpoint;

[HotfixRpcService(typeof(IChatService), EndpointName = "control")]
internal static partial class ChatServiceEndpoint;
