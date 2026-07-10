# Suppress Host Shutdown Prompts

## Goal

Keep Lakona server startup logs concise by removing repeated interactive
shutdown instructions while preserving useful framework readiness information.

## Behavior

- RPC listener readiness logs use exactly
  `RPC server listening on {ListenAddress}.` and do not include
  `Press Ctrl+C to stop`.
- `LakonaGameServer` suppresses the complete set of status messages emitted by
  the .NET Generic Host console lifetime, including
  `Press Ctrl+C to shut down`, `Application started`, the hosting environment,
  and the content root.
- Lakona-owned output remains unchanged: the banner, listener readiness logs,
  and `Lakona server started successfully. NodeId={NodeId}.` are retained.
- Standalone consumers of `RpcServerHost` keep their existing Ctrl+C handling;
  only the redundant log wording changes.

## Design

Use the official `ConsoleLifetimeOptions.SuppressStatusMessages` option in the
Lakona game-server host setup. This expresses the intended ownership boundary
directly and avoids category-level filtering or a custom `IHostLifetime`.

Change the RPC listener log template at its source instead of filtering it at
the game-server layer, so every listener reports only its address and repeated
end-user instructions cannot reappear for multi-listener configurations.

## Testing

- Add a focused RPC host logging test that starts a real test acceptor and
  verifies the listener message contains the address but no shutdown prompt.
- Add a game-server hosting test that verifies console lifetime status messages
  are suppressed by default.
- Run the affected RPC and game-server test projects plus the package-version
  graph guard.

## Package Impact

The runtime changes affect `Lakona.Rpc.Server` and `Lakona.Game.Server`; both
package versions must receive patch bumps. Any additional dependency-closure
bumps are determined by the repository package-version graph guard.
