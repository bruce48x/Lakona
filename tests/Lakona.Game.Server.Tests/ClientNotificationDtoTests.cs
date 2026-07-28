using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Serializer.Json;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationDtoTests
{
    [Fact]
    public void Json_roundtrips_client_notification_dispatch_request()
    {
        var serializer = new JsonRpcSerializer();
        using var frame = serializer.SerializeFrame(new ClientNotificationDispatchRequest
        {
            Command = new ClientNotificationCommand
            {
                OwnerKey = "player-1",
                SessionId = "session-1",
                CallbackContractType = "Game.ILoginCallback",
                MethodName = "OnMatchedAsync",
                ServiceId = 7,
                MethodId = 11,
                Payload = [7, 8, 9]
            }
        });

        var decoded = serializer.Deserialize<ClientNotificationDispatchRequest>(frame.Memory);

        Assert.NotNull(decoded.Command);
        Assert.Equal("player-1", decoded.Command.OwnerKey);
        Assert.Equal(7, decoded.Command.ServiceId);
        Assert.Equal(11, decoded.Command.MethodId);
        Assert.Equal(new byte[] { 7, 8, 9 }, decoded.Command.Payload);
    }
}
