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
Hub itself is self-contained, while project operations use a compatible .NET 10
SDK. Windows uses MSI, macOS uses DMG, and Linux uses native DEB and RPM
packages. Release artifacts contain only the Hub application so routine Hub
updates do not repeatedly transfer an SDK. Their version lifecycles remain
distinct:

- the Hub application updater updates Hub
- the SDK manager installs and switches verified SDK versions
- future project maintenance operations update Lakona project dependencies

At startup, the SDK manager first checks the Hub-managed SDK, then compatible
stable .NET 10 SDKs available through the system `dotnet` command and standard
platform installation locations. If neither exists, Hub explains the source
and destination and waits for explicit consent
before downloading the pinned SDK from Microsoft's official release service.
The archive is SHA-512 verified, extracted to a temporary directory, version
validated, and atomically activated in Hub's per-user application-data
directory. Hub does not modify the user's system `PATH`, require administrator
access, or install a global SDK.

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

The versioned Hub user-settings document persists imported projects and their
server-editor choices, display language, the project-creation draft, current
page, window placement, the last explicit update-check result, and a disposable
cache of automatically detected tools. Startup displays the tool cache
immediately and then refreshes it from the machine; manual tool registrations
remain authoritative in their separate settings document.

Moving or deleting a project may make a local index entry stale. Hub reports
that state and allows the user to locate or remove the entry; it does not write
an identity marker into the project to avoid it.

## V1 Product Scope

V1 includes:

- create a Lakona project through a guided form
- import and inspect an existing Lakona project without modifying it
- list locally registered projects
- manage display language and detected development tools from one settings page
- detect a Hub-managed or compatible system .NET 10 SDK and supported client editors
- restore, build, start, stop, and show bounded structured logs
- open the project or client editor
- update Hub and the Hub-managed SDK independently
- manually check for, download, verify, and install Hub updates

V1 does not include:

- game discovery, installation, launching, patching, or distribution
- accounts, commerce, publishing, or remote deployment
- automatic project migration
- modification of imported projects during inspection or registration

## Local Editor Discovery

Hub discovers Rider, Visual Studio, VS Code, Unity Hub, every available Unity
Editor installation, and Godot from their standard installation locations, the
user's `PATH`, and Windows application registration. Portable Godot folders at
the root of a fixed drive are also recognized. Settings shows every detected
installation rather than collapsing all versions of one editor into one row.

The development-tool list also accepts manually selected executables. Known
Unity, Godot, and IDE executables retain their project-launch behavior, while an
otherwise unknown executable is treated as a generic server IDE. Multiple
manual installations of the same kind are allowed and can be removed
independently. Manual registrations are stored only in Hub's per-user
application data, take precedence over identical automatic candidates, and
never change editor settings, the system environment, or project content.

Each project row keeps editor choice separate from the open action. The server
editor selector defaults to Rider, then Visual Studio, then VS Code, then a
manually added generic IDE, while remaining user-selectable. Console clients
reuse that selection and priority. Unity and Godot clients open with the best
detected or manually added editor matching the inspected client kind; Unity Hub
is environment information and is not used in place of a Unity Editor. Hub
disables the action when no compatible editor is available.

## Settings And Localization

Environment status is part of Settings rather than a separate navigation area.
The settings page owns the Hub display language, .NET SDK status, detected
editor summary, and an explicit editor re-detection action.

The desktop window is user-resizable even though Hub draws its own frame. Its
minimum supported size is 1000 by 800 logical pixels, and its last normal size,
position, and maximized state are restored on the next launch.

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
covering the three desktop operating-system families. Every artifact is a
self-contained NativeAOT Hub application and does not include the .NET SDK.
NativeAOT publishing runs on a matching Windows, Linux, or macOS runner; cross-
operating-system publishing is not a supported release path. Linux support is
limited to glibc-based Debian and RPM distribution families on x64.

Each Release contains one x64 MSI for Windows, x64 and arm64 DMGs for macOS,
one x64 DEB, one x64 RPM, and a `lakona-hub-manifest.json`. Linux asset names
include `linux-x64` so their target platform remains visible outside package
manager metadata.

Hub updates are installer-based on all platforms. After downloading and
verifying the matching MSI, DMG, DEB, or RPM, Hub opens it through the operating
system. Windows Installer owns files under Program Files, macOS users install
the app from the mounted DMG, and Linux package managers own files under
`/usr/lib/lakona-hub`. Hub does not overwrite package-managed application files,
request elevation itself, or bypass the platform installer.

Update checking is explicit: Settings contains a **Check for updates** action
and Hub does not poll in the background. The update module exposes only check
and install operations to the window. GitHub discovery, platform and Linux
package-family selection, semantic-version comparison, delta selection,
downloading, verification, staging, replacement, package-installer launch, and
rollback remain behind that interface.

Before opening an installer, Hub verifies its length and SHA-256 against the
Release manifest. A failed download or verification leaves the installed Hub
unchanged.

Installer downloads stream directly to the staged package and report actual
received bytes against the manifest size. Settings shows a determinate progress
bar with percentage and transferred size while downloading, followed by
explicit verification and installer-launch states.

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

Downloaded SDK archives and application updates must be retrieved over HTTPS
and verified before activation. SDK asset URLs and SHA-512 digests come from
Microsoft's official .NET release metadata. A failed SDK or Hub update keeps
the previous version usable.

## V1 Validation Budgets

Release validation must measure, rather than assume:

- cold and warm startup time
- idle private working set
- application payload size and on-demand SDK download size separately
- bounded memory while streaming long build output
- cancellation and cleanup of child processes
- successful startup on a machine without a globally installed .NET SDK

Every release treats trim and AOT analysis warnings as errors. After each native
publish, CI starts the produced executable on the matching operating system and
runs the built-in `--aot-smoke-test`. That path initializes Avalonia and the main
window's compiled XAML, and exercises all three localization resources without
requiring a system SDK or network connection. SDK manager tests independently
cover system discovery, official metadata selection, streamed progress,
integrity verification, version validation, and atomic activation. Release
packaging tests and the repository release guards remain mandatory in addition
to this executable smoke test; suppressing an unexplained trim or AOT warning
is not an acceptable way to make a release pass.
