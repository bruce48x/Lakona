# Lakona Monorepo

Lakona is maintained as one monorepo that contains the RPC runtime, actor
runtime, and game-server framework.

## Goals

The monorepo keeps related framework layers together so cross-layer changes can
be developed, tested, and reviewed in one place.

The repository uses the Lakona public naming surface:

- `Lakona.Rpc.*` owns typed RPC contracts, runtime, serializers, transports,
  analyzers, and starter support.
- `Lakona.Actor` owns the process-local actor/mailbox runtime and source
  generator.
- `Lakona.Game.*` owns game-server hosting, cluster adapters, hotfix runtime,
  client helpers, generators, and samples.
- `Lakona.Tool` owns project scaffolding and maintenance commands.

## Development Model

Internal repository development should prefer `ProjectReference` links between
Lakona packages. External consumers still consume separate NuGet packages.

This keeps package boundaries visible while avoiding the friction of developing
interdependent layers across multiple repositories.

## Migration Policy

The initial monorepo migration is an intentional breaking rename to the Lakona
brand. The repository does not preserve old package ids, namespaces, command
names, or documentation branding as compatibility aliases by default.

Compatibility shims should be added only when there is a concrete consumer
migration need.

## Repository Entry Points

- Root solution: `Lakona.sln`
- Source packages: `src/**`
- Tests: `tests/**`
- Samples: `samples/**`
- Durable documentation: `docs/**`
- Blog article sources: `blog/**`

Build and test from the repository root:

```powershell
dotnet build Lakona.sln
dotnet test Lakona.sln --no-build
```
