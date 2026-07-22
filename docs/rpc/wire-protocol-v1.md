# Lakona.Rpc Wire Protocol v1

Status: draft package-set contract

Date: 2026-06-04

## Scope

This document defines the Lakona.Rpc RPC envelope wire format. The envelope format describes the bytes produced and consumed by `RpcEnvelopeCodec`.

Lakona.Game framework-internal payloads such as handshake and heartbeat travel
inside normal RPC envelopes but use `LakonaInternalCodec` for the envelope
payload bytes. This document defines the envelope; the internal Lakona.Game
payload codec is documented in [Session Lifecycle](../session.md).

This document does not define:

- concrete transport framing, such as TCP length prefixes, WebSocket messages, or KCP handshakes
- payload serializer formats, such as JSON or MemoryPack DTO bytes
- optional security transforms, such as compression and encryption wrapping

The current envelope format has no explicit version field. The framework is
still early, so this document describes the current package-set contract rather
than a backwards-compatible public wire standard. Do not mix RPC packages that
were built against different versions of this document in one deployment.

## Primitive Encoding

- `byte`: unsigned 8-bit value.
- `int32`: signed 32-bit integer, big-endian.
- `uint32`: unsigned 32-bit integer, big-endian.
- `int64`: signed 64-bit integer, big-endian.
- UTF-8 strings are encoded as an `int32` byte length followed by that many UTF-8 bytes.
- Payload bytes are opaque to the envelope layer. Business RPC payload bytes
  are produced by the configured `IRpcSerializer`; framework-internal
  Lakona.Game payloads use `LakonaInternalCodec`.

The maximum accepted RPC envelope payload length is `RpcProtocolLimits.DefaultMaxPayloadSize`, currently 64 MiB.

## Frame Types

| Value | Name |
| --- | --- |
| `1` | `Request` |
| `2` | `Response` |
| `3` | `Push` |
| `4` | `KeepAlivePing` |
| `5` | `KeepAlivePong` |

These numeric values are stable v1 wire values and must not be reused for different meanings.

## Request Frame

Client-to-server RPC request.

| Field | Type | Notes |
| --- | --- | --- |
| `FrameType` | `byte` | Must be `1`. |
| `RequestId` | `uint32` | Client-assigned correlation id. |
| `ServiceId` | `int32` | Stable service id from `[RpcService]`. |
| `MethodId` | `int32` | Stable method id from `[RpcMethod]`. |
| `PayloadLength` | `int32` | Number of payload bytes. |
| `Payload` | `byte[PayloadLength]` | Serialized request DTO bytes. |

Example:

```text
01                                      FrameType=Request
00 00 00 01                             RequestId=1
00 00 00 02                             ServiceId=2
00 00 00 03                             MethodId=3
00 00 00 03                             PayloadLength=3
AA BB CC                                Payload
```

## Response Frame

Server-to-client RPC response.

| Field | Type | Notes |
| --- | --- | --- |
| `FrameType` | `byte` | Must be `2`. |
| `RequestId` | `uint32` | Request id being answered. |
| `Status` | `byte` | RPC response status. |
| `PayloadLength` | `int32` | Number of payload bytes. |
| `Payload` | `byte[PayloadLength]` | Serialized return DTO bytes, often empty for failures. |
| `HasError` | `byte` | `0` means no error string; non-zero means an error string follows. |
| `ErrorLength` | `int32` | Present only when `HasError` is non-zero. |
| `ErrorUtf8` | `byte[ErrorLength]` | Present only when `HasError` is non-zero. |

Status values:

| Value | Name | Meaning |
| --- | --- | --- |
| `0` | `Ok` | Request completed successfully. |
| `1` | `NotFound` | Target service or method was not found. |
| `2` | `HandlerError` | Server handler failed or returned an invalid framework response. |
| `3` | `Overloaded` | Server could not accept the request because it is overloaded. |
| `4` | `BadRequest` | Request reached the RPC layer but was invalid for the target RPC contract. |
| `5` | `ProtocolError` | Peer violated the RPC wire protocol or connection state machine. |

