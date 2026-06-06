# Lakona Monorepo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the existing Lakona.Rpc, Lakona.Actor, and Lakona.Game code into this repository and rename the public project surface to Lakona.

**Architecture:** Keep the current layered architecture, but colocate all projects in one repository. Internal references should become `ProjectReference` where practical, while package IDs, root namespaces, project directories, and docs use Lakona naming.

**Tech Stack:** .NET 10, C# projects, Roslyn source generators, NuGet packages, PowerShell migration commands.

---

### Task 1: Import Source Trees

**Files:**
- Create: `src/`, `tests/`, `samples/`, `docs/`, `blog/`, `design/`, `scripts/`
- Modify: `.gitignore`

- [ ] Copy useful source, test, sample, docs, blog, design, and scripts files from the three source repositories.
- [ ] Exclude `.git`, `bin`, `obj`, `.tmp`, `temp`, `_verify`, `_verify_fresh`, generated package artifacts, and repository-local build outputs.
- [ ] Keep the source repositories read-only.

### Task 2: Rename Projects And Directories

**Files:**
- Modify: `src/**/*.csproj`, `tests/**/*.csproj`, `samples/**/*.csproj`

- [ ] Rename `Lakona.Rpc.*` project directories and files to `Lakona.Rpc.*`.
- [ ] Rename `Lakona.Actor` directories and files to `Lakona.Actor`.
- [ ] Rename `Lakona.Game.*` directories and files to `Lakona.Game.*`.
- [ ] Rename `Lakona.Game.Tool` to `Lakona.Tool`.

### Task 3: Replace Public Names

**Files:**
- Modify: `src/**/*.cs`, `tests/**/*.cs`, `samples/**/*.cs`, `docs/**/*.md`, `README.md`, package metadata

- [ ] Replace namespaces and using directives:
  - `Lakona.Rpc` -> `Lakona.Rpc`
  - `Lakona.Actor` -> `Lakona.Actor`
  - `Lakona.Game` -> `Lakona.Game`
- [ ] Replace package IDs and root namespaces with the same Lakona mapping.
- [ ] Replace tool command names with Lakona-oriented names.

### Task 4: Fix Project References

**Files:**
- Modify: `src/**/*.csproj`, `tests/**/*.csproj`, `samples/**/*.csproj`

- [ ] Convert internal `PackageReference` entries for migrated packages to `ProjectReference` entries.
- [ ] Update relative paths after directory renames.
- [ ] Keep third-party `PackageReference` entries unchanged.

### Task 5: Add Root Build Entry Points

**Files:**
- Create or modify: root solution/build files

- [ ] Add root-level build metadata suitable for all projects.
- [ ] Add a root solution or solution filter containing the migrated projects.
- [ ] Keep package versioning local to the migrated projects for the first pass.

### Task 6: Verify And Repair

**Files:**
- Modify: files reported by build and test failures

- [ ] Run a root build.
- [ ] Fix compile errors caused by rename fallout.
- [ ] Run representative test projects.
- [ ] Record any remaining failures that require deeper follow-up.

