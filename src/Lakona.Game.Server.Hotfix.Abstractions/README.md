# Lakona.Game.Server.Hotfix.Abstractions

Stable attributes and result contracts for Lakona.Game server hotfix systems.

This package is intentionally small so stable model projects, hotfix projects, runtime packages, and source generators can share the same metadata without depending on Lakona.Game server hosting internals.

## Contracts

- `[HotfixState]` marks stable partial state types that can receive generated friend accessors.
- `[HotfixSystemOf]` binds a static hotfix system class to the stable state type it extends.
- `[FriendOf]` declares that a hotfix system is intended to use generated friend accessors for a stable state type.
- `HotfixMethodKey`, `HotfixSnapshot`, and `HotfixReloadResult` describe loaded method identity and reload outcomes.

`[FriendOf]` is metadata for the hotfix model and tooling. It is not an access-control mechanism; generated accessors are normal public members on the stable type in the first implementation.

Keep actor identity, serialized state, persistence schema, RPC contracts, and transport contracts outside the hotfix assembly.
