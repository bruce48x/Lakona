# Lakona.Rpc.Analyzers

Internal Roslyn analyzers and source generators embedded in `Lakona.Rpc.Core`.
This is an implementation project, not a separately published package.

Consumers reference their runtime owner packages, which deliver the matching
compiler extension transitively.
Consumers must not reference `Lakona.Rpc.Analyzers` directly.

## Guides

- [Source generation](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/source-generation.md): attributes, generation settings, Unity defaults, and generated namespaces.
- [Public API boundaries](https://github.com/bruce48x/Lakona/blob/main/docs/rpc/public-api-boundaries.md): package ownership and supported integration surfaces.
- [Define RPC contract](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-define-rpc-contract/SKILL.md): contract authoring workflow.
