---
title: "Reliable Business Push: Why Reliable Transport Is Not Enough"
date: 2026-05-07T11:25:00+08:00
summary: "Business push needs proof that the client has applied an event; transport alone cannot provide that."
---

Transport can make data reach a connection more reliably, but it cannot prove that the client has applied a business event to UI or local session state. In an online game, that gap can directly turn into a stuck player flow.

## A Typical Failure Case

1. Players A and B enter matchmaking.
2. The server creates a room and pushes `Matched` to both clients.
3. A receives the push and enters the room.
4. B reconnects during the push window.
5. The old connection is already gone, and the server does not know whether B actually handled `Matched`.
6. B may stay on the matchmaking screen forever.

This is not something a serializer or transport can solve by itself. The business layer needs a push mechanism that can be acknowledged, replayed, and deduplicated.

## The Lakona.Game Model

Lakona.Game uses at-least-once delivery with a per-owner monotonic sequence number:

- the server assigns an increasing sequence to each owner
- the outbox keeps business push records that have not been acknowledged
- the client only applies messages newer than its local latest sequence
- after applying a message, the client acknowledges the latest sequence
- the server removes records where `sequence <= latestAppliedSequence`
- after reconnect, the server replays records that are still pending

This is more practical than chasing exactly-once delivery. Reconnects, process restarts, client crashes, and server failover all break exactly-once assumptions; at-least-once delivery plus idempotent handling is easier to verify.

## Responsibility Boundary

`Lakona.Game.Server` owns the generic mechanism:

- sequence allocation
- pending record storage
- replay after reconnect
- pruning after acknowledgement
- retention and pending-count limits

Business code owns the semantics:

- decide which messages require reliable delivery
- include the sequence in the payload
- expose an ack RPC or piggyback ack on an existing request
- make client handlers idempotent

This keeps reliable push as host/session infrastructure instead of moving business concepts such as matchmaking, rooms, mail, or rewards into the framework core.

## State Loss Must Be Explicit

If the client believes it can resume a session but the server has already lost compatible state, the server must not treat that as a normal successful reconnect.

Common causes include:

- the client stayed offline beyond the reconnect grace period
- the gateway restarted and lost its in-memory outbox
- the server cleaned up the session

The correct behavior is to return an explicit state-lost result and require the client to clear old state and start a new session, instead of leaving it on a stale matchmaking or in-match UI.

## Implementation Location

The implementation belongs in `Lakona.Game.Server.ReliablePush`:

- `IReliablePushOutbox`
- `InMemoryReliablePushOutbox`
- `ReliablePushOptions`

The more complete internal design notes remain in `CONTRIBUTING.md` at the repository root.
