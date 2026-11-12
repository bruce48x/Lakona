# RPC Transport Contract

`ITransport` is the stable extension boundary for one connection carrying complete
frames. This document defines caller and implementation responsibilities. It does
not require transport implementations to share a base class or exception type.

## Responsibility Boundary

| Operation | Caller / RPC runtime responsibility | Transport responsibility |
| --- | --- | --- |
| Initialization | Await `ConnectAsync` before starting frame I/O; serialize initialization attempts | Establish an outbound connection or initialize an accepted connection; repeated initialization of an already initialized live connection is harmless |
| Concurrency | At most one send and one receive at a time per instance | Support one send concurrently with one receive; concurrent sends or concurrent receives are not required |
| Sending | Keep the input memory valid until the returned operation completes, including failure or cancellation | Borrow the memory only for that operation; retain any asynchronously needed bytes in transport-owned storage |
| Receiving | Dispose each returned `TransportFrame` after use | Return a complete owned frame; keep framing and partial input internal |
| Cancellation | Pass a lifetime token to operations that can wait; observe their completion | End pending waits when cancellation is requested; clean up resources acquired by failed initialization |
| Normal shutdown | Stop admitting work, cancel and join outstanding operations, then dispose the owned transport | Release owned resources; sequential repeated disposal is safe |
| Abort | A server host may dispose a transport to break stalled I/O during bounded shutdown | Cause pending I/O to terminate without retaining resources indefinitely; callers must still observe the pending tasks |
| Reconnection | Create a new transport for a new connection | No common guarantee of reuse after failed initialization, terminal I/O failure, or disposal |

`RpcConnectionChannel` serializes writes from requests, responses, notifications,
and keepalive. If cancellation wins before the writer claims a queued frame,
the sender removes that entry immediately and completes its send as canceled.
The remaining frames keep their FIFO order even while an earlier write stalls.
The shared sender admits at most 128 MiB and 65,536 frames per connection,
including the active write. Exhaustion fails the new send with
`InvalidOperationException` before enqueueing; it does not move retained buffers
into an unbounded waiting list. Completion, queued cancellation, and disposal
release their reservations.
Client and Session receive loops supply the single reader. A
custom caller bypassing those owners must provide the same serialization.
Repeated initialization is not permission to race two `ConnectAsync` calls or to
race initialization with disposal. Normal client disposal cancels and joins its
connection attempt before releasing the transport. Concurrent disposal callers
must coordinate at their owner; the shared disposal completion in RPC runtime
does not imply that every transport implements that policy independently.

The client also cancels and joins admitted request sends before publishing
`Disconnected` or disposing its transport. This includes default-token calls
and queued writes. A canceled active write retains the caller's frame until the
transport operation actually completes; cancellation alone does not release it.

A throwing cancellation callback cannot skip client cleanup. The client records
the cancellation failure, joins startup and active I/O, and attempts every owned
resource's disposal before completing its shared `DisposeAsync` task. Cancellation
and cleanup failures are reported together; repeated disposal observes the same
result. An earlier terminal I/O error remains the `Disconnected` reason.

