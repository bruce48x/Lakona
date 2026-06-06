namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Central defaults for RPC protocol payload, transport frame, and security transform limits.
    /// </summary>
    public static class RpcProtocolLimits
    {
        public const int DefaultMaxPayloadSize = 64 * 1024 * 1024;
        public const int DefaultMaxTransportFrameSize = DefaultMaxPayloadSize;
        public const int DefaultMaxDecompressedFrameBytes = DefaultMaxPayloadSize + 1024;
    }
}
