# MMO Sample Language

## Character

A Character is the persistent in-world identity controlled through one Game Session. Client code sends Commands for a Character; it never sends resulting position, health, or damage.

## Zone

A Zone is one server-authoritative simulation partition. `ZoneActor` exclusively owns its live Entities, pending Commands, simulation tick, AOI calculation, and authoritative combat results.

## Entity

An Entity is a Character or Monster simulated by a Zone. An Entity is not an Actor and cannot mutate itself concurrently with the Zone.

## Command

A Command is sequenced client intent: movement direction and an optional attack target. The Zone validates ownership and sequence, then applies it on a later server tick.

## Interest Set

An Interest Set is the bounded set of Entities relevant to one observing Character. Each World Snapshot contains only that Character and nearby Entities.

## World Snapshot

A World Snapshot is an authoritative, server-tick-stamped projection of an Interest Set. The client may interpolate between snapshots for presentation but must not treat prediction as authoritative state.