`RpcStatus` is a framework-only status taxonomy. Business failures belong in business DTOs, not in response status values.

Example response with no error string:

```text
02                                      FrameType=Response
01 02 03 04                             RequestId=0x01020304
00                                      Status=Ok
00 00 00 02                             PayloadLength=2
10 20                                   Payload
00                                      HasError=false
```

Example response with an error string:

```text
02                                      FrameType=Response
00 00 00 07                             RequestId=7
02                                      Status=HandlerError
00 00 00 00                             PayloadLength=0
01                                      HasError=true
00 00 00 04                             ErrorLength=4
66 61 69 6C                             ErrorUtf8="fail"
```

## Push Frame

Server-to-client push notification. Push frames do not carry a request id.

| Field | Type | Notes |
| --- | --- | --- |
| `FrameType` | `byte` | Must be `3`. |
| `ServiceId` | `int32` | Stable service id associated with the notification contract. |
| `MethodId` | `int32` | Stable notification method id from `[RpcNotification]`. |
| `MetadataCount` | `int32` | Number of metadata entries. Current runtimes accept `0` or `1`. |
| `MetadataTypeLength` | `int32` | Present for each metadata entry; number of UTF-8 metadata type bytes. |
| `MetadataTypeUtf8` | `byte[MetadataTypeLength]` | Non-empty metadata type understood by higher-level packages. |
| `MetadataPayloadLength` | `int32` | Present for each metadata entry; number of opaque metadata payload bytes. |
| `MetadataPayload` | `byte[MetadataPayloadLength]` | Present for each metadata entry; metadata payload bytes owned by the metadata type. |
| `PayloadLength` | `int32` | Number of payload bytes. |
| `Payload` | `byte[PayloadLength]` | Serialized notification DTO bytes. |

Example:

```text
03                                      FrameType=Push
00 00 00 02                             ServiceId=2
00 00 00 03                             MethodId=3
00 00 00 00                             MetadataCount=0
00 00 00 02                             PayloadLength=2
AA BB                                   Payload
```

Metadata is placed before the business payload so generated clients and
engine-neutral runtimes can inspect framework metadata before deserializing or
invoking the notification callback.

The envelope layer treats metadata as a generic typed byte payload. Higher-level
packages own metadata type names and payload layouts. For example,
Lakona.Game reliable push uses the `lakona.game.reliable-push` metadata type
and encodes session id, sequence, and delivery kind with
`LakonaInternalCodec`; those fields are not business DTO fields and are not
interpreted by `Lakona.Rpc.Core`.

## Keepalive Frames

Keepalive timestamps use UTC ticks stored as `int64`.

Ping:

| Field | Type | Notes |
| --- | --- | --- |
| `FrameType` | `byte` | Must be `4`. |
| `TimestampTicksUtc` | `int64` | Sender timestamp. |

Pong:

| Field | Type | Notes |
| --- | --- | --- |
| `FrameType` | `byte` | Must be `5`. |
| `TimestampTicksUtc` | `int64` | Timestamp copied from the matching ping. |

## Decode Failure Rules

The v1 decoder rejects malformed envelopes:

- empty frame data
- unexpected frame type for the requested decode method
- negative payload or error string lengths
- payload or error string lengths above the configured maximum
- declared lengths that exceed remaining frame bytes
- unsupported Push metadata counts
- empty Push metadata type strings
- extra trailing bytes after a complete envelope

Transport/session code may close the connection after decode failures. Application code should treat malformed envelope bytes as protocol errors, not business failures.

## Change Rules

- Wire changes are allowed while the framework is early, but they must update
  this document and the `RpcEnvelopeCodec` golden-byte tests in the same change.
- Do not add compatibility shims that make the envelope shape less direct unless
  there is an explicit supported mixed-version deployment requirement.
- Published service ids, method ids, and push ids must not be reused for
  different meanings inside a package set.
- New frame types or status values must document their current runtime behavior.
