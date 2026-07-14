# Lakona Hub

Status: V1 architecture authority
Date: 2026-07-14
Audience: maintainers and contributors

## Purpose

Lakona Hub is a lightweight desktop application for people who should not need
to install or learn the .NET CLI before creating and running a Lakona project.
It provides guided project creation and local project management while keeping
every generated project usable without Hub.

V1 is a project tool. A game catalog, game installer, account system, store,
and distribution platform are future product possibilities and are not part of
the V1 architecture. Do not add placeholder navigation, storage, protocols, or
modules for them.

## Technology Decision

The desktop adapter uses Avalonia on .NET 10. Release builds should be evaluated
with NativeAOT once the complete V1 dependency set is known. Hub invokes a
private, portable .NET 10 SDK for project operations and must not require a
machine-wide .NET installation.

The SDK lifecycle is distinct from the Hub application lifecycle:

- the Hub application updater updates Hub
- the SDK manager installs and switches verified SDK versions
- future project maintenance operations update Lakona project dependencies

The private SDK is invoked by absolute executable path with a process-local
environment. Hub does not modify the user's system `PATH` or install a global
SDK.

## Module Shape

```txt
Lakona.ProjectSystem
  inspect project
  plan project creation
  plan future maintenance
  execute validated plans

Lakona.Tool
  command-line adapter

Lakona.Hub
  Avalonia desktop adapter
  local project index
  SDK and process supervision
```

`Lakona.ProjectSystem` is a deep module. Its interface accepts user intent and
returns typed descriptions, plans, progress, and results. It does not expose
renderers, XML mutation details, process implementations, or UI concepts.

Project inspection is read-only. Generation retains the existing canonical
pipeline:

```txt
project specification -> validated generation plan -> transactional write
```

Hub and Tool both call `LakonaProjectCreator`; the complete pipeline now lives
behind the ProjectSystem seam. Hub must not call `lakona-tool` as its permanent
implementation, and neither adapter may create a second generator.

## Non-Invasive Project Contract

A Lakona project is the authoritative source of project facts. Hub must not add
`.lakona/project.json`, another hidden project directory, or entries in the
project's `.gitignore` for its own bookkeeping.

Project inspection derives facts from existing project content, including:

- the Shared, Server App, and Server Hotfix project shape
- literal Lakona package references
- Unity or Tuanjie `ProjectVersion.txt`
- Godot `project.godot`
- the Console client project shape

Inspection must not evaluate MSBuild, load project assemblies, restore
packages, or execute project code.

Hub-owned data lives in the operating system's per-user application-data
directory. It may contain project paths, display preferences, recent activity,
and disposable inspection/build caches. Hub-owned data is never required to
build a project and can be deleted without damaging one.

Moving or deleting a project may make a local index entry stale. Hub reports
that state and allows the user to locate or remove the entry; it does not write
an identity marker into the project to avoid it.

## V1 Product Scope

V1 includes:

- create a Lakona project through a guided form
- import and inspect an existing Lakona project without modifying it
- list locally registered projects
- detect the private .NET SDK and supported client editors
- restore, build, start, stop, and show bounded structured logs
- open the project or client editor
- update Hub and the private SDK independently

V1 does not include:

- game discovery, installation, launching, patching, or distribution
- accounts, commerce, publishing, or remote deployment
- automatic project migration
- modification of imported projects during inspection or registration

## Local Editor Discovery

Hub discovers Rider, Visual Studio, VS Code, Unity, and Godot from their
standard installation locations, the user's `PATH`, and Windows application
registration. Portable Godot folders at the root of a fixed drive are also
recognized. Discovery is read-only and does not change editor settings or the
system environment.

Each project row keeps editor choice separate from the open action. The server
editor selector defaults to Rider, then Visual Studio, then VS Code, while
remaining user-selectable. Console clients reuse that selection and priority.
Unity and Godot clients open with a detected editor matching the inspected
client kind; Hub disables the action when no compatible editor is available.

## Guided Project Creation

The creation experience is one page rather than a multi-step wizard. It shows
project name, output location, client and engine version, transport, serializer,
persistence, NuGetForUnity source, and deployment profile at the same time.
Options are never hidden behind an advanced-settings disclosure. Fields that do
not apply to the selected client remain visible but disabled with an explanation.

The form defaults must match the canonical generator: Unity 2022, KCP,
MemoryPack, no persistence, embedded NuGetForUnity packages, and no deployment
profile. Changing the client immediately updates its supported version choices.
The final project path and validation result remain visible before creation.

Future project maintenance begins with a read-only analysis and a visible
change plan. Applying a plan requires explicit confirmation, transactional
write behavior, backup or rollback, and post-change validation. When the
project shape is ambiguous, Hub refuses automatic maintenance instead of
guessing.

## Security Contract

Opening and inspecting files is distinct from executing project operations.
An imported project may contain arbitrary MSBuild targets. Hub must obtain an
explicit user action before restore, build, or run; those operations execute as
the current non-elevated user and stream their exact command and output.

Downloaded SDK archives and application updates must be authenticated and
verified before activation. A failed SDK or Hub update keeps the previous
version usable.

## V1 Validation Budgets

Release validation must measure, rather than assume:

- cold and warm startup time
- idle private working set
- application payload size separately from the private SDK
- bounded memory while streaming long build output
- cancellation and cleanup of child processes
- successful startup on a machine without a globally installed .NET SDK

NativeAOT is enabled only after the complete desktop flow passes trim and AOT
analysis without suppressing unexplained warnings.
