# Lakona.Game.Server.Generators

Source generators for typed Lakona.Game server actor access.

The generator emits one `Lakona.Game.Server.Generated.ActorAccess` root with
constrained `Route<TActor>`, `Local<TActor>`, and `Place<TActor>` selectors. It
does not emit one plural collection class per actor. Stable actor instance
methods are passed with typed lambdas; cluster handlers remain generated
runtime plumbing.
