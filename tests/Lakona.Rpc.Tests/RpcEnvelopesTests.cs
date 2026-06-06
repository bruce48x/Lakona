using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public class RpcEnvelopesTests
{
    [Fact]
    public void RpcRequestEnvelope_DefaultPayload_IsEmpty()
    {
        var req = new RpcRequestEnvelope();
        Assert.True(req.Payload.IsEmpty);
    }

    [Fact]
    public void RpcResponseEnvelope_DefaultPayload_IsEmpty()
    {
        var resp = new RpcResponseEnvelope();
        Assert.True(resp.Payload.IsEmpty);
    }

    [Fact]
    public void RpcResponseEnvelope_DefaultErrorMessage_IsNull()
    {
        var resp = new RpcResponseEnvelope();
        Assert.Null(resp.ErrorMessage);
    }

    [Fact]
    public void RpcPushEnvelope_DefaultPayload_IsEmpty()
    {
        var push = new RpcPushEnvelope();
        Assert.True(push.Payload.IsEmpty);
    }

    [Fact]
    public void RpcVoid_Instance_IsSingleton()
    {
        var a = RpcVoid.Instance;
        var b = RpcVoid.Instance;
        Assert.Same(a, b);
    }

    [Fact]
    public void RpcStatus_Values()
    {
        Assert.Equal(0, (byte)RpcStatus.Ok);
        Assert.Equal(1, (byte)RpcStatus.NotFound);
        Assert.Equal(2, (byte)RpcStatus.HandlerError);
        Assert.Equal(3, (byte)RpcStatus.Overloaded);
        Assert.Equal(4, (byte)RpcStatus.BadRequest);
        Assert.Equal(5, (byte)RpcStatus.ProtocolError);
    }

    [Fact]
    public void RpcFrameType_Values()
    {
        Assert.Equal(1, (byte)RpcFrameType.Request);
        Assert.Equal(2, (byte)RpcFrameType.Response);
        Assert.Equal(3, (byte)RpcFrameType.Push);
    }

    [Fact]
    public void RpcException_PreservesRemoteFailureDetails()
    {
        var ex = new RpcException(
            RpcStatus.NotFound,
            "No handler for 2:3",
            requestId: 7,
            serviceId: 2,
            methodId: 3);

        Assert.Equal(RpcStatus.NotFound, ex.Status);
        Assert.Equal("No handler for 2:3", ex.ErrorMessage);
        Assert.Equal(7u, ex.RequestId);
        Assert.Equal(2, ex.ServiceId);
        Assert.Equal(3, ex.MethodId);
        Assert.Contains("NotFound", ex.Message);
        Assert.Contains("2:3", ex.Message);
    }

    [Fact]
    public void RpcException_IsNotInvalidOperationException()
    {
        Exception ex = new RpcException(
            RpcStatus.HandlerError,
            errorMessage: null,
            requestId: 1,
            serviceId: 2,
            methodId: 3);

        Assert.False(ex is InvalidOperationException);
    }
}
