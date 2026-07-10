# Gateway-Owned Reliable Push Design

## Status

Approved direction: the node that owns a game session route is the only node
that may assign reliable-push sequence numbers, retain that session's outbox,
accept acknowledgements, and replay pending notifications.

This design fixes the Agar three-node failure in which the gateway sent
`Queued` with sequence 1 and the data node independently sent `Matched` with
sequence 1. The client correctly discarded the second notification as a
duplicate even though it contained the match result.

## Scope Checkpoint

- Goal: make notifications published from any cluster node enter the reliable
  push outbox on the session-owning gateway before they are sent to the client.
- Affected surfaces: `Lakona.Game.Server` session notification routing,
  reliable push dispatch, cluster notification binding, server tests, the Agar
  three-node example tests, `docs/session.md`, and `docs/cluster.md`.
- Coupling: route resolution, owner-side sequencing, ACK handling, replay, and
  cluster dispatch form one strongly coupled runtime change and must remain
  under one continuity-preserving implementation owner.
- Independent work: final review and documentation consistency checks may be
  delegated after the runtime contract is stable. Runtime implementation is
  not split across agents.
- Compatibility: no new business-facing API is required. Internal framework
  wiring may be replaced rather than preserving the incorrect publication
  order. Public infrastructure types that are not intended as user extension
  points may be narrowed or removed if that produces a cleaner boundary.
- Validation: focused notification and reliable-push tests, the complete
  `Lakona.Game.Server.Tests` project, Agar `BusinessLogic.Tests`, documentation
  consistency and package-version graph checks, then the dedicated Agar
  three-node Docker Compose plus Unity PlayMode smoke test.
- Versioning: the change ships in `Lakona.Game.Server`. That package is already
  at the new unreleased `0.12.0` version on this branch, so it must not receive
  a second version bump for the same release.

## Ownership Rule

A `GameSessionKey` has one current route owner. That owner is the reliable-push
authority for the session generation.

Only the owner may:

- allocate a sequence number;
- add a notification to the in-memory pending outbox;
- attach reliable-push metadata to the outbound RPC notification;
- decide whether an ACK is valid and remove acknowledged records;
- replay pending records after resume or heartbeat.

Other nodes publish an unsequenced notification intent. They never create a
local reliable-push record for a remotely owned session.

The owner is normally a gateway because that node owns the client RPC session
and callback binding. The rule is expressed in terms of route ownership rather
than a configured node role so it also works for single-node servers and other
topologies.

## Publication Flow

### Local owner

```text
business notification intent
  -> resolve session route as local (or use local mode without clustering)
  -> owner reliable-push runtime allocates sequence and stores record
  -> local callback dispatcher sends notification
  -> client ACK returns to the same owner's outbox
```

### Remote owner

```text
business notification intent on data/battle node
  -> resolve session route as remote gateway
  -> send unsequenced command over the cluster endpoint
  -> gateway cluster handler validates the session identity
  -> gateway reliable-push runtime allocates sequence and stores record
  -> gateway local callback dispatcher sends notification
  -> client ACK and replay use the same gateway outbox
```

Cluster notification commands are intents, not prebuilt reliable-push frames.
Reliable metadata received over the remote-intent path must not be trusted or
forwarded. The owner creates metadata from its own outbox record.

## Runtime Boundaries

The existing order is inverted: `ClientNotifications` currently writes to the
calling node's outbox before route resolution. The new order is:

1. Capture the typed callback invocation into a notification command.
2. Resolve the current session owner.
3. If local, publish through the local owner's reliable-push runtime.
4. If remote, relay the unsequenced command to that owner.
5. On remote receipt, publish through the receiving owner's reliable-push
   runtime before local callback dispatch.

The owner-side reliable-push runtime dispatches only to a local callback. It no
longer performs cluster routing after assigning a sequence. This separation
prevents a record and its ACK endpoint from living on different nodes.

Single-node hosting remains simple: with no cluster routing services, the
current node is the owner and publication goes directly through its local
outbox and callback dispatcher.

## Failure Semantics

- Missing or stale session route: return `RouteNotFound`; do not create an
  outbox record on an arbitrary business node.
- Remote transport failure before owner acceptance: return `Failed`; no remote
  sequence has been consumed.
- Owner accepts the intent but the callback is temporarily unavailable: retain
  the owner-side pending record for replay and return the immediate callback
  status under the existing `ClientNotificationStatus` contract.
- Reliable push disabled: the owner immediately dispatches without sequence,
  ACK, or replay metadata. Remote intents still travel to the owner first so
  callback ownership remains correct.
- Owner process failure: in-memory pending notifications may be lost. The user
  explicitly accepts queue clearing; this design does not add Redis, database
  persistence, replication, or cross-gateway outbox transfer.
- Session route moves to another gateway: the old in-memory outbox is not
  migrated in this iteration. A resumed/new generation starts under the new
  owner's state according to the existing session-generation rules.

## Concurrency And Ordering

The in-memory outbox remains the serialization point for sequence allocation
within one owner process. Notifications reaching the owner concurrently receive
unique, monotonically increasing sequence numbers for the session owner key.

The framework guarantees ordering at the owner-side acceptance boundary, not
by business-event creation timestamps across nodes. This is sufficient for the
client inbox contract: every accepted notification has one distinct sequence
and replay follows that sequence order.

## Testing Strategy

The implementation is test-driven. The primary regression must fail before the
runtime change and demonstrate this exact topology:

1. A gateway-owned session receives a local queued notification.
2. A remote node publishes a matched notification for the same session.
3. Both notifications are captured after owner-side dispatch.
4. Their reliable sequences are 1 and 2, not 1 and 1.
5. The gateway outbox, ACK service, and replay path recognize both records.

Additional focused coverage must verify:

- remote commands arrive without caller-assigned reliable metadata;
- owner-side receipt assigns metadata exactly once;
- disabled reliable push preserves remote delivery without metadata;
- missing/stale routes do not create a non-owner outbox record;
- custom notification dispatcher registrations remain preserved;
- single-node notification and replay behavior remains unchanged.

The final acceptance test is
`scripts/game/ci/test-agar-three-node.ps1`. Unity must receive the matched KCP
endpoint, attach the realtime connection, enter the match, receive world state,
and submit movement input. The test result XML must contain exactly one passed
test case.

## Out Of Scope

- durable or replicated reliable-push storage;
- migration of pending records when a gateway fails;
- globally unique sequences across unrelated sessions;
- business-selectable reliable versus best-effort delivery per notification;
- Startup Actor service-group implementation;
- changing Agar matchmaking policy or the five-second deadline mechanism
  beyond verifying that the matched notification becomes observable.
