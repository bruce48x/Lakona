# 1. Macro Architecture

Map modules, interfaces, seams, adapters, package dependencies, ownership, and
lifetimes. Look for:

- duplicate models, owners, runtime graphs, or sources of truth
- shallow modules and pass-through interfaces
- seams with only one real adapter
- package or assembly splits without independent ownership
- hidden fallback providers, ambient state, global replacement, and friend
  access
- scattered startup, shutdown, rollback, cancellation, or disposal ownership
- public compatibility surfaces without active behavior
- extension points created speculatively
- documentation that describes a cleaner architecture than the code implements

Apply the deletion test: if deleting a module removes complexity instead of
concentrating it behind a smaller interface, treat it as suspect.
