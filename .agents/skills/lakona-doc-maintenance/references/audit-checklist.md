## Complete Quality Audit Checklist

For a full documentation audit, complete all six passes independently. Do not
stop after finding one class of problem. Record each pass as **clear**,
**findings**, or **not applicable**, and cite the files, implementation, tests,
or generated output used as evidence. For a reduced-scope audit, run every pass
that can be affected by the scoped change; do not mark a pass not applicable
without checking.

1. **Factual and semantic consistency**
   - Compare claims across authoritative docs and against the implementation,
     tests, configuration, and generated output when those are the real
     evidence.
   - Verify mutable facts such as defaults, public API signatures, project
     layout, ownership, failure behavior, delivery guarantees, and runtime
     contracts.
   - Treat a consistently repeated claim as unverified until it agrees with the
     system it describes.

2. **Ownership and boundary clarity**
   - Ensure each cross-cutting contract has one clearly named authoritative
     owner and that related docs use the same boundary.
   - Check commonly confused boundaries explicitly: configuration versus
     runtime behavior, stable App code versus Hotfix code, Application HTTP
     versus Management or RPC surfaces, and cluster-wide versus endpoint-local
     concerns.
   - Resolve vague, overlapping, or contradictory ownership statements in the
     relevant authority document.

3. **Stale plans and history on the current documentation path**
   - Find completed plans, roadmaps, phase checklists, migration instructions,
     implementation diaries, resolution summaries, and superseded decisions.
   - Rewrite any still-valid rule as a present-tense contract in the relevant
     authority document.
   - Delete the remaining history-only artifact instead of moving it to an
     archive.

4. **Duplication and update fan-out**
   - Search for mutable facts copied across multiple docs, especially complete
     configuration blocks, exact API signatures, defaults, project trees, and
     generated examples.
   - Keep one canonical definition. Replace other copies with the minimum
     context needed by their readers plus a link to the authority.
   - Preserve useful explanation and deliberate terminology repetition; remove
     duplication only when it creates competing maintenance surfaces.

5. **Competing authority mechanisms**
   - Treat the documentation map in `CONTRIBUTING.md` as the sole registry of
     authoritative contributor documentation.
   - Confirm every mapped target exists and every document treated as a current
     authority is represented in that map.
   - Remove document-local currentness metadata such as `Status`, `Date`,
     `Audience`, or `Last reviewed`, and remove self-declared authority labels
     that compete with the map.
   - When implementation changes architecture, configuration, public APIs,
     generated output, or runtime contracts, confirm the same change updates
     every affected authoritative document.

6. **Historical and transitional wording**
   - Review terms such as `legacy`, `old`, `removed`, `historical`, `migration`,
     `first implementation`, `current iteration`, and future-phase language.
   - Replace historical narration with the current positive contract, current
     non-goal, or current constraint.
   - Preserve wording that has present operational meaning, including protocol
     versions, active compatibility guarantees, release history, and the
     current App/Hotfix generation distinction.
   - Remove completed-resolution tables and before/after narratives when they
     no longer guide current work.
