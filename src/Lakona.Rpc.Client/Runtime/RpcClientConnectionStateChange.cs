namespace Lakona.Rpc.Client
{
    /// <summary>A locally observed RPC connection transition.</summary>
    public readonly struct RpcClientConnectionStateChange
    {
        public RpcClientConnectionStateChange(RpcClientConnectionState previousState, RpcClientConnectionState currentState)
        {
            PreviousState = previousState;
            CurrentState = currentState;
        }

        public RpcClientConnectionState PreviousState { get; }
        public RpcClientConnectionState CurrentState { get; }
    }
}
