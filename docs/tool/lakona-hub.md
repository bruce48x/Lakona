# Lakona Hub

## Purpose

Lakona Hub is a lightweight desktop application for people who should not need
to install or learn the .NET CLI before creating and managing a Lakona project.
It provides guided project creation, local project registration, packaging,
and development-tool management while keeping every generated project usable
without Hub.

Hub V1 is a project tool. It does not provide a game catalog, game installer,
account system, store, or distribution platform. Do not add placeholder
navigation, storage, protocols, or modules for those capabilities.

Hub runs as one desktop instance per user. Launching Hub while an existing
instance is running sends an activation request to that instance and exits the
new process; the existing window is restored from minimized state and focused.
The instance lock and activation channel are local operating-system resources,
and they do not add files to a Lakona project.

## Technology Decision

The desktop adapter uses Avalonia on .NET 10 and release builds use NativeAOT.
Hub itself is self-contained, while project packaging uses a compatible .NET 10
SDK. Windows uses MSI, macOS uses DMG, and Linux uses native DEB and RPM
packages. Release artifacts contain only the Hub application so routine Hub
updates do not repeatedly transfer an SDK. Their version lifecycles remain
distinct:

- the Hub application updater updates Hub
- the SDK manager installs and switches verified SDK versions

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
  package deployable servers and Hotfix versions
  execute validated plans

Lakona.Tool
  command-line adapter

Lakona.Hub
  Avalonia desktop adapter
  local project index
  SDK and editor management
