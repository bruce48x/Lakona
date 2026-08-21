namespace Lakona.Rpc.Transport.Kcp
{
    /// <summary>
    ///     The KCP listener explicitly rejected a transport connection attempt.
    /// </summary>
    public sealed class KcpConnectionRejectedException : Exception
    {
        internal KcpConnectionRejectedException(KcpHandshakeRejectionReason reason)
            : base(CreateMessage(reason))
        {
        }

        private static string CreateMessage(KcpHandshakeRejectionReason reason)
        {
            return reason == KcpHandshakeRejectionReason.ServerBusy
                ? "The KCP listener is busy and cannot accept another pending connection."
                : $"The KCP listener rejected the connection (reason {(int)reason}).";
        }
    }
}
