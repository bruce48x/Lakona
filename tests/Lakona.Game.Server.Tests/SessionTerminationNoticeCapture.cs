using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Client;

namespace Lakona.Game.Server.Tests;

internal static class SessionTerminationNoticeCapture
{
    public static Task<SessionTerminationNotice> Register(RpcClientRuntime client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var received = new TaskCompletionSource<SessionTerminationNotice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RegisterRawNotificationHandler(
            GameSessionNotificationRpcIds.ServiceId,
            GameSessionNotificationRpcIds.TerminatedNotificationId,
            payload =>
            {
                received.TrySetResult(
                    LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                return default;
            });
        return received.Task;
    }
}
