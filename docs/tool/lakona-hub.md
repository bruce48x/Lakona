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

The desktop adapter uses Avalonia on .NET 10 and release builds use NativeAOT.
Hub invokes a private, portable .NET 10 SDK for project operations and must not
require a machine-wide .NET installation.

Release artifacts contain both the self-contained Hub runtime and the pinned
private .NET 10 SDK so a first-time user installs only one artifact. Windows
and macOS use portable archives; Linux uses native DEB and RPM packages. Their
version lifecycles remain distinct:

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
- manage display language and detected development tools from one settings page
- detect the private .NET SDK and supported client editors
- restore, build, start, stop, and show bounded structured logs
- open the project or client editor
- update Hub and the private SDK independently
- manually check for, download, verify, and install Hub updates

V1 does not include:

- game discovery, installation, launching, patching, or distribution
- accounts, commerce, publishing, or remote deployment
- automatic project migration
- modification of imported projects during inspection or registration

## Local Editor Discovery

Hub discovers Rider, Visual Studio, VS Code, Unity, and Godot from their
standard installation locations, the user's `PATH`, and Windows application
registration. Portable Godot folders at the root of a fixed drive are also
recognized. Settings shows one row per supported tool with its executable path.
Each row can open a file picker at the detected executable's directory, or at
the user's home directory when the tool was not detected, so the user can
select an executable manually. Manual paths are stored only in Hub's per-user
application data, take precedence over automatic candidates, and never change
editor settings, the system environment, or project content.

Each project row keeps editor choice separate from the open action. The server
editor selector defaults to Rider, then Visual Studio, then VS Code, while
remaining user-selectable. Console clients reuse that selection and priority.
Unity and Godot clients open with a detected editor matching the inspected
client kind; Hub disables the action when no compatible editor is available.

## Settings And Localization

Environment status is part of Settings rather than a separate navigation area.
The settings page owns the Hub display language, bundled .NET status, detected
editor summary, and an explicit editor re-detection action.

Hub supports Simplified Chinese, Traditional Chinese, and English. It follows
the same culture detection as Lakona.Tool: `zh-Hant`, `zh-TW`, `zh-HK`,
`zh-MO`, and `zh-CHT` select Traditional Chinese; other Chinese cultures select
Simplified Chinese; all other cultures select English. The user can switch
among all three languages immediately without changing the operating-system
language. The manual `HubLocalization.SetLanguage` seam is also the test
contract: UI model tests must select a language explicitly instead of relying
on the machine's current culture.

## Release And Update Contract

`src/Lakona.Hub/Lakona.Hub.csproj` is the single source of truth for the Hub
release version. A relevant change merged to `main` runs Hub tests, validates
that the version has not already been published, builds the release, creates
the matching `hub-v<semver>` tag, and publishes the GitHub Release. The manual
workflow trigger takes no version input and exists only to retry an unpublished
version.

Relevant release inputs are Hub source, Lakona.ProjectSystem source, Hub
packaging scripts and workflow, and repository-level .NET build inputs. The Hub
version guard requires `<Version>` to change whenever one of these inputs
changes. Existing tags and Releases are immutable release boundaries: never
replace their assets with a different build.

The release targets Windows x64, Linux x64, macOS x64, and macOS arm64,
covering the three desktop operating-system families. Every artifact is
self-contained NativeAOT and includes the pinned private .NET 10 SDK. NativeAOT
publishing runs on a matching Windows, Linux, or macOS runner; cross-operating-
system publishing is not a supported release path. Linux support is limited to
glibc-based Debian and RPM distribution families on x64.

Each Release contains full ZIPs for Windows and macOS, one `amd64` DEB, one
`x86_64` RPM, and a `lakona-hub-manifest.json`. From the second Release onward,
the workflow compares each new portable package with the preceding stable Hub
Release and emits a file-level delta ZIP containing only changed files, a
deletion list, and the new package manifest. Windows and macOS use a delta only
when its `fromVersion` exactly matches the installed version and automatically
fall back to the full package otherwise. Linux packages never use file-level
deltas because their installed files are owned by the system package manager.

Windows and macOS self-update requires the portable application directory to
be writable by the current user. Hub checks this before it exits; if the
location is not writable, it reports the problem and leaves the running
installation unchanged. On Linux, Hub identifies the DEB or RPM family from
`/etc/os-release`, downloads and verifies the matching full package, then opens
it through the desktop system installer. Hub does not overwrite files under
`/usr/lib/lakona-hub`, request elevation itself, or bypass `dpkg` or RPM package
ownership. A legacy portable Linux installation must be replaced manually by
the first DEB or RPM installation.

Update checking is explicit: Settings contains a **Check for updates** action
and Hub does not poll in the background. The update module exposes only check
and install operations to the window. GitHub discovery, platform and Linux
package-family selection, semantic-version comparison, delta selection,
downloading, verification, staging, replacement, package-installer launch, and
rollback remain behind that interface.

Before activation, Hub verifies the downloaded archive length and SHA-256 from
the Release manifest. The external updater then constructs a complete staged
installation and verifies every target file against the package manifest. It
does not modify the live installation until validation succeeds. The running
Hub exits, the updater swaps the validated directory into place, and a failed
swap or restart restores the previous directory. The previous directory is
deleted only after the updated Hub reaches normal application initialization.

Release assets and manifests are retrieved over authenticated HTTPS from the
GitHub API and GitHub Releases. Repository release permissions are restricted
to the publishing job; the desktop client never receives a GitHub token.

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

Every release treats trim and AOT analysis warnings as errors. After each native
publish, CI starts the produced executable on the matching operating system and
runs the built-in `--aot-smoke-test`. That path initializes Avalonia and the main
window's compiled XAML, exercises all three localization resources, starts the
bundled SDK, and verifies its exact pinned version. Release packaging tests and
the repository release guards remain mandatory in addition to this executable
smoke test; suppressing an unexplained trim or AOT warning is not an acceptable
way to make a release pass.