```

`Lakona.ProjectSystem` is a deep module. Its interface accepts user intent and
returns typed descriptions, plans, progress, and results. It does not expose
renderers, XML mutation details, process implementations, or UI concepts.

Project inspection is read-only. Generation retains the existing canonical
pipeline:

```txt
project specification -> validated generation plan -> transactional write
```

Hub and Tool both depend on `ILakonaProjectCreator` and use
`LakonaProjectCreator`; the complete pipeline now lives behind the ProjectSystem
seam. Hub must not call `lakona-tool` as its permanent
implementation, and neither adapter may create a second generator.

Within Hub, `MainWindow` is the Avalonia composition, navigation, overlay, and
window-lifetime adapter. It composes focused stateful workflows instead of
mirroring their asynchronous state:

- `HubEnvironmentWorkflow` owns local application detection and registration,
  server-editor selection, and .NET SDK inspection and installation.
- `HubUpdateWorkflow` owns update-check freshness, activation checks,
  installation progress, and the persisted update snapshot.

Project creation and packaging retain their existing focused form modules.
Hub should deepen those experience boundaries when they gain new policy rather
than introducing one universal window view model or speculative workflow
interfaces with only one implementation.

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

New projects use `Shared/`, `Server/`, and `Client/` as their project roots.
Inspection prioritizes those names, then scans only the repository's immediate
subdirectories for the same Shared, Server, and supported-client structure.
This read-only fallback supports renamed roots without assuming a fixed root
name.

Inspection must not evaluate MSBuild, load project assemblies, restore
packages, or execute project code.

Hub-owned data lives in the operating system's per-user application-data
directory. It may contain project paths, display preferences, recent activity,
and disposable development-tool detection caches. Hub-owned data is never
required to build a project and can be deleted without damaging one.

The versioned Hub user-settings document persists imported projects, the
global server-editor choice, display language, the project-creation draft,
current page, window placement, the last explicit update-check result, and a
disposable cache of automatically detected tools. Startup displays the tool
cache immediately and then refreshes it from the machine; manual tool
registrations remain authoritative in their separate settings document.

Hub also keeps a small crash-recovery document and active-session marker in its
per-user application-data directory. Fatal UI or process exceptions record the
Hub version, operating-system description, process architecture, last known
operation, exception message, and bounded exception stack trace. User-profile,
application-data, and temporary-directory paths are replaced with placeholders
before the report is written. A normal shutdown removes the session marker.

On the next launch after a recorded crash, Hub asks whether the user wants to
report the problem only when the persisted report contains a non-empty exception
stack trace. Reports without diagnostic frames and incomplete-session markers
alone never trigger the feedback prompt. Confirmation opens a standardized,
prefilled GitHub Issue in the default browser; Hub never stores a GitHub token,
and the user can review, edit, or abandon the report before GitHub submission.

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
- package self-contained server releases and standalone Hotfix versions for a
  selected RID and build configuration
- open the project folder, server editor, or client editor
- include the project-compatible Agent Skill Pack in every newly created
  project
- update Hub and the Hub-managed SDK independently
- manually check for, download, verify, and install Hub updates

V1 does not include:

- game discovery, installation, launching, patching, or distribution
- accounts, commerce, publishing, or remote deployment
- general project restore, build, start, stop, or log supervision
- automatic project migration
- Agent Skill installation into, updating of, or deletion from existing
  projects
- modification of imported projects during inspection or registration

## Project Packaging

The project row places `Package` beside `Open server`. Packaging opens one
bounded operation dialog rather than adding deployment state to the project
index. The operator selects:

- a complete server package or standalone Hotfix package;
- `Release` or `Debug`;
- `linux-x64`, `linux-arm64`, `win-x64`, or `win-arm64` for complete server
  packages.

Hotfix packages do not select a runtime because they contain managed application
behavior for the stable server BuildTag. The dialog displays the BuildTag
inspected from `Server/BuildTag.props` as read-only metadata and never asks for
a package version. The artifact output directory defaults to `Server/Build` and
can be edited directly or selected with the platform folder picker before the
operation starts. Hub uses the exact compatible .NET 10 SDK executable selected
by its SDK manager, including a Hub-managed SDK that is not on the system
`PATH`.

Packaging behavior belongs to the public `ILakonaProjectPackager` boundary in
`Lakona.ProjectSystem`. Hub and Tool call that same boundary; Hub must not spawn
`lakona-tool`, and neither adapter may maintain its own publish, manifest,
checksum, or archive implementation. Progress is bounded to the active dialog,
supports cancellation, and exposes the completed artifact path without adding
Hub-owned files to the project. Packaging child processes run without creating
a console window so focus remains in Hub; the same ProjectSystem process runner
keeps `lakona-tool` from opening a second console window during packaging.

Packaging remains local. Uploading artifacts, rendering production
configuration, and coordinating multi-node activation remain external
operations workflows and are not remote-deployment features of Hub V1.
Artifact identity and operational behavior are defined by
[Packaging and Deployment](../deployment.md).
After a package completes successfully, Hub opens the artifact's containing
folder automatically while keeping the completed artifact path visible in the
packaging dialog.

Packaging failures keep the dialog actions visible and show only a concise,
single-line summary in the dialog. Hub decodes redirected `dotnet` output as
UTF-8 and writes the complete exception and build output to a UTF-8 log under
its per-user application-data directory. The failure state exposes an explicit
action to open that log's containing folder. Hub retains at most the latest 20
packaging failure logs so diagnostics remain bounded.

## Local Editor Discovery

Hub discovers Rider, Visual Studio, VS Code, Unity Hub, every available Unity
Editor installation, Tuanjie Hub, every available Tuanjie Editor installation,
and Godot from their standard installation locations, the user's `PATH`, and
Windows application registration. Portable Godot folders at the root of a
fixed drive are also recognized. Settings shows every detected installation
rather than collapsing all versions of one editor into one row. Tuanjie entries
are omitted from the default empty list and appear only when detected or
manually added. Tuanjie installation folder names expose their upstream Unity
compatibility version; Hub resolves the user-facing Tuanjie product version
through Tuanjie Hub's `versionMapping.json` and falls back to the compatibility
version only when that mapping is unavailable.

The development-tool list also accepts manually selected executables. Known
Unity, Godot, and IDE executables retain their project-launch behavior, while an
otherwise unknown executable is treated as a generic server IDE. Multiple
manual installations of the same kind are allowed and can be removed
independently. Manual registrations are stored only in Hub's per-user
application data, take precedence over identical automatic candidates, and
never change editor settings, the system environment, or project content.

Settings owns one server-editor selector shared by every project. It defaults
to Rider, then Visual Studio, then VS Code, then a manually added generic IDE.
Project rows expose only the server open and package actions. Console clients
reuse the global server-editor selection. Unity, Tuanjie, and Godot clients
open with the best detected or manually added editor matching the inspected
client kind; Unity Hub and Tuanjie Hub are environment information and are not
used in place of an Editor. Hub disables the action when no compatible editor
is available.
The per-project overflow menu is reserved for opening the project folder and
removing only Hub's local list entry; server and client editor actions remain
visible in the project row.

## Settings And Localization

Environment status is part of Settings rather than a separate navigation area.
The settings page owns the Hub display language, .NET SDK status, global server
IDE selection, detected editor summary, and an explicit editor re-detection
action.

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

Replacing localized ComboBox choices may transiently clear Avalonia's selected
item. Project-creation state must preserve its last valid selections during
that binding transition, and persistence must only capture a complete draft.

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
the app from the mounted DMG, and Linux invokes the distribution package
manager through PolicyKit, waits for its result, and keeps package ownership
under `/usr/lib/lakona-hub`. After a successful Linux or Windows package update,
Hub starts the newly installed version by default and closes the old window. On
Windows, the running Hub copies its NativeAOT executable into the verified
update staging directory as a temporary update worker. The worker waits for the
old process to exit, runs Windows Installer with system authorization, and then
reopens the installed application; this avoids holding installed files open
during MSI replacement. If Windows installation is canceled or fails, the
worker reopens the existing installed version. Hub does not overwrite
package-managed application files or bypass the platform installer; the
operating system owns authorization and displays the elevation prompt.

Hub automatically checks for updates when the main window opens and whenever
the user returns to it after it was deactivated, but a successful check remains
fresh for one hour. Automatic checks during that interval reuse the persisted
result, including an available update, and a return during an active check does
not queue a redundant check after it completes. The sidebar shows the installed
Hub version above the **Help & feedback** button; clicking the version opens the
update dialog, which owns the explicit **Check for updates** action that always
performs a fresh check. When an update is available, an **Update** button
appears beside the version number, and both the dialog action and the button
run the same verified installer flow.
The update module exposes only check and install operations to the window.
GitHub discovery, platform and Linux package-family selection, semantic-version
comparison, delta selection, downloading, verification, staging, replacement,
package-installer launch, and rollback remain behind that interface.

Before opening an installer, Hub verifies its length and SHA-256 against the
Release manifest. A failed download or verification leaves the installed Hub
unchanged.

Installer downloads stream directly to the staged package and report actual
received bytes against the manifest size. The update dialog shows a determinate
progress bar with percentage and transferred size while downloading, followed
by explicit verification and installer-launch states.

Release assets and manifests are retrieved over authenticated HTTPS from the
GitHub API and GitHub Releases. Repository release permissions are restricted
to the publishing job; the desktop client never receives a GitHub token.

### Deferred User-Scoped Installation And Branded Updating

Lakona intends to replace the installer-owned Hub update experience with a
user-scoped, fully branded installation and update path when release engineering
capacity is available. This is deferred future work, not the current release
contract. Until every migration and acceptance requirement below is complete,
MSI, DMG, DEB, and RPM remain authoritative and Hub must not partially bypass
their ownership.

The target experience is one Lakona-designed window from initial installation
through later updates. A small standalone updater remains alive after Hub exits,
shows the active stage in the selected Hub language and theme, applies the new
payload transactionally, verifies that the new process is healthy, and restores
the previous version when activation fails. Platform authorization may still
appear when the operating system requires it; the UI must not imitate or obscure
an elevation or security prompt.

#### Distribution And Installation Ownership

Windows is the first implementation target. New installations move from the
per-machine MSI under `Program Files` to a per-user application root:

```txt
%LOCALAPPDATA%/Programs/Lakona Hub/
  Lakona.Hub.exe
  Lakona.Hub.Updater.exe
  ... NativeAOT publish payload

