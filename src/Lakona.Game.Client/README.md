# Lakona.Game.Client

Engine-neutral Game client connection, session, heartbeat, and reliable-push
primitives. Use the project's generated `LakonaGameClient` as the application
entry point; the package does not depend on Unity, Godot, or a transport.

## Install

```powershell
dotnet add package Lakona.Game.Client
```

Reference the chosen transport, serializer, and shared business contracts.
Generated projects supply their matching client and compiler configuration.

## Guides

- [Game client lifecycle](https://github.com/bruce48x/Lakona/blob/main/docs/session.md#game-client-lifecycle): connection states, automatic recovery, transport factories, and disposal.
- [Sessions and reliable push](https://github.com/bruce48x/Lakona/blob/main/docs/session.md): handshake, establishment, notifications, and recovery boundaries.
- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): generated facade configuration and contract bindings.
- [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md): provider setup and application-owned lifetime.
- [Integrate Game client](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-integrate-game-client/SKILL.md): agent workflow for connection UI, business login, recovery, and engine dispatch.

Use the compatible `.agents/skills/` copy in generated projects. The integration
skill includes a Unity presenter example; application callbacks and UI remain
owned by the engine's main-thread dispatch mechanism.
