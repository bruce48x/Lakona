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
| Backpressure, owned queues, and failure isolation | Loopback tests in `Lakona.Rpc.Tests`, KCP transport and regression tests |

These checks run on .NET 10. They do not establish the Unity/netstandard fallback
behavior, bounded cancellation of a network send under sustained pressure,
buffer safety when disposal races active datagram delivery, or frame integrity
after partial-send cancellation. Those need targeted fault tests; an idle-receive
abort test is not evidence for every disposal race. The implementation must still
honor memory ownership in such races. In particular, KCP client disposal returns
its receive buffer before joining a receive operation; safe ownership during
concurrent delivery needs separate verification before declaring that race safe.

Keep failures found by those tests separate from documentation cleanup. Do not
weaken an ownership requirement merely to match one implementation, and do not
describe an unverified path as a guarantee already demonstrated by the suite.