%LOCALAPPDATA%/Lakona/Hub/
  settings.json and existing Hub-owned state
  logs/
  updates/
    lock
    journal.json
    staging/<version>/
    backup/
```

The release publishes a signed offline `Lakona.Hub.Setup.exe` containing the
complete compressed Hub payload. The setup program renders the same installation
surface as the updater, verifies its embedded payload before extraction, creates
per-user Start menu registration, and writes an uninstall entry under the
current user's Windows uninstall registry. It never requires a system-wide
service, modifies `PATH`, or writes to `Program Files`.

The installed updater is a dedicated Avalonia NativeAOT executable rather than
a second implementation in web technology. It may share a narrowly scoped Hub
resource dictionary for colors, typography, icons, localization, and control
metrics, but it must not reference `MainWindow`, project workflows, the SDK
manager, or another Hub feature module. Its command protocol and update journal
are versioned contracts. The running Hub copies the current updater into the
verified staging root before launch so the helper never replaces files from its
own executable directory.

Windows Releases add a compressed application payload alongside the setup
program. A future manifest schema records at least the payload kind, target
architecture, root directory, entry point, exact length, SHA-256 digest, minimum
compatible updater protocol, and detached signature identity. Full payloads are
required initially. Delta payloads remain optional and must reconstruct a byte-
identical, fully verified full payload before activation; failure to apply a
delta falls back to the full asset.

Hub-owned settings remain in the existing per-user application-data location,
outside the versioned application root. Updating, rolling back, or uninstalling
the application therefore never rewrites project registrations or user
preferences. Uninstall preserves this data by default and offers an explicit
separate choice to remove it.

#### Update Transaction

Only one setup, update, repair, or uninstall transaction may own the per-user
lock. A second process reports the active operation instead of waiting without a
bound. Every filesystem path is canonicalized and checked against the exact
application, staging, or backup root before extraction, replacement, or cleanup.
Archives reject absolute paths, parent traversal, links, duplicate destinations,
and entries outside the declared payload root.

The normal update state machine is:

```txt
checking
  -> downloading
  -> verifying
  -> waiting-for-hub-exit
  -> extracting-to-staging
  -> backing-up-current-version
  -> activating-new-version
  -> launching
  -> awaiting-health-confirmation
  -> completed

