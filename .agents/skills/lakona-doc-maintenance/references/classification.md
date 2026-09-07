## Classification

Classify every relevant doc before editing:

- **Current authority:** defines active workflow, package boundaries, runtime
  contracts, validation rules, or architecture.
- **Current supplement:** explains a current subsystem but is not an entry point.
- **User-facing:** belongs to `README.md`, package README files, samples, or blog.
- **Stale plan:** task list, implementation plan, roadmap, or phase document for
  work that has already landed.
- **History-only:** explains removed frameworks, migration mechanics, old package
  names, or old starter designs without current operational value.
- **Duplicate:** repeats content already covered by a clearer current authority.

Default actions:

- Keep current authority and current supplements.
- Apply the entrypoint's user-facing scope rule: repair links affected by the
  authorized cleanup, but rewrite user-facing content only when requested.
- Delete stale plans and history-only docs.
- Merge only durable current rules from duplicates into the active authority,
  then delete the duplicate.

## Lakona-Specific Rules

- `CONTRIBUTING.md` should be a concise contributor entry point, not a full
  architecture manual.
- `CONTRIBUTING.md` documentation maps should link only current authoritative
  docs.
- Do not preserve removed framework branding, old package names, or migration
  history in current docs unless there is an active compatibility reason.
- Do not create a new archive bucket just to save old decisions.
- If a historical decision still matters, rewrite it as a current rule in the
  relevant authority document.
- Move valuable content from `docs/superpowers/**` into permanent `docs/**`
  documentation, then clean up `docs/superpowers/**`.
- Remove empty archive directories after deleting their contents.
