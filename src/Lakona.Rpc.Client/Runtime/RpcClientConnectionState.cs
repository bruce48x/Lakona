namespace Lakona.Rpc.Client
{
    /// <summary>The locally observed lifecycle of one RPC client connection.</summary>
    public enum RpcClientConnectionState
    {
        /// <summary>The runtime has not started connecting.</summary>
        Created,
        /// <summary>Transport initialization is in progress.</summary>
        Connecting,
        /// <summary>Transport initialization completed and RPC loops started; remote liveness is not guaranteed.</summary>
        Connected,
        /// <summary>Startup failed or the connection stopped; queued work and resources may still be draining.</summary>
        Stopped,
        /// <summary>Disposal has started. Await DisposeAsync to observe cleanup completion.</summary>
        Disposed
    }
}
