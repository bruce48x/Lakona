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
and keepalive. Client and Session receive loops supply the single reader. A
custom caller bypassing those owners must provide the same serialization.
Repeated initialization is not permission to race two `ConnectAsync` calls or to
race initialization with disposal. Normal client disposal cancels and joins its
connection attempt before releasing the transport. Concurrent disposal callers
must coordinate at their owner; the shared disposal completion in RPC runtime
does not imply that every transport implements that policy independently.

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
| KCP | Client performs the bounded bootstrap; accepted server initialization may already have occurred in the listener. UDP supplies no peer-close signal, so silent peer loss needs RPC keepalive. Sending synchronously queues owned bytes into KCP. |
| Loopback | Initialization activates an endpoint of one shared pair. Disposing either endpoint closes both directions and wakes waiting readers/writers. A closed pair cannot become a new connection. |

## Official Transport Behavior

### KCP

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
| KCP client receive-buffer lifetime | `KcpClientReceiveLifetimeTests` observes pool-return ordering on socket abort, return on cancellation, subsequent reception, and incoming frames racing disposal |
| Backpressure, owned queues, and failure isolation | Loopback tests in `Lakona.Rpc.Tests`, KCP transport and regression tests |

The KCP client rents a buffer for each receive operation and returns it in that
operation's `finally`, after socket I/O and datagram processing have ended.
Disposal closes the socket without returning the active receive's buffer. Input
processing checks connection/KCP state under the same lock as KCP disposal, so
an arriving datagram cannot access a released KCP instance.

These checks run on .NET 10. They do not establish Unity/netstandard runtime
behavior, bounded cancellation of a network send under sustained pressure, or
frame integrity after partial-send cancellation. The targeted KCP receive tests
do not establish every send/disposal race or replace sustained stress testing.

Keep failures found by those tests separate from documentation cleanup. Do not
weaken an ownership requirement merely to match one implementation, and do not
describe an unverified path as a guarantee already demonstrated by the suite.