The host's abort deadline is a containment mechanism, not a guarantee that an
arbitrary third-party implementation cooperates. See
[Host And Session Lifetime](architecture.md#host-and-session-lifetime).

## Completion, Cancellation, And Connection State

A successful send means the implementation has accepted the complete frame and
no longer borrows the caller's memory. It does not prove peer receipt or RPC
execution; transports may buffer data or retransmit it after the send completes.

Cancellation is cooperative and may race completion. A synchronous operation or
an already buffered receive can finish before observing cancellation. Cancellation
must release a pending wait, but it does not prove that no bytes were sent, roll
back a partially transmitted frame, or guarantee that the connection is reusable.
Do not retry a business operation merely because its transport operation was
canceled. RPC request outcomes and business retry policy belong above transport.

`IsConnected` reports locally known connection state, not verified peer liveness
or a promise that the next I/O succeeds. Accepted transports can already report
connected before initialization. Remote closure can be reported as an empty frame
or an I/O/connection exception; callers must handle both. Exact exception types,
and their ordering relative to cancellation or disposal, are transport-specific.
Do not send empty application frames: an empty received frame is a terminal
signal at the RPC boundary.

Transport-specific behavior remains relevant:

| Implementation | Initialization and termination behavior |
| --- | --- |
| TCP | Client connects; accepted server initializes framing. Orderly remote EOF currently throws `IOException`. `TcpClient.Connected` is only locally known state. Do not rely on the client's ability to allocate another socket after disposal. |
| WebSocket | Client connects; accepted server uses the accepted socket. Remote close currently throws `IOException`. Disposal attempts a close handshake with a one-second timeout, then aborts if needed. Cancellation may abort the socket. |
| KCP | Client performs the bounded bootstrap; accepted server initialization may already have occurred in the listener. UDP supplies no peer-close signal, so silent peer loss needs RPC keepalive. Sending queues owned chunks into KCP, asynchronously waiting for bounded capacity when necessary. |
| Loopback | Initialization activates an endpoint of one shared pair. Disposing either endpoint closes both directions and wakes waiting readers/writers. A closed pair cannot become a new connection. |

## Official Transport Behavior

### KCP

`KcpTransportOptions` is the shared client/listener/acceptor configuration API.
Existing constructors remain supported. Options are validated and copied at
construction, before socket startup; changing the original object does not
change a connection or future sessions accepted by that listener. Peers configure
their own endpoints independently; settings are not negotiated on the wire.

| Option | Default | Accepted values |
| --- | --- | --- |
| `UpdateIntervalMilliseconds` | 20 | 10–5000 milliseconds |
| `SendWindowSize` | 128 | 1–512 segments |
| `ReceiveWindowSize` | 128 | 128–65535 segments |
| `NoDelay` | false | Enables shorter retransmission timeouts when true |
| `FastResend` | 0 | Nonnegative ACK-skip threshold; 0 disables fast retransmission |
| `CongestionControl` | true | Disabling congestion control is an explicit deployment choice |

```csharp
var options = new KcpTransportOptions { UpdateIntervalMilliseconds = 20, SendWindowSize = 128 };
var client = new KcpTransport("127.0.0.1", 20000, options);
var acceptor = new KcpConnectionAcceptor(20000, "0.0.0.0",
    RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections, options);
```

The previous library defaults can be selected with interval 100 and send window
32; the other defaults are unchanged. MTU remains 1400, message mode remains
enabled, and the 512-segment pending budget remains fixed. The receive-window
minimum exceeds the fragments in a 64 KiB chunk; exposing MTU or smaller receive
windows independently could prevent a complete KCP message from being received.
Larger receive windows permit more per-connection buffering. These settings do
not change RPC frame limits or request/heartbeat deadlines.

KCP remains in message mode. Each RPC frame has one length prefix and is written
as ordered KCP messages of at most 64 KiB. Existing frame accumulation reconstructs
the original frame without a new wire protocol. Only the first chunk is packed
with the length prefix; subsequent chunks are submitted from the original frame.
Small frames still use one KCP Send. KCP retains its own reliable segment copies;
this is not end-to-end zero-copy transmission. Every KCP Send return value must
match the submitted chunk length. The normal 64 MiB decoded RPC envelope limit
still applies, with the existing transport-security overhead allowance.

One connection admits at most 512 queued or unacknowledged KCP segments (about
688 KiB of segment payload at the fixed default MTU). A full budget suspends the
single writer until datagram input advances acknowledgements. No unbounded queue
of waiting transport writes is created. Cancellation before any chunk is accepted
preserves the connection; failure or cancellation after partial submission makes
it terminal. Disposal and protocol failure wake both pending readers and writers.
A completed send means full transport acceptance, not peer receipt.

Capacity and receive-data notifications coalesce to one pending semaphore
signal. Producers check and release under the connection's KCP lock; consumers
only remove a signal and recheck protocol or terminal state. Ordinary repeated
notifications must not allocate `SemaphoreFullException` as control flow.
Cancellation and shutdown must still wake pending work.

Large frames remain ordered on the same connection and can delay later RPCs,
notifications and heartbeats. RPC keepalive observes complete inbound frames;
KCP ACK progress is not application-level liveness. Configure request/heartbeat
deadlines for the supported payload sizes and link speeds. Chunking does not
introduce frame multiplexing or guarantee a particular transfer rate.

The client owns a continuous UDP receive loop so ACKs progress independently of
application reads, including during a send-only upload. Both client and server
leave application bytes in KCP's bounded receive window until ReceiveFrameAsync
drains them into the length-prefixed accumulator. Application receive cancellation
does not stop the UDP loop. Client disposal closes the socket, joins that loop,
and only then releases KCP state.

The KCP server listener accepts UDP datagrams up to 2,048 bytes. It discards
larger datagrams in full and continues receiving traffic for all connections.
This limit applies to each datagram before handshake or KCP packet processing.

The KCP server listener shares one UDP receive loop across connections, but it
must not eagerly drain decoded KCP messages into a separate application frame
queue. Datagram input remains in KCP's bounded per-connection receive window
until that connection's `ReceiveFrameAsync` caller requests the next frame. A
slow RPC Session therefore closes its advertised KCP receive window without
blocking the shared listener, retaining an unbounded number of decoded frames,
or delaying unrelated connections. The listener also does not invoke arbitrary
application admission while receiving datagrams, so new handshakes cannot hold
up traffic for established connections.

KCP background faults are terminal at their smallest owner. An unexpected
listener receive-loop failure closes the listener's accept boundary with the
original cause so endpoint supervision can stop cleanly. A scheduled update
failure removes only that connection's registration, transitions its transport
to disconnected, and wakes pending receive work with the original cause.
Schedulers do not retry or log transport failures; RPC Session and host owners
provide the single diagnostic boundary.

KCP update scheduling follows the protocol's `Check` deadline instead of
unconditionally queuing every connection on each scheduler scan. `Send`
submits the next deadline after its immediate update, while datagram input
invalidates the previous deadline and makes that connection due again. Each
registration remains isolated and non-overlapping, so a delayed update cannot
serialize unrelated connections behind it.

KCP client bootstrap is finite even when callers do not supply a cancellation
token. One connection attempt owns a ten-second deadline and retransmits the
same conversation request every 250 milliseconds until a matching response
arrives. Pending-capacity exhaustion returns a fixed, low-cardinality
`ServerBusy` rejection without creating a transport or RPC Session; a lost
request or response falls back to bounded retransmission and ultimately a
`TimeoutException`. Explicit rejection surfaces as
`KcpConnectionRejectedException`, and caller cancellation as
`OperationCanceledException`. Transport
rejection does not represent RPC Session admission or Game Session recovery.

One KCP transport connection is identified by remote UDP address, remote UDP
port, and conversation id together. The listener uses that complete identity
for handshake deduplication, KCP datagram routing, failure containment, and
cleanup. Repeated handshakes for the same identity are idempotent. A reused
endpoint with a new conversation id creates a separate RPC
Session within the existing pending and active connection limits; it never
replaces or terminates another conversation. Whether the new RPC Session
recovers an existing Game Session remains a Lakona.Game decision.

### WebSocket

RPC frame boundaries are independent of WebSocket message and receive boundaries.
Each receive drains a complete buffered RPC frame before requesting more socket
input. Coalesced frames therefore progress even when the peer sends nothing else
or closes afterward; a trailing partial prefix or payload waits for more input.
Already buffered frames may complete before cancellation is observed, as allowed
by the transport contract. They do not bypass disposal's receive admission gate.

Client and accepted-server transports share a framing owner which closes receive
admission when disposal begins. An active receive is aborted and allowed to exit
before its partial-frame accumulator is cleared or the server's disposal callback
runs. Clearing cannot race an append or return the receive operation's pooled
socket buffer while it is still in use. A frame already completed by that receive
keeps its independent ownership, including any slices, after transport disposal.

With no active receive, disposal retains the close handshake with its one-second
timeout and abort fallback. A close handshake must not start a second receive
while an application receive is pending. Repeated disposal shares cleanup
completion, and an accepted transport invokes its disposal callback only once.

`WebSocketReceiveLifetimeTests` verifies both ends with real sockets interrupted
during an 8 MiB partial transfer, with and without caller cancellation first. A
controlled socket additionally delays receive completion after abort to verify
cleanup ordering and the lifetime of a frame which completes during disposal.
`WebSocketBufferedReceiveTests` verifies coalesced frames, partial prefixes and
payloads, peer closure, cancellation and repeated disposal on both endpoints. A
controlled socket also proves buffered complete frames require no further read.

### Loopback

The Loopback transport models one connection pair with one shared lifecycle
owner. Each direction uses a bounded frame queue with wait-based backpressure;
callers may select a smaller capacity for deterministic pressure tests.
Disposing either endpoint closes both directions, wakes pending I/O, rejects
new sends, and releases queued owned frames. Loopback must not report one peer
connected after the other peer has closed.

## Verification Boundary

`tests/Lakona.Rpc.Transport.Tests/TransportLifecycleContractTests.cs` applies the
same checks to TCP, WebSocket, KCP, and Loopback, including both ends where
applicable. Network transports use real local sockets; Loopback uses its bounded
in-memory queues.

| Contract | Evidence |
| --- | --- |
| Repeated live initialization and one-send/one-receive concurrency | Three bidirectional frame exchanges per transport; each receive starts before the opposite send |
| Cancellation of a pending receive | Client and accepted-server cases for all four transports; does not assert post-cancellation reuse |
| Normal and repeated disposal | Both endpoints disconnect after sequential disposal with no active I/O |
| Abort of an idle receive | Disposal ends the actual pending receive, accepting an empty frame or connection/cancellation exception |
| Connection cancellation and bounded bootstrap | `TcpTransportTests`, `KcpTransportTests`, and RPC `RpcClientLifecycleTests` |
| Send serialization and queued cancellation | RPC `RpcConnectionChannelTests` |
| KCP client receive-buffer lifetime | `KcpClientReceiveLifetimeTests` observes pool-return ordering on socket abort, continued protocol ownership during application cancellation, subsequent reception, and incoming frames racing disposal |
| Backpressure, owned queues, and failure isolation | Loopback tests in `Lakona.Rpc.Tests`, KCP transport and regression tests |

The KCP client's UDP loop owns its pooled receive buffer until socket I/O ends;
disposal closes the socket before returning that buffer. Input and KCP disposal
share a lock. `KcpLargeFrameTests` exercises 12 MiB transfers in both directions,
send-only ACK progress, following-frame integrity, bounded queues with a stalled
receiver, pre-send cancellation, partial-send cancellation, and disposal.
`KcpSignalTests` observes first-chance exceptions during repeated capacity
notifications. `TcpRpcProgressTests` checks unique requests and complete response
contents across concurrent bursts and idle transitions with real TCP sockets;
it is a bounded progress check, not a reproduction of every intermittent stall.
These checks run on .NET 10; they do not replace Unity runtime or long-running
loss/reordering and throughput measurements.

Keep failures found by those tests separate from documentation cleanup. Do not
weaken an ownership requirement merely to match one implementation, and do not
describe an unverified path as a guarantee already demonstrated by the suite.
