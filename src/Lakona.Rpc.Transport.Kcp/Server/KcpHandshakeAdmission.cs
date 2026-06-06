using System.Net;

namespace Lakona.Rpc.Transport.Kcp;

public delegate ValueTask<bool> KcpHandshakeAdmission(uint conversationId, IPEndPoint remoteEndPoint, CancellationToken ct);
