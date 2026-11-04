namespace Lakona.Rpc.Server;

/// <summary>Maps an unhandled service method exception to its declared response, or returns null to decline.</summary>
public delegate object? RpcServiceExceptionHandler(RpcServiceExceptionContext context);

/// <summary>Identifies a failed typed RPC call and its declared response type.</summary>
public sealed record RpcServiceExceptionContext(
    Exception Exception,
    string? ServiceName,
    string? MethodName,
    uint RequestId,
    Type ResponseType);

/// <summary>A framework lease held through typed service invocation and exception mapping.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IRpcServiceInvocationScope : IDisposable
{
    RpcServiceExceptionHandler? ExceptionHandler { get; }
}
