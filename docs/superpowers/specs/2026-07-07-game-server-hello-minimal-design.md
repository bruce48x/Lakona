# Minimal GameServerHello Design

Date: 2026-07-07
Status: accepted design

## Problem

`GameServerHello` has accumulated fields that do not change client behavior:

- `EndpointTransport`
- `EndpointSerializer`
- `ServerTimeUtc`
- `ServerNodeId`
- `ReliablePush.DeliveryMode`
- `ReliablePush.ReplaySupported`
- `ReliablePush.MaxPending`

`EndpointTransport` and `EndpointSerializer` are connection facts that both
sides already know before the handshake RPC can run. A client cannot switch its
transport or serializer after creating the connection, so echoing these values
does not negotiate or validate anything useful.

`ServerTimeUtc` and `ServerNodeId` are currently diagnostic-looking values, but
the generated game client does not use them for clock skew, routing, node
affinity, reconnect targeting, or telemetry. Keeping them in the framework
handshake implies behavior that does not exist.

`ReliablePush.DeliveryMode`, `ReliablePush.ReplaySupported`, and
`ReliablePush.MaxPending` are server-side policy details or derivable labels.
The client currently needs to know whether reliable push is enabled and whether
acks are required. It does not use the string delivery mode, replay flag, or
server outbox capacity.

## Product Decision

`GameServerHello` should describe only framework policies the client must apply
after the handshake. It should not echo endpoint facts, expose server-internal
capacity, or reserve fields for hypothetical diagnostics.

The target shape is:

```csharp
public sealed class GameServerHello
{
    public int SelectedProtocolVersion { get; set; }

    public ReliablePushHandshakeSettings ReliablePush { get; set; } = new();

    public GameHeartbeatHandshakeSettings Heartbeat { get; set; } = new();
}

public sealed class ReliablePushHandshakeSettings
{
    public bool Enabled { get; set; }

    public bool AckRequired { get; set; }
}
```

Keep:

- `SelectedProtocolVersion`: the client validates this before applying any
  server policy.
- `ReliablePush.Enabled`: the client uses this to decide whether reliable push
  middleware is active.
- `ReliablePush.AckRequired`: the client uses this with `Enabled` to decide
  whether acknowledgements are required.
- `Heartbeat.Interval` and `Heartbeat.Timeout`: the server owns heartbeat
  timing policy and the client starts its heartbeat loop from these values.

Remove:

- `ServerNodeId`
- `EndpointTransport`
- `EndpointSerializer`
- `ServerTimeUtc`
- `ReliablePush.DeliveryMode`
- `ReliablePush.ReplaySupported`
- `ReliablePush.MaxPending`

## Protocol Boundary

This is a breaking protocol cleanup across the Lakona.Game package set. The
repository is still in early development, and the framework does not support
mixing old and new Lakona.Game protocol packages in one deployment. Change the
abstractions, server runtime, client runtime, tests, docs, and package versions
together.

The internal codec should write the remaining fields in a compact deterministic
layout:

1. `SelectedProtocolVersion`
2. `ReliablePush.Enabled`
3. `ReliablePush.AckRequired`
4. `Heartbeat.Interval.Ticks`
5. `Heartbeat.Timeout.Ticks`

The codec should continue to validate:

- positive selected protocol version;
- positive heartbeat interval;
- positive heartbeat timeout;
- heartbeat timeout not shorter than heartbeat interval.

`ReliablePush.MaxPending` validation stays in server-side guardrails and
reliable push runtime configuration. It is not a client handshake concern.

## Documentation Updates

Update `docs/session.md` so its handshake description no longer says
`ServerHello` returns node identity, endpoint transport, endpoint serializer, or
server time. The durable rule should say:

> `ServerHello` returns the selected protocol version plus server-owned
> reliable-push and heartbeat policies. Endpoint transport, endpoint serializer,
> server node identity, server time, platform, runtime, build, and capability
> metadata are not part of the default framework handshake unless they have
> concrete framework behavior.

## Tests

Update tests around:

- `LakonaInternalCodecTests` to assert the compact `GameServerHello` roundtrip
  and absence of removed fields from the DTO contract;
- `GameHandshakeTests` to assert only retained reliable push and heartbeat
  policies;
- client tests only if they construct `GameServerHello` with removed fields;
- repository scans that currently mention endpoint serializer metadata.

## Out Of Scope

Do not add replacement diagnostics in this change. If node identity, server
time, or endpoint metadata later becomes useful, introduce it with concrete
behavior such as reconnect affinity, latency/clock-skew measurement, admission
policy, or structured diagnostics.

Do not change `GameClientHello`; it already carries only
`ProtocolVersion = 1`.
