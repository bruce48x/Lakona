# Lakona Server Layout Heuristics

## Hard Lakona Boundaries

These constraints follow runtime ownership rather than directory taste:

- Shared contracts own wire DTOs, RPC service and notification contracts, and
  stable numeric IDs. Keep server runtime dependencies out of Shared.
- `Server.App` owns stable process resources, application modules, stable actor
  state shells, stable HTTP contracts, and types that must survive a Hotfix
  generation change.
- `Server.Hotfix` owns reloadable product behavior: RPC handlers, HTTP
  handlers, actor behavior, timer callbacks, and lifecycle policy.
- Generated code remains generator-owned. Change its source contract or
  generator rather than editing generated output.
- Project references must keep the stable-to-reloadable dependency direction;
  stable assemblies cannot depend on Hotfix implementation types.

A different folder name is valid when these ownership and dependency rules
still hold.

## Soft Heuristics

Evaluate these as signals, not laws:

- Prefer folders named for a business capability when one change routinely
  touches contracts, state, handlers, persistence, and tests for that
  capability.
- Keep a technical folder when its contents share one lifecycle, abstraction,
  or maintenance reason.
- Align namespaces with paths when the repository already uses that convention.
- Keep sibling concepts at comparable granularity. A single tiny type does not
  need a new folder merely to make the tree symmetric.
- Optimize for the next likely change, not for a complete taxonomy.

An optional feature-first arrangement can look like:

```text
Server/
├── App/
│   ├── Users/
│   ├── Rooms/
│   └── Operations/
└── Hotfix/
    ├── Users/
    ├── Rooms/
    └── Operations/
```

This is an example of cross-assembly domain locality, not a required tree.
Layer-first, hybrid, or project-specific structures remain sound when they
communicate ownership and keep changes coherent.

## Smell Catalogue

Investigate rather than automatically rewrite:

- A generic folder accumulates unrelated reasons to change.
- One feature edit requires navigating many orthogonal folders with no useful
  ownership boundary.
- A path implies stable state while the type is reloadable behavior, or the
  reverse.
- Namespaces and paths diverge enough to make discovery or source scans
  unreliable.
- A source file combines contract, orchestration, persistence, and unrelated
  policy.
- Tests, configuration, reflection, or documentation depend on obsolete fully
  qualified names or literal paths.
- Empty folders, forwarding shells, or one-type abstractions remain after a
  move without serving compatibility.

## Choosing Among Valid Layouts

When more than one layout is sound, compare:

1. Which option matches the project's domain vocabulary?
2. Which option makes the most frequent changes local?
3. Which option makes stable versus reloadable ownership obvious?
4. Which option minimizes namespace churn and compatibility risk?
5. Which option does the user prefer after seeing the tradeoff?

Use the answer as a recommendation, not as a universal Lakona convention.
