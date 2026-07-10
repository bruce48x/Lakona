# Framework Startup Success Log Design

## Goal

Emit one framework-level success log only after the Lakona server is genuinely
ready to serve traffic:

```text
Lakona server started successfully. NodeId={NodeId}.
```

Existing component-level listener logs remain in place for troubleshooting.

## Scope Checkpoint

- **Goal:** Make the framework success log a trustworthy declaration that
  framework startup completed, rather than a message emitted merely because the
  generic host reached its started phase.
- **Affected surfaces:** `Lakona.Game.Server` startup hosting, RPC/cluster RPC
  listener readiness in `Lakona.Rpc.Server`, health and local-admin listener
  readiness, focused tests, current startup documentation, and package versions.
- **Coupling:** Listener binding, hosted-service lifecycle ordering, actor startup,
  cluster registration, and final success logging are one strongly coupled
  runtime change and remain under one continuity-preserving implementation owner.
- **Independent slices:** Documentation wording and final source scans are safe
  independent review tasks after the runtime contract is stable. No helper-agent
  work is required for implementation.
- **Compatibility:** No configuration or application-facing usage changes are
  introduced. Any RPC-server readiness hook needed by the game-server host stays
  internal so this work does not expand the public API.
- **Validation:** Focused `Lakona.Game.Server.Tests` and `Lakona.Rpc.Tests`, then
  both affected test projects, solution build/test as practical, package-version
  graph guard, `git diff --check`, and final diff inspection.
- **Versioning:** Changes to shippable sources require patch version bumps for
  `Lakona.Game.Server` and `Lakona.Rpc.Server`.

This is a large cross-cutting change because it alters runtime lifecycle
semantics and spans two packages, but it remains within the requested framework
startup and logging boundary.

## Considered Approaches

### Explicit listener-readiness barrier

Each framework listener exposes an internal completion signal. The signal
completes only after its actual socket or transport acceptor has bound
successfully. A final hosted lifecycle service waits for all enabled framework
listeners during the host's started phase and then writes the success log.

This is the selected approach. It provides an exact readiness boundary without
adding configuration or application-facing APIs.

### Unified two-phase listener API

Every listener could be redesigned around separate bind/start and run phases.
This would make the lifecycle explicit throughout the runtime, but it would
expand the public RPC hosting surface and broaden the change beyond the logging
goal.

### Post-start port probing

The framework could connect to configured ports after host startup. This is
rejected because probing is racy, cannot prove the current process owns the
listener, and does not directly propagate the original bind failure.

## Startup Success Contract

The framework emits the success log only after all of the following complete:

1. Initial hotfix loading succeeds.
2. Every configured startup actor is created successfully.
3. Actor activation and hotfix actor-start lifecycle callbacks complete
   successfully.
4. Cluster node registration completes successfully when cluster registration
   is enabled.
5. Every enabled client RPC and cluster RPC acceptor binds successfully.
6. The health HTTP listener binds successfully when enabled.
7. The local-admin HTTP listener binds successfully when enabled.
8. Every other framework `IHostedService.StartAsync` operation returns
   successfully.

Disabled optional listeners satisfy their readiness requirement without
opening a socket. A server with no listener configurators may still start
successfully after the remaining framework startup work completes.

If any required operation fails or startup is cancelled, the final success log
is not emitted. The original failure is propagated through host startup rather
than converted into a warning or delayed background failure.

## Architecture

### Listener readiness

`RpcServersHostedService` tracks all configured RPC servers as one framework
readiness unit. Each server reports readiness at the point where
`RpcServerHost` has acquired the transport acceptor and the acceptor owns its
bound listening endpoint. The aggregate unit completes only after every
configured RPC or cluster RPC server reports readiness.

The RPC readiness notification is an internal runtime hook shared with
`Lakona.Game.Server`; the existing public `RunAsync` behavior remains unchanged.

`LakonaHealthHttpHostedService` and `LakonaLocalAdminHostedService` expose
internal readiness tasks. Each task completes immediately when its endpoint is
disabled, after `listener.Start()` when enabled, or with the original exception
when binding fails.

Readiness tasks must also complete as cancelled if shutdown wins before binding,
so the final waiter cannot hang during aborted startup.

### Final success logger

A dedicated framework hosted lifecycle service runs its final check during
`StartedAsync`. At this point normal hosted-service `StartAsync` methods have
already completed, covering startup actors, their lifecycle callbacks, cluster
registration, timers, and other synchronous startup work.

The lifecycle service then awaits the internal readiness tasks for all
registered listener services. After every task succeeds, it writes exactly one
structured information-level log:

```text
Lakona server started successfully. NodeId={NodeId}.
```

The message has one structured property, `NodeId`. It does not repeat startup
actor counts or listener addresses because component-level logs already provide
those details.

## Ordering and Failure Handling

.NET 10 runs the entire `BackgroundService.ExecuteAsync` method on a background
thread. Therefore generic-host startup alone does not prove that code before the
first `await` has executed or that a listener has bound. The explicit readiness
tasks close this gap.

Listener background services catch failures only long enough to fault their
readiness task, then rethrow so normal host background-service failure handling
still applies. The final lifecycle service observes the same failure during
startup and prevents the success log.

Shutdown behavior remains unchanged after successful startup. Component-level
stop and disconnect logs continue to describe runtime shutdown.

## Testing

Focused tests protect observable behavior rather than implementation details:

- the final log is absent until every configured RPC listener reports a bound
  acceptor;
- the final log is emitted once and contains only the expected message and
  structured `NodeId` value;
- startup actor creation and actor-start lifecycle callbacks precede the final
  log;
- cluster registration precedes the final log;
- an RPC, health, or local-admin bind failure faults startup and suppresses the
  final log;
- disabled health or local-admin listeners do not block startup;
- zero RPC configurators do not block startup;
- cancellation before listener readiness does not hang startup;
- existing component-level listener logs remain intact.

Implementation follows test-driven development: each new contract is first
captured by a focused failing test, the expected failure is observed, and only
then is the minimal runtime change added.

## Milestones and Review Gates

1. Add failing listener-readiness and final-log tests; review the lifecycle
   boundary before production edits.
2. Add the internal RPC acceptor-ready notification and aggregate readiness;
   run focused RPC tests.
3. Add health and local-admin readiness; run focused game-server tests.
4. Add the final hosted lifecycle logger and ordering/failure tests.
5. Update current startup documentation and patch package versions.
6. Run affected suites, repository guards, hygiene scans, and final integration
   review.

The same implementation owner handles milestones 1 through 4. Documentation
and source scans may be reviewed independently once the runtime contract is
stable.
