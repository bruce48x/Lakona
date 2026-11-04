using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ClientNotificationDtoTests
{
    [Fact]
    public void Cluster_codec_preserves_endpoint_profile_and_opaque_business_bytes()
    {
        var serializer = new MemoryPackRpcSerializer();
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
                SerializerProfile = "inventory-v2",
                Payload = [7, 8, 9]
            }
        });

        var decoded = serializer.Deserialize<ClientNotificationDispatchRequest>(frame.Memory);

        Assert.NotNull(decoded.Command);
        Assert.Equal("player-1", decoded.Command.OwnerKey);
        Assert.Equal(7, decoded.Command.ServiceId);
        Assert.Equal(11, decoded.Command.MethodId);
        Assert.Equal("inventory-v2", decoded.Command.SerializerProfile);
        Assert.Equal(new byte[] { 7, 8, 9 }, decoded.Command.Payload);
    }
}
