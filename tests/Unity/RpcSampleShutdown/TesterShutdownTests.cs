using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using NUnitAssert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Lakona.SampleTests
{
    public sealed partial class TesterShutdownTests
    {
        [UnityTest]
        public IEnumerator Disable_during_login_releases_the_connection() => StopWhileWaiting(1, disableFirst: true);

        [UnityTest]
        public IEnumerator Destroy_during_poll_releases_the_connection() => StopWhileWaiting(2, disableFirst: false);

        [UnityTest]
        public IEnumerator Shutdown_during_connect_disposes_after_connect_returns()
        {
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var peer = new HoldingPeer(0, connected.Task);
            var owner = new GameObject("RPC connecting shutdown test");
            owner.SetActive(false);
            var tester = owner.AddComponent<ShutdownProbe>();
            tester.AutoConnect = false;
            tester.Options = () => new RpcClientOptions(peer, new EmptyDtoSerializer());
            try
            {
                owner.SetActive(true);
                var startup = tester.ConnectAndTestAsync();
                NUnitAssert.That(peer.ConnectToken.CanBeCanceled, Is.True);
                tester.enabled = false;
                Object.Destroy(owner);
                yield return null;
                NUnitAssert.That(peer.ConnectToken.IsCancellationRequested, Is.True);
                connected.SetResult(true); // The transport reports success even though shutdown requested cancellation.
                yield return WaitUntil(() => startup.IsCompleted && peer.Disposals > 0, "Connecting session did not finish cleanup.");
                startup.GetAwaiter().GetResult();
                NUnitAssert.That(peer.DisposedDuringConnect, Is.False, "The RPC client disposed the transport during ConnectAsync.");
                NUnitAssert.That(peer.Disposals, Is.EqualTo(1));
                NUnitAssert.That(peer.Requests, Is.Zero, "Login started after shutdown.");
            }
            finally
            {
                connected.TrySetResult(true);
                if (owner != null) Object.Destroy(owner);
                peer.ReleaseForCleanup();
            }
        }

        private static IEnumerator StopWhileWaiting(int heldPlayerMethod, bool disableFirst)
        {
            var peers = new List<HoldingPeer>();
            var owner = new GameObject("RPC shutdown test");
            owner.SetActive(false);
            var tester = owner.AddComponent<ShutdownProbe>();
            tester.AutoConnect = false;
            tester.RequestIntervalSeconds = 0.1f;
            tester.Options = () =>
            {
                var peer = new HoldingPeer(heldPlayerMethod);
                peers.Add(peer);
                return new RpcClientOptions(peer, new EmptyDtoSerializer());
            };

            var lateMessages = new List<string>();
            void RecordLateLog(string message, string trace, LogType type)
            {
                if (message.StartsWith("Session[") || message.StartsWith("State =>") ||
                    message.StartsWith("[KCP]") || message.StartsWith("RPC "))
                    lateMessages.Add(message);
            }

            try
            {
                owner.SetActive(true);
                var startup = tester.ConnectAndTestAsync();
                yield return WaitUntil(() => peers.Count > 0 && peers.All(peer => peer.Holding) &&
                    (heldPlayerMethod == 1 || startup.IsCompleted), "Peer never received the target request.");
                var connections = peers.Count;
                var requests = peers.Sum(peer => peer.Requests);
                Application.logMessageReceived += RecordLateLog;
                if (disableFirst)
                    tester.enabled = false;
                else
                    Object.Destroy(owner);

                yield return WaitUntil(() => startup.IsCompleted && peers.All(peer => peer.Disposals == 1),
                    "Shutdown is still waiting for a reply instead of releasing the connection.");
                startup.GetAwaiter().GetResult();
                if (disableFirst) Object.Destroy(owner); // Destroy after disable must not clean up a second time.
                yield return null; // Drain the Unity-context continuations from cancellation and disposal.
                NUnitAssert.That(peers.Count, Is.EqualTo(connections), "Startup opened another connection after shutdown.");
                NUnitAssert.That(peers.Sum(peer => peer.Requests), Is.EqualTo(requests));
                NUnitAssert.That(peers.All(peer => peer.RequestTokens.All(token => token.IsCancellationRequested)), Is.True,
                    "A generated RPC call did not receive the session's cancellation.");
                NUnitAssert.That(peers.All(peer => peer.Disposals == 1), Is.True);
                NUnitAssert.That(lateMessages, Is.Empty);
            }
            finally
            {
                Application.logMessageReceived -= RecordLateLog;
                if (owner != null) Object.Destroy(owner);
                foreach (var peer in peers) peer.ReleaseForCleanup();
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            NUnitAssert.That(condition(), Is.True, failure);
        }

        // A live peer that answers setup requests, then accepts one request without ever replying to it.
        private sealed class HoldingPeer : ITransport
        {
            private readonly int heldPlayerMethod;
            private readonly Task pendingConnect;
            private readonly SemaphoreSlim available = new(0);
            private readonly ConcurrentQueue<TransportFrame> responses = new();
            public readonly ConcurrentQueue<CancellationToken> RequestTokens = new();
            private int playerPolls;
            private int disposals;
            private int holding;

            public HoldingPeer(int heldPlayerMethod, Task pendingConnect = null)
            {
                this.heldPlayerMethod = heldPlayerMethod;
                this.pendingConnect = pendingConnect ?? Task.CompletedTask;
            }
            public int Requests => RequestTokens.Count;
            public int Disposals => Volatile.Read(ref disposals);
            public bool Holding => Volatile.Read(ref holding) != 0;
            public bool IsConnected => Disposals == 0;
            public CancellationToken ConnectToken { get; private set; }
            public bool DisposedDuringConnect { get; private set; }
            public ValueTask ConnectAsync(CancellationToken ct = default)
            {
                ConnectToken = ct;
                return new ValueTask(pendingConnect);
            }

            public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
            {
                if (RpcEnvelopeCodec.PeekFrameType(frame.Span) != RpcFrameType.Request) return default;
                using var copy = TransportFrame.CopyOf(frame.Span);
                using var request = RpcEnvelopeCodec.DecodeRequest(copy);
                RequestTokens.Enqueue(ct);
                // Let the first complete polling round exercise every module before holding the next player poll.
                if (request.ServiceId == 1 && request.MethodId == heldPlayerMethod &&
                    (heldPlayerMethod == 1 || Interlocked.Increment(ref playerPolls) == 2))
                {
                    Volatile.Write(ref holding, 1);
                }
                else
                {
                    responses.Enqueue(RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, new byte[1]));
                    available.Release();
                }
                return default;
            }

            public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
            {
                await available.WaitAsync(ct);
                return responses.TryDequeue(out var frame) ? frame : TransportFrame.Empty;
            }

            public ValueTask DisposeAsync()
            {
                if (!pendingConnect.IsCompleted) DisposedDuringConnect = true;
                Interlocked.Increment(ref disposals);
                ReleaseForCleanup();
                return default;
            }

            public void ReleaseForCleanup() => available.Release();
        }

        private sealed class EmptyDtoSerializer : IRpcSerializer
        {
            public void Serialize<T>(IBufferWriter<byte> destination, T value) { }
            public T Deserialize<T>(ReadOnlyMemory<byte> data) => Activator.CreateInstance<T>();
            public T Deserialize<T>(ReadOnlySpan<byte> data) => Activator.CreateInstance<T>();
        }
    }
}
