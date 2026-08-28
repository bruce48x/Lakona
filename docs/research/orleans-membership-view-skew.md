# Orleans membership-view skew and Actor routing research

Date: 2026-08-28

## Question and short answer

The investigated failure shape is: a sender routes an Actor request using a
newer committed membership/directory view while the receiver has not yet
observed that view, so the receiver rejects the request and the sender exhausts
its immediate retry budget.

This is **not unique to Lakona**. Temporary view skew, stale Actor-location
caches, ownership movement, and misrouted messages are inherent coordination
problems in distributed Actor runtimes. Orleans explicitly documents and tests
these conditions.

The **exact failure mode is Lakona-specific**: Lakona attaches an exact target
proof (cluster, node incarnation, membership view, Actor activation) to a
business RPC and originally rejected a proof immediately when the receiver's
local membership snapshot was behind. Orleans does not apply that same proof
gate to an ordinary grain invocation. It handles the common underlying races at
several other layers: membership refresh, version-coordinated directory
operations, stale-location cache invalidation, bounded forwarding, and
directory view-change recovery.

Lakona's fix—wait within the invocation lifetime for the receiver's membership
view to reach the proof's committed view, then re-run the exact validation—is
therefore consistent with Orleans' design. It is not an Orleans-specific API
copy; it is the corresponding solution at Lakona's stricter RPC-proof boundary.

## Keep the four concepts separate

### 1. Membership table version and propagation

