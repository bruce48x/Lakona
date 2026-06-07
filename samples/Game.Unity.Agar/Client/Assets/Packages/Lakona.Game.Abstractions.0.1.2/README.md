# Lakona.Game.Abstractions

`Lakona.Game.Abstractions` contains the small set of framework-owned types shared by `Lakona.Game.Server` and `Lakona.Game.Client`.

Current shared types include:

- `GameSessionKey`
- `GameEndpointName`
- `ReliablePushSequence`
- `ReliablePushAckStatus`
- `ReliablePushAckOutcome`
- `SessionResumeStatus`
- `SessionResumeDecision`

It intentionally does not contain game DTOs, account models, matchmaking payloads, room state, or engine-specific APIs. Put those in your own shared game project.