any activation failure after backup
  -> rolling-back
  -> relaunching-previous-version
  -> failed-but-recovered
```

Hub continues to stream downloads to a temporary file, supports bounded retry
and resume when the server confirms the same asset, and verifies length, digest,
signature, platform, and version before renaming the payload into the trusted
staging area. Verification failure leaves the installed application untouched.

Before exiting, Hub passes the updater only validated absolute paths, the parent
process identifier, selected language and theme, expected installed version,
target version, and a one-time activation token. The updater persists each
completed state transition to `journal.json` with an atomic replace and flushes
the transition before performing the next destructive step. Logs contain stage,
version, duration, exit code, and a bounded sanitized error; they must not record
project paths, user-profile paths, downloaded URLs containing credentials, or
environment variables.

After the parent process exits, the updater extracts into a new directory and
validates the staged entry point before changing the live installation. It moves
the current application root to the single backup location, moves the staged
root into the stable application path, and starts the new Hub with the one-time
activation token. The new process confirms startup only after Avalonia
initialization, settings loading, and update-protocol compatibility checks have
completed. A bounded timeout, early process exit, wrong version, or invalid
token triggers rollback.

Rollback stops the failed new process, removes only the validated new
application root, restores the backup to the stable path, and relaunches the
previous executable. One known-good backup is retained until health confirmation
and then deleted. A crash or power loss resumes from the journal on the next
setup, updater, or Hub start; recovery decisions depend on the persisted stage
and observed directories, not on elapsed time alone. If neither version can be
proved complete, the updater preserves both directories, stops mutation, and
offers repair with an actionable log location.

Cancellation is allowed during checking and downloading. Once backup begins,
the UI changes from a cancel action to an explanation that recovery must finish.
Closing the updater window during this phase hides or minimizes the surface but
does not terminate the transaction. Setup and update retain at most one backup,
one active staging directory, and a bounded number of failure logs; abandoned
downloads expire by age and total-byte budget.

#### Initial Setup, Repair, And Uninstall

Initial setup uses the same extraction, activation, launch confirmation, and
rollback machinery as an update, with the missing current installation treated
as an empty backup. Re-running setup for the installed version offers launch,
repair, and uninstall rather than silently overwriting files. Repair verifies
the installed payload against the signed manifest and restores only from a
complete verified payload.

Uninstall launches a staged copy of the updater, waits for Hub to exit, removes
the exact per-user application root, Start menu entries, and current-user
uninstall registration, then removes its own temporary directory on the next
safe cleanup opportunity. User settings and managed SDKs are separate explicit
choices because deleting either may be expensive or surprising.

Existing MSI installations require a one-time migration. The new setup detects
the registered MSI product and must not silently create two authoritative Hub
installations. It installs and validates the per-user copy first, transfers no
settings because settings already live in per-user application data, updates
shortcuts, and then offers to remove the MSI. Removing the machine-owned MSI may
produce one final Windows authorization prompt. Cancellation leaves the verified
new copy available but reports the old MSI registration clearly; release notes
and the updater must not claim that migration is complete until the MSI is gone.

#### macOS And Linux Boundary

The visual state model, manifest validation, journaling, health confirmation,
and rollback rules are platform-neutral. Installation ownership is not.

- macOS may add a signed and notarized application ZIP for in-app updates while
  retaining a DMG for first installation. The updater may replace only the exact
  installed `.app` bundle after validating its code signature and notarization.
  A non-writable Applications directory or App Translocation requires an
  explicit platform-appropriate fallback; Hub must not weaken macOS security
  attributes or install an unnotarized replacement.
- Linux DEB and RPM installations remain package-manager-owned and continue
  through PolicyKit. A fully branded, non-privileged path requires a separate
  user-owned distribution such as an AppImage or verified per-user archive.
  Hub detects its installation kind and never updates package-managed files
  with the user-owned updater. Both channels may share discovery and progress
  UI, but authorization and activation remain truthful to their owner.

Identical pixels are not a cross-platform requirement. Shared language,
hierarchy, status names, icons, progress behavior, and recovery semantics are;
native security prompts and platform installation conventions remain visible
when required.

#### Release Security Prerequisites

The custom path must not ship until its trust chain is stronger than the current
package path:

- Windows setup, Hub, and updater executables have valid Authenticode signatures
  from the same publisher, and CI verifies those signatures before publishing.
- macOS application and updater code are signed with hardened runtime, notarized,
  and stapled; CI validates the final distributed archive rather than only the
  pre-archive bundle.
- The release manifest has a detached signature verified with a pinned Lakona
  release key. Transport HTTPS and a digest delivered beside its payload are not
  sufficient on their own.
- Signing credentials remain in protected release jobs. Desktop clients contain
  public verification material only and never receive publishing credentials or
  a GitHub token.
- Downgrades are rejected by default. An explicit repair or rollback may activate
  only the journaled previous version or a user-selected payload whose signature
  and compatibility are valid.

The updater protocol must remain backward compatible for at least the oldest Hub
version allowed to update directly to the current release. A manifest requiring
a newer protocol selects a separately signed updater bootstrap asset, verifies
it with the existing trust root, and restarts the transaction before touching
the installed Hub. Release publication fails when no tested upgrade path exists
from the declared minimum supported version.

#### Required Verification Before Cutover

Focused unit and contract tests cover manifest parsing, signature and digest
failure, path containment, malicious archives, lock contention, state-machine
transitions, journal recovery, health-token validation, cancellation boundaries,
retention limits, uninstall preservation choices, and every rollback branch.
Filesystem tests inject a failure after each destructive transition and prove
that at least one complete signed version remains recoverable.

Release E2E runs on clean platform VMs and validates:

1. clean offline installation and launch;
2. update from the oldest supported version and from the immediately previous
   version;
3. interrupted download and successful resume;
4. process crash and simulated power-loss recovery at every journaled phase;
5. failed new-version startup followed by automatic rollback;
6. concurrent setup/update rejection;
7. repair of a corrupted installed file;
8. uninstall with settings preserved and with explicit settings removal;
9. Windows MSI-to-user-install migration, including canceled authorization;
10. signature, wrong-architecture, downgrade, and archive-traversal rejection.

The current package channel is removed only after signed artifacts, migration,
uninstall registration, rollback, crash recovery, platform E2E, and documentation
all pass in the same release candidate. Windows may cut over before macOS and
Linux, but manifests and UI must identify each platform's active installation
owner instead of pretending that all platforms have switched together.

## Guided Project Creation

The creation experience is one page rather than a multi-step wizard. It shows
project name, output location, client and engine version, transport, serializer,
NuGetForUnity source, and cluster Membership provider at the same time.
Options are never hidden behind an advanced-settings disclosure. Fields that do
not apply to the selected client remain visible but disabled with an explanation.

Hub keeps the most recently entered project name and output location in its
user-settings draft. New forms default to Unity 2022, WebSocket, MemoryPack,
OpenUPM NuGetForUnity packages, and in-memory Membership. PostgreSQL, Redis,
and MySQL choices generate the matching Adapter package and connection
configuration. Hub does not expose or generate deployment configuration.
Changing the client immediately updates its
supported version choices. The final project path and validation result remain
visible before creation.

Hub delegates creation to the same `Lakona.ProjectSystem` creator as the CLI.
For Unity and Tuanjie this includes the exact-editor, source-free NuGet restore
and verification transaction; Hub does not maintain a second restore path. If
the required editor cannot start or restore all packages, creation reports the
failure and no final project directory is published.

While creation is active, Hub keeps a bounded modal progress dialog visible and
reports the current ProjectSystem stage: preparing the request, restoring and
verifying client dependencies, writing the transactional project, initializing
Git, and completing the operation.

The project index persists when each project was added to Hub separately from
when it was last opened. The default descending time sort uses the last-opened
time when present and otherwise the added time, so a newly created or manually
imported project that has never been opened appears first without being labeled
as opened.

## Security Contract

Opening and inspecting files is distinct from packaging a project. An imported
project may contain arbitrary MSBuild targets, so Hub requires an explicit user
action before packaging. Packaging uses the selected compatible SDK, executes
as the current non-elevated user, and supports cancellation.

Downloaded SDK archives and application updates must be retrieved over HTTPS
and verified before activation. SDK asset URLs and SHA-512 digests come from
Microsoft's official .NET release metadata. A failed SDK or Hub update keeps
the previous version usable.

## V1 Validation Budgets

Release validation must measure, rather than assume:

- cold and warm startup time
- idle private working set
- application payload size and on-demand SDK download size separately
- cancellation and cleanup of packaging child processes
- successful startup on a machine without a globally installed .NET SDK

Every release treats trim and AOT analysis warnings as errors. After each native
publish, CI starts the produced executable on the matching operating system and
runs the built-in `--aot-smoke-test`. That path initializes Avalonia and the main
window's compiled XAML, and exercises all three localization resources without
requiring a system SDK or network connection. It also switches a fully
initialized window through every supported language so compiled bindings and
settings persistence participate in the test. SDK manager tests independently
cover system discovery, official metadata selection, streamed progress,
integrity verification, version validation, and atomic activation. Release
packaging tests and the repository release guards remain mandatory in addition
to this executable smoke test; suppressing an unexplained trim or AOT warning
is not an acceptable way to make a release pass.