Orleans membership changes are globally ordered using an atomically updated
membership version. Propagation is nevertheless asynchronous: silos gossip
snapshots for fast convergence and periodically reread the membership table as
a fallback. Therefore, total ordering of committed views does not imply that
all silos observe the latest view simultaneously. The official cluster
management documentation states both properties explicitly: snapshot
broadcast is an optimization, periodic reads remain the fallback, and all
membership configurations are globally ordered ([cluster-management protocol,
steps 4 and 6–8](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/cluster-management#the-membership-protocol)).

The runtime exposes a targeted refresh which repeatedly refreshes until the
requested version has been observed
([`MembershipTableManager.Refresh`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/MembershipService/MembershipTableManager.cs#L114-L131),
[`ClusterMembershipService.Refresh`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/MembershipService/ClusterMembershipService.cs#L53-L77)).
This is the closest low-level analogue to Lakona waiting for a target
`MembershipViewId`.

### 2. Directory ownership and activation registration

Orleans' strongly consistent distributed directory derives range ownership
from membership views. Requests carry the caller's view and responses carry
the partition's view. A mismatch causes synchronization and retry, while range
locks prevent a partition from serving a range during ownership transfer
([official grain-directory design](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/grain-directory),
[`DistributedGrainDirectory` design comment](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/GrainDirectory/DistributedGrainDirectory.cs#L27-L57)).

The receiver side does not simply reject a request whose membership version is
ahead. `GrainDirectoryPartition.WaitForOwnershipViewAsync` waits for the
requested/current view's range transition to complete, then verifies that the
captured view is still current before deciding whether it owns the grain
([source](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/GrainDirectory/GrainDirectoryPartition.Interface.cs#L145-L164)).
On the caller side, a newer response causes `RefreshViewAsync` and re-evaluation
of the owner
([`DistributedGrainDirectory.InvokeAsync`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/GrainDirectory/DistributedGrainDirectory.cs#L228-L314),
[`DirectoryMembershipService.RefreshViewAsync`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/GrainDirectory/DirectoryMembershipService.cs#L31-L44)).

Orleans also explicitly says membership is monotonic but silos can skip
intermediate views. In that case the directory abandons an uncertain handoff
and runs recovery instead
([official design, recovery process](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/grain-directory#recovery-process)).

### 3. Placement and ordinary message routing

The original Orleans paper describes a distributed Actor directory plus local
Actor-location caches. It states that the caches can be stale, that misdirected
messages are rerouted by the recipient or returned to the sender, and that both
sides repair the stale cache/directory information. It also explicitly states
that membership views can diverge temporarily and registration requests can be
misrouted while membership is in flux
([Orleans research paper, sections 3.2 and 3.8–3.9](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/Orleans-MSR-TR-2014-41.pdf)).

The current runtime retains this repair pattern for ordinary messages: an
invalid activation invalidates the local cache, adds a cache-invalidation
header, and forwards/reroutes the message up to `MaxForwardCount`
([`MessageCenter.ProcessRequestToInvalidActivation` and forwarding](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/src/Orleans.Runtime/Messaging/MessageCenter.cs#L336-L473)).
This is analogous to Lakona's stale-activation re-resolution, but it is not the
same as membership proof catch-up.

### 4. Membership gossip is not directory transfer

Gossip distributes a committed membership snapshot. Directory view change then
uses that membership view to transfer ownership and activation registrations.
Placement chooses/creates an activation, and message routing uses cached or
resolved activation locations. A test which merely waits until every node
reports the same membership does not exercise the interval in which a request
arrives after one node has advanced but before another node has processed that
view. That interval was the load-bearing condition in the Lakona failure.

## Orleans tests which cover similar conditions

| Orleans test | What it controls and proves | Similarity to Lakona |
| --- | --- | --- |
| [`StartupTaskTests.StartupTaskCanCallGrains`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.Runtime.Tests/StartupTaskTests.cs#L178-L198) with `DelayedDirectoryMembershipService` ([fixture](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.Runtime.Tests/StartupTaskTests.cs#L77-L145)) | Holds the directory on a stale `Joining` view while cluster membership is already `Active`; a grain call must request a refresh to the active version and complete. | Very close deterministic analogue: one component has the newer committed view while the receiving/routing component is deliberately held behind. |
| [`CachedGrainLocatorTests.LocalGrainDirectoryAppliesNewerMembershipBeforeRegisterForwarding`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.Core.Tests/Directory/CachedGrainLocatorTests.cs#L200-L273) | Supplies a `GrainAddress` stamped with a newer membership version and verifies that the local directory applies that membership before deciding the forwarding owner. | Directly tests “metadata from the future relative to the receiver” before an address-bearing directory RPC is routed. Added by the official [`Test membership refresh before forwarding`](https://github.com/dotnet/orleans/commit/5586e593fe209f2a79e594940ec8568a61784752) change. |
| [`CachedGrainLocatorTests.LocalGrainDirectoryAppliesNewerMembershipBeforeLookupForwarding`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.Core.Tests/Directory/CachedGrainLocatorTests.cs#L275-L371) | Advances a remote silo through `ShuttingDown`, `Stopping`, and `Dead` and verifies lookup ownership/routing after the newer snapshot is applied. | Covers the removal/replacement side of view skew, which is complementary to Lakona's node-join reproduction. |
| [`DistributedGrainDirectoryMembershipTests.OwnerResolutionWaitsForActiveDirectoryMembership`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.GrainDirectory.Tests/GrainDirectory/DistributedGrainDirectoryTests.cs#L39-L88) | Publishes a synthetic stale `Joining` view, blocks the real `Active` update, asserts owner resolution remains incomplete, releases the update, then asserts resolution succeeds. | Closest receiver-wait contract in Orleans' current strongly consistent directory. |
| [`GrainDirectoryResilienceTests.JoiningSilo_DoesNotLeaveStaleEntriesOnPreviousOwner`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.GrainDirectory.Tests/GrainDirectory/GrainDirectoryResilienceTests.cs#L193-L282) | Runs continuous grain calls while a silo joins, waits for directory migration, and checks directory integrity. | Same broad node-join-under-load scenario as Lakona's failing integration test, but its primary assertion is migration/integrity rather than deliberately controlling view skew. |
| [`GrainDirectoryResilienceTests.ElasticChaos`](https://github.com/dotnet/orleans/blob/f7d40d8718fe15c2a4311b74b49ecc0af38af625/test/Orleans.GrainDirectory.Tests/GrainDirectory/GrainDirectoryResilienceTests.cs#L42-L190) | Continuously calls grains while adding, stopping, and killing silos. It explicitly swallows `SiloUnavailableException` and `OrleansMessageRejectionException` as transient. | Demonstrates that Orleans considers some business-call failures during general chaos acceptable. Lakona's join regression is stronger for its narrower scenario because it requires every create/call/destroy iteration to succeed. |

There is **no exact Orleans test for “ordinary grain invocation contains an
exact membership/activation proof newer than the receiver, and the receiver
waits within that invocation's TTL.”** Orleans has no equivalent proof field at
that boundary. Its closest tests are split between directory-version catch-up
and invalid-activation rerouting.

## What is common and what belongs specifically to Lakona

Common distributed-systems problem:

- Local observations of a globally ordered membership history converge at
  different times.
- Directory ownership changes when membership changes.
- Actor activation locations and local route caches can become stale while
  Actors activate, deactivate, or move.
- Immediate retries without delay, refresh, or an update barrier can all land
  inside the same propagation window.
- Correct implementations need a version/epoch fence plus a bounded wait,
  refresh, reroute, or retry policy.

Lakona-specific engineering choice:

- A business RPC carries an exact cluster/node-incarnation/membership-view/
  activation proof and the target validates the whole proof before mailbox
  dispatch.
- The original handler treated “my view is behind the proof” as evidence that
  the proof was stale. It is actually an undecidable state until the receiver
  catches up or the request lifetime expires.
- Because the sender's single stale-route retry was immediate, both attempts
  could predictably fall into one membership propagation interval.

Therefore, the race is general; the leaked `NodeUnavailableException` was a
Lakona policy bug at the proof-validation boundary.

## Lakona coverage assessment and recommended additions

Lakona now has strong focused coverage:

- `Actor_rpc_stops_waiting_for_the_target_membership_view_at_its_deadline`
  proves the wait is bounded.
- `Actor_rpc_waits_through_intermediate_membership_views_before_validating_the_proof`
  proves it does not validate at an intermediate view.
- `Actor_rpc_revalidates_the_exact_node_after_membership_catches_up` proves an
  incarnation change is caught after the wait.
- `Node_join_converges_during_continuous_actor_create_call_and_destroy_load`
  exercises the production path under a real join.

The material gap found during this review was that the integration test relied
on scheduler timing to create the skew, so it could pass without proving that
an RPC was actually held at the membership barrier. Orleans' strongest
analogous tests insert a controllable membership-update gate and assert that
the operation is pending before releasing the newer view.

Implemented P0 coverage:

1. `LakonaTestMembershipViewControl` adds a deterministic
   multi-node/transport-level test hook which pauses the
   target node's application of one committed membership view while the sender
   has already advanced.
2. `Actor_call_waits_for_a_lagging_receiver_membership_view_over_real_transport`
   creates one hot Actor, holds its receiver behind during a real node join,
   and calls it through the normal Directory, route cache, RPC transport, and
   mailbox path.
3. The test observes an additional blocked Membership waiter, asserts that the
   call is pending, releases the target view, and verifies success with exactly
   one Actor-side increment.

This closes the deterministic sender-newer/receiver-older integration gap. The
existing join-under-load test remains valuable as broader transition stress.

Recommended P1 matrix:

- Repeat the deterministic barrier for node leave/restart (same node name, new
  incarnation), not only join.
- Exercise both directions: sender newer/receiver older, and sender older/
  receiver newer with stale activation re-resolution.
- Cover an unreachable/far-future proof view through the real transport and
  assert deadline-bounded failure plus the correct proof-failure metric.
- Keep one many-Actor migration/integrity test and one single-hot-Actor churn
  test. They expose different bugs: range transfer breadth versus repeated
  activation replacement for one identity.

## Sources and version note

Source-code links are pinned to Orleans commit
[`f7d40d8718fe15c2a4311b74b49ecc0af38af625`](https://github.com/dotnet/orleans/commit/f7d40d8718fe15c2a4311b74b49ecc0af38af625),
the `main` revision inspected on 2026-08-28. Relevant Orleans v10.2 work also
includes [`Fix LocalGrainDirectory membership reconciliation`](https://github.com/dotnet/orleans/commit/80ef781b025d70d7d082722a6ccbabcdcf91d7f7),
[`Refresh membership before address RPC routing`](https://github.com/dotnet/orleans/commit/4e6a17205828e5ae392d26098d46a79e76674f77),
and the official [v10.2.0 release notes](https://github.com/dotnet/orleans/releases/tag/v10.2.0).
