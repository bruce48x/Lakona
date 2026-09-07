# 2. Micro Code Smells

Review every maintained handwritten source, project, test, workflow, and script
file in the inventory. Inspect neighboring callers and tests when a smell
depends on usage. Look for small but real friction, including:

- strange branches, impossible states, ineffective guards, and silent fallback
- unused state, parameters, abstractions, options, and configuration
- one-line forwarding types, single-use helpers, and test-only production seams
- duplicate registration, conversion, validation, or cleanup logic
- stringly dispatch or reflection where typed information already exists
- nullable or boolean flags that hide lifecycle states
- unnecessary public surface and friend declarations
- dependencies constructed or resolved in surprising places
- ownership split between a method and its callers
- cancellation, disposal, async, and error handling that is correct only by
  convention
- names or indirection that force maintainers to jump between files to
  understand one concept

Do not report formatting preferences or generic style advice. A micro finding
must identify a concrete maintenance cost, failure risk, misleading contract,
or unnecessary concept. Keep its local scope visible instead of inflating it
into a broad architecture claim.

A documented repository standard is never generic style. When a finding is a
breach of a rule in `CONTRIBUTING.md`, `docs/contributing/engineering.md`, or
`docs/contributing/testing.md`, report it under Pass 8 with the rule cited.
Pass 2 reports design judgement; rule conformance belongs to Pass 8.
