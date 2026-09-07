# 5. Reliability, Boundedness, And Recovery

Treat failure behavior as part of each module's interface. Build a failure
matrix covering:

- partial startup and rollback
- unavailable, slow, or unhealthy dependencies
- timeout and cancellation at every async seam
- disconnect, reconnect, duplicate, out-of-order, and stale work
- Hotfix load, publication, rollback, unload, and replacement
- graceful and forced shutdown
- overload, slow consumers, and saturated downstream modules

For every queue, cache, registry, retry loop, buffer, timer, background task,
connection, and retained generation, identify:

- its owner
- its capacity or other bound
- expiry, eviction, backpressure, or rejection behavior
- its stop, cancellation, or disposal condition

Look for failure amplification across sessions, Actors, endpoints, nodes, and
processes. Verify that recovery preserves ordering, idempotency, state identity,
and explicit lost-state outcomes. Treat malformed input, unbounded input rate,
and obvious resource-exhaustion paths as reliability findings; leave a complete
adversarial security audit to the security workflow.
