# Server Pack Command Plan Questions - Resolved

Status: resolved
Date: 2026-06-23

This file records answers to implementation questions raised against
`docs/tool/server-pack-command-implementation-plan.md`. The answers have been
folded back into `docs/tool/server-pack-command.md` and
`docs/tool/server-pack-command-implementation-plan.md`.

## 1. Validator Scope

Decision: implement the broader design.

`ServerPackageValidator` must verify installed hotfix checksums and must reject
`bin` or `obj` directories anywhere under the staged app root.

Implementation plan change:

- Extract `HotfixPackageVerifier` from `HotfixPackageInstaller`.
- Make both hotfix install and server package validation use that shared
  checksum verifier.
- Add validator tests for checksum mismatch and `bin` / `obj` rejection.

## 2. `builtAtUtc` Format

Decision: canonical server manifest timestamps use UTC `Z` text.

`lakona-server.json` must write `builtAtUtc` like:

```json
"builtAtUtc": "2026-06-23T15:30:45Z"
```

Implementation plan change:

- Add `UtcDateTimeOffsetJsonConverter` to `ServerJson.Options`.
- Update the manifest serialization test to expect the `Z` suffix.

## 3. BuildTag With Custom Paths

Decision: allow custom `--project` and `--hotfix-project` paths, but fail when
their BuildTags differ.

`server pack` must not rewrite or override the hotfix manifest BuildTag. A
mismatch means the hotfix package was not built against the stable app boundary
being packaged.

Implementation plan change:

- Pass the stable app BuildTag into `ServerPackageWriteRequest`.
- Write `lakona-server.json` from the app BuildTag.
- Let validation fail when the installed hotfix manifest BuildTag differs.
- Add a writer test for BuildTag mismatch.
