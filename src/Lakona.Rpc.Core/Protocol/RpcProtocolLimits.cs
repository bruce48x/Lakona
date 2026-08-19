namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Central defaults for decoded RPC envelopes and their derived transport framing budgets.
    /// </summary>
    public static class RpcProtocolLimits
    {
        public const int DefaultMaxEnvelopeSize = 64 * 1024 * 1024;

        // AES-CBC framing adds one flags byte, up to one full block of PKCS#7
        // padding, a 16-byte IV, and a 32-byte HMAC.
        public const int MaximumSecurityTransformOverhead = 1 + 16 + 16 + 32;

        public const int DefaultMaxTransportFrameSize =
            DefaultMaxEnvelopeSize + MaximumSecurityTransformOverhead;

        public const int DefaultMaxLengthPrefixedFrameSize =
            sizeof(uint) + DefaultMaxTransportFrameSize;
    }
}
