# Lakona Monorepo Design

## Goal

Integrate the existing Lakona.Rpc, Lakona.Actor, and Lakona.Game codebases into this repository as one Lakona monorepo, while replacing the old public naming with the Lakona brand.

## Architecture

Lakona keeps the current conceptual layers but makes them live in one repository:

- `Lakona.Rpc.*` owns typed RPC contracts, runtime, serializers, transports, analyzers, and starter/scaffolding support.
- `Lakona.Actor` owns the process-local actor/mailbox runtime and source generator.
- `Lakona.Game.*` owns the higher-level game server framework, cluster adapters, hotfix runtime, client helpers, generators, samples, and tool integration.

The repository should use project references for internal development and retain separate NuGet packages for consumers.

## Naming

The migration replaces:

- `Lakona.Rpc.*` with `Lakona.Rpc.*`
- `Lakona.Actor` with `Lakona.Actor`
- `Lakona.Game.*` with `Lakona.Game.*`
- `Lakona.Game.Tool` with `Lakona.Tool`
- command names based on `lakona-starter` or `lakona` with Lakona-oriented command names

## Repository Layout

```text
src/
  Lakona.Rpc.*
  Lakona.Actor
  Lakona.Actor.SourceGenerator
  Lakona.Game.*
  Lakona.Tool
tests/
  Lakona.Rpc.*
  Lakona.Actor.*
  Lakona.Game.*
samples/
docs/
blog/
design/
scripts/
```

## Migration Policy

The migration is intentionally a breaking rename. It does not preserve old package IDs, namespaces, command names, or documentation branding as compatibility aliases in the first pass. Compatibility shims can be added later only if there is a concrete consumer migration need.

