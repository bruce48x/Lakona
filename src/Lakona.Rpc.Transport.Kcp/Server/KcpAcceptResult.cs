using System.Net;

namespace Lakona.Rpc.Transport.Kcp
{
    public sealed class KcpAcceptResult
    {
        public KcpAcceptResult(KcpServerTransport transport, EndPoint remoteEndPoint, uint conversationId, int localPort)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            ConversationId = conversationId;
            LocalPort = localPort;
        }

        public uint ConversationId { get; }
        public int LocalPort { get; }
        public EndPoint RemoteEndPoint { get; }
        public KcpServerTransport Transport { get; }
    }
}
