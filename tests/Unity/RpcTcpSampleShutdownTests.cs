using System.Buffers.Binary;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Lakona.Rpc.Core;
using NUnit.Framework;
using Rpc.Testing;
using UnityEngine;
using UnityEngine.TestTools;
using NUnitAssert = NUnit.Framework.Assert;

namespace Lakona.SampleTests
{
    public sealed partial class TesterShutdownTests
    {
        [UnityTest]
        public IEnumerator Disable_during_tcp_login_closes_the_socket_without_a_reply()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var owner = new GameObject("TCP shutdown test");
            owner.SetActive(false);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var tester = owner.AddComponent<RpcConnectionTester>();
                JsonUtility.FromJsonOverwrite("{\"_endpoint\":{\"Host\":\"127.0.0.1\",\"Port\":" + port + "}}", tester);
                tester.AutoConnect = false;
                tester.ConnectionCount = 1;
                owner.SetActive(true);

                var accepting = listener.AcceptTcpClientAsync();
                var startup = tester.ConnectAndTestAsync();
                yield return WaitUntil(() => accepting.IsCompleted, "The configured tester did not connect to the TCP listener.");
                using var peer = accepting.GetAwaiter().GetResult();
                var stream = peer.GetStream();
                var login = ReadLoginAsync(stream);
                yield return WaitUntil(() => login.IsCompleted, "Did not receive the login request.");
                login.GetAwaiter().GetResult();

                var lateUpdates = 0;
                tester.StatusChanged += (_, __) => lateUpdates++;
                var closed = ReadPeerCloseAsync(stream); // The server deliberately keeps its socket open and sends no reply.
                tester.enabled = false;
                yield return WaitUntil(() => closed.IsCompleted && startup.IsCompleted,
                    "The tester did not close its TCP socket while the login reply was withheld.");
                NUnitAssert.That(closed.GetAwaiter().GetResult(), Is.True, "Received more bytes after login.");
                startup.GetAwaiter().GetResult();
                yield return null;
                NUnitAssert.That(lateUpdates, Is.Zero);
            }
            finally
            {
                listener.Stop();
                Object.Destroy(owner);
            }
        }

        private static async Task ReadLoginAsync(NetworkStream stream)
        {
            var header = await ReadBytesAsync(stream, 4);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            NUnitAssert.That(length, Is.InRange(1, LengthPrefix.DefaultMaxFrameSize));
            var payload = await ReadBytesAsync(stream, length);
            using var frame = TransportFrame.CopyOf(payload);
            using var request = RpcEnvelopeCodec.DecodeRequest(frame);
            NUnitAssert.That((request.ServiceId, request.MethodId), Is.EqualTo((1, 1)), "Expected Player.LoginAsync.");
        }

        private static async Task<byte[]> ReadBytesAsync(NetworkStream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var n = await stream.ReadAsync(bytes, offset, count - offset);
                if (n == 0) throw new EndOfStreamException("Client closed before sending the login frame.");
                offset += n;
            }
            return bytes;
        }

        private static async Task<bool> ReadPeerCloseAsync(NetworkStream stream)
        {
            try { return await stream.ReadAsync(new byte[1], 0, 1) == 0; }
            catch (IOException ex) when (ex.InnerException is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionReset)
            {
                return true;
            }
        }
    }
}
