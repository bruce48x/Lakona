# Lakona Domain Language

Lakona separates product-owned player continuity from framework-owned game
sessions and connection-scoped RPC sessions. These terms keep lifecycle and
recovery discussions precise.

## Language

**Player Session**:
The product-owned continuity of one authenticated player, including the player's current room and match participation. It may reference multiple Game Sessions.
_Avoid_: Game session, connection, RPC session

**Control Game Session**:
A resumable Game Session used for control-plane requests and reliable business notifications.
_Avoid_: WebSocket session, login connection

**Realtime Game Session**:
A resumable Game Session used for latency-sensitive gameplay input and realtime notifications.
_Avoid_: KCP session, battle connection

**Game Session**:
A framework-owned resumable identity whose state may outlive the RPC Session currently bound to it.
_Avoid_: Connection, RPC session

**RPC Session**:
A connection-scoped RPC lifetime that ends permanently when its transport connection ends.
_Avoid_: Game session, resumable session

**Recovery Anchor**:
The Control Game Session that authoritatively coordinates recovery of the Player Session and its Realtime Game Session.
_Avoid_: Primary connection, master socket

**Gateway Affinity**:
The requirement that a reconnecting Control Game Session return to the gateway owner that retains its session and reliable sequence state.
_Avoid_: Cross-gateway migration, redirect, shared outbox

**Game Session Resume Window**:
The server-defined interval after RPC disconnection during which the same Game Session may resume and automatic client recovery may continue.
_Avoid_: Retry count, outbox retention, route lease

**Reliable Push**:
An endpoint-level, explicitly enabled delivery policy that retains unacknowledged callback commands for replay across RPC Sessions bound to the same Game Session.
_Avoid_: Transport reliability, KCP retransmission, default notification delivery

**Reliable Sequence**:
The contiguous per-Game-Session ordering of reliable callback commands. Commands may be transmitted more than once, but each sequence is applied at most once and a later sequence is never applied across a gap.
_Avoid_: Arrival order, best-effort ordering, exactly-once transport

**State Refresh Required**:
A recovery outcome stating that the Game Session still exists but reliable notification continuity can no longer be proven, so ordinary replay must not continue.
_Avoid_: Successful resume, silent message loss, terminated session
