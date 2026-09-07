# 6. Contract Evolution And Determinism

Distinguish obsolete compatibility shims from published contracts that must
remain stable. Inspect:

- RPC and notification IDs, wire formats, serialized member order, and error
  semantics
- stable state and type identity shared across Hotfix generations
- source-generator and project-renderer determinism
- startup and registration order that may depend on reflection, file order, or
  container enumeration
- Unity 2022 LTS, C# 9.0, IL2CPP, and ordinary .NET compatibility
- direct and transitive package versions and their single source of truth
- configuration defaults and upgrade behavior across supported topologies

The same supported input and configuration must produce the same generated
shape and runtime decisions. Report accidental nondeterminism even when one run
usually succeeds.
