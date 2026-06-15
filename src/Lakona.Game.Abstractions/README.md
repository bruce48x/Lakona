# Lakona.Game.Abstractions

`Lakona.Game.Abstractions` contains the small set of client-safe framework-owned types shared by `Lakona.Game.Server` and `Lakona.Game.Client`.

Current shared types include:

- `ReliablePushSequence`
- `ReliablePushAckStatus`
- `ReliablePushAckOutcome`
- `SessionTerminationNotice`
- `SessionTerminationReason`

Server-owned session identity, including `GameSessionKey`, lives in `Lakona.Game.Server`. This package intentionally does not contain game DTOs, account models, matchmaking payloads, room state, or engine-specific APIs. Put those in your own shared game project.
