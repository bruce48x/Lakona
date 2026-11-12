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
The activation channel acknowledges requests after dispatching the activation
handler; the new process waits up to two seconds for this acknowledgement.
The instance lock and activation channel are local operating-system resources,
and they do not add files to a Lakona project.

## Technology Decision

The desktop adapter uses Avalonia on .NET 10 and release builds use NativeAOT.
Hub itself is self-contained, while project packaging uses a compatible .NET 10
SDK. Windows uses a per-user EXE setup, macOS uses DMG, and Linux uses native DEB and RPM
packages. Release artifacts contain only the Hub application so routine Hub
updates do not repeatedly transfer an SDK. Their version lifecycles remain
distinct:

- the Hub application updater updates Hub
- the SDK manager installs and switches verified SDK versions

At startup, the SDK manager first checks the Hub-managed SDK, then compatible
stable .NET 10 SDKs available through the system `dotnet` command and standard
platform installation locations. A missing executable skips only that candidate;
cancellation stops discovery, and other native process-start errors remain errors.
If neither exists, Hub explains the source
and destination and waits for explicit consent
before downloading the pinned SDK from Microsoft's official release service.
Selection matches the SDK version and platform and accepts only `.zip` or
`.tar.gz` archives, regardless of installers appearing earlier in the release
list. If no supported archive exists, installation fails before downloading it.
The archive is SHA-512 verified, extracted to a temporary directory, version
validated, and atomically activated in Hub's per-user application-data
directory. Hub does not modify the user's system `PATH`, require administrator
access, or install a global SDK.

The [first-project tutorial](../../blog/content/posts/getting-started.md) builds
and runs the server from an IDE or an ordinary terminal. That path also requires
a system .NET 10 SDK: `dotnet` must be on the terminal's `PATH`, and
`dotnet --list-sdks` must include a `10.0.xxx` SDK. The server IDE must use a
compatible system SDK as well. Installing only the Hub-managed SDK or a .NET
runtime does not satisfy this prerequisite; Hub does not configure the IDE to
use its private SDK.

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

Settings changes are debounced and written in the background. Background and
immediate saves serialize disk writes; a canceled save rechecks cancellation
after obtaining write access. An immediate save writes its latest UI-captured
snapshot after any in-flight writer. Disposal cancels pending saves and waits
for disk I/O, without waiting for a continuation on the closing UI thread.
Atomic file replacement preserves file integrity; serialized writes preserve
save ordering.

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
behavior for the stable server BuildTag. On opening, the dialog retains the
inspected server directory and uses it for BuildTag edits and both package
project paths. The dialog displays the BuildTag from that directory's
`BuildTag.props` in an editable field. Each valid edit is
saved immediately through ProjectSystem, preserving whether the props file has
an XML declaration; invalid input preserves the last saved
value, and invalid input or a save failure disables packaging. The field is
disabled while packaging runs. The dialog never asks for
a package version. The artifact output directory defaults to `Build` under the
inspected server directory (`Server/Build` for the standard layout) and
can be edited directly or selected with the platform folder picker before the
operation starts. Hub uses the exact compatible .NET 10 SDK executable selected
by its SDK manager, including a Hub-managed SDK that is not on the system
`PATH`.

Renamed server roots therefore use the same resolved location for saving and
packaging. An unresolved server directory blocks both operations with an
actionable message; Hub does not fall back to creating another `Server`
directory. Custom output directories remain supported. ProjectSystem's
existing project-root BuildTag save entry point and CLI default paths retain
their standard-layout behavior.

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

Closing the window during packaging requests cancellation. The running
operation releases its cancellation source when it finishes. Late progress
and results do not update the closed form or launch an artifact folder.

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

When Hub creates a Unity or Tuanjie project, it passes the selected detected
editor executable to ProjectSystem. Editor compatibility is checked by the
major/minor version stream, so patch releases in the same stream are accepted.
The generated client `ProjectVersion.txt` records the exact editor version and
revision that performed dependency restoration.

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
The settings page owns the Hub display language, appearance, .NET SDK status,
global server IDE selection, detected editor summary, and an explicit editor
re-detection action. General preferences and development settings use two
compact groups of aligned rows rather than a separate card for every setting.
SDK details and development-tool actions stay in their respective rows, while
each detected installation retains its own path and action. Appearance offers
dark, light, and follow-system choices.
The preference is persisted, changes apply immediately, and follow-system uses
Avalonia's default theme variant so operating-system appearance changes do not
require platform-specific code.

Light appearance uses neutral gray page surfaces, white cards, and dark text.
Primary actions use mango yellow (`#FFB824`) suited to white surfaces, with
matching warm yellow hover and pressed fills rather than brown shades. Selected
items use pale yellow with black text; accent text, meaningful icons, progress indicators,
and accent borders use black in light appearance. The selected sidebar item
uses a separate mango tint (`#FFD782`), with matching
hover and pressed shades; it does not change other selected-item surfaces. Color
and opacity overrides for control states are scoped to the actual light theme,
including when following the system. Dark appearance retains its established
palette and control-state colors; font and size improvements are shared.
Enabled text must maintain at least 4.5:1 contrast against its surface, and
control boundaries and focus indicators at least 3:1 against the surrounding
surface. Hover, pressed, selected, and keyboard-focus states must preserve
those foreground/background pairings. Supporting text starts at 12 logical
pixels, and form hints and body copy use 13 and 14 pixels respectively. The
shared font stack includes Windows, macOS, and Linux CJK sans-serif families so
missing platform fonts do not silently degrade Chinese text into thin serif
fallbacks. Form hints wrap within their columns at the minimum window size.

The desktop window is user-resizable even though Hub draws its own frame. Its
minimum supported size is 1000 by 800 logical pixels, and its last normal size,
position, and maximized state are restored on the next launch.
In normal window mode, that frame uses a black edge in light appearance and
the established yellow edge in dark appearance. Its emphasis
tracks activation, follows the complete rounded outline, and uses a soft
exterior shadow that separates Hub from the desktop. Maximized windows remove
the edge, shadow, outer inset, and corner radius so content remains flush with
the screen boundary. Hub remains fully borderless so native glass margins never
surround the rounded application frame. Minimizing enters the platform
minimized state immediately on every supported desktop platform. Closing
immediately runs the normal shutdown cleanup, whether requested by the custom
close button or a platform gesture such as Alt+F4.

### Cross-Platform Visual Contract

Hub's visual language, hierarchy, spacing, state emphasis, and interaction
feedback must remain coherent across Windows, macOS, and Linux. Platform-native
differences are appropriate when they preserve operating-system conventions,
accessibility, or security, but a decorative effect must not silently make one
platform look like a different product or leave another with a visibly degraded
substitute.

Use Avalonia's public, shared controls, composition primitives, and supported
platform capabilities as the default implementation boundary. A visual proposal
that cannot be implemented cleanly through those facilities is a design
checkpoint, not routine implementation work. If it would require
platform-specific interop, undocumented hooks, duplicated rendering paths, or a
fragile workaround, stop before implementing it and discuss the tradeoff
explicitly. That discussion must cover the intended visual outcome, affected
platforms, fallback behavior, accessibility and performance impact, validation
cost, and long-term maintenance burden.

A platform-specific enhancement may proceed only after that review establishes
that it is progressive enhancement: every supported platform retains the same
information hierarchy, readability, interaction semantics, and deliberate
visual quality, and the enhancement does not create artifacts outside its
intended surface. Cross-platform consistency takes precedence over decorative
fidelity when those requirements conflict.

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

Each Release contains `Lakona.Hub.Setup.exe` and a `win-x64-user.zip` update
payload for Windows, x64 and arm64 DMGs for macOS, one x64 DEB, one x64 RPM,
and `lakona-hub-manifest.json`. Linux asset names include `linux-x64`.
New MSI packages are no longer built or published. Existing MSI users download
the EXE once to migrate; their older built-in MSI updater cannot install it.

Windows updates use the independent per-user updater described below. macOS
opens the verified DMG for installation. Linux invokes the distribution package
manager through PolicyKit, waits for its result, and keeps ownership under
`/usr/lib/lakona-hub`; successful installation restarts Hub. Platform-managed
application files are never replaced by the Windows user updater.

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
Platform detection runs during update checks, not window construction. An
unsupported Linux environment reports an update error without preventing Hub
startup. Inside Flatpak, Hub identifies the host through `/run/host/os-release`
and checks host package-manager paths under `/run/host/usr/bin`, never the
runtime's distribution or package manager. Installation and restarting the
installed Hub use `flatpak-spawn --host`; the IDE must already permit host
commands and expose the host tools. PolicyKit still owns installation
authorization. The downloaded package must be reachable at the same absolute
path on the host, as it is in Rider's per-app data directory under `~/.var/app`.
Missing host tools or denied host-command permissions produce an update error;
Hub does not change Flatpak permissions.

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

### Windows User-Scoped Installation

Windows uses three components: an offline NativeAOT setup bootstrap, an
independent Avalonia updater window, and shared installation logic. The updater
shares Hub's color dictionary, without referencing Hub features. First install,
repair, and updates use the same verified payload, staging, startup confirmation,
and recovery code. There is no background service or delta-patch implementation.

The installer writes only the current user's application and registration:

```txt
%LOCALAPPDATA%/Programs/Lakona Hub/
  Lakona.Hub.exe
  Lakona.Hub.Updater.exe
  ... NativeAOT payload and signed file manifest

%LOCALAPPDATA%/Lakona/Hub/
  user-settings.json and existing Hub-owned state
  updates/<version>/<platform>/
  installer/
    lock, journal.json, health.json, installation.log
    staging/, backup/, setup-<id>/, handoff-<id>/
```

Setup creates a current-user Start menu shortcut and Windows uninstall entry.
It does not modify `PATH` or write the new application into `Program Files`.
The installer uses a compact borderless window with the Hub identity, one
Install action, and a close control. Progress and diagnostics appear only after
an operation starts. Re-running setup updates or repairs the current installation;
uninstall is available through the Windows installed-apps entry. Uninstall
removes the application, shortcut and registration, preserving projects,
settings and downloaded SDKs. There is no implicit user-data removal option.

#### Package Trust And Release Builds

Windows Authenticode publisher signing is deferred. Unsigned executables work,
but Windows may display security warnings; setup does not suppress or bypass
them. This is separate from the required RSA-PSS/SHA-256 signature on the update
payload's file manifest. Every file is bound to that manifest before activation.
Archives reject tampering, undeclared files, traversal, links, duplicate names,
wrong platforms, unsupported protocols, and downgrades.

The public update key is tracked in `src/Lakona.Hub.Installation/release-public.pem`.
Its private key lives in the repository Actions secret `HUB_UPDATE_PRIVATE_KEY`.
CI writes it to a temporary file, clears the environment variable before building,
and removes that file afterwards. No private key is embedded in a binary or
published artifact. The packager rejects a private key that does not match the
tracked public key. Do not replace this key without a tested trust-migration
plan: installed clients pin it across releases.

Build on Windows with a serviced .NET 10 SDK:

```powershell
pwsh -NoProfile -File scripts/hub/New-HubUserSetup.ps1 `
  -Version 0.18.1 -OutputRoot artifacts/hub-user-package `
  -ManifestPrivateKeyPath <protected-RSA-private-key.pem>
```

The optional `-SignScript` and `-PublisherThumbprint` must be supplied together
when Authenticode becomes available. The script receives `-FilePath`; the build
then checks certificate identity and Windows signature validity. The optional
`-RuntimeFrameworkVersion` selects the same serviced runtime and NativeAOT
compiler patch when the local SDK defaults to an older patch.

`New-HubRelease.ps1` requires the EXE and update ZIP under
`hub-win-x64-package`. The release manifest exposes only `win-x64-user` for
Windows. The complete setup supports `--smoke-test`: it verifies and extracts
the embedded payload, opens and closes the updater, and leaves installation
and registration unchanged. An ordinary setup run still requires the user to
choose Install or Repair.

#### Update And Recovery

Hub streams the download with bounded retries and resumes only when the server
confirms the same asset. Length and SHA-256 are checked before handoff; the
pinned key authenticates the package itself. A failed download or verification
does not change the installed application.

Hub verifies the target package and launches its updater from an external
handoff directory. It passes its process ID and start time, language, theme,
and a readiness marker; Hub exits only after the updater accepts the handoff.
The updater waits for that exact parent process and acquires the per-user lock.
A second installer fails promptly instead of waiting indefinitely.

The transaction extracts and verifies staging, moves the current application
to backup, activates staging at the stable application path, and starts Hub
with a one-time token. Journal transitions use atomic replacement and disk
flushes. Startup succeeds only when the live process confirms the expected
version and token after application initialization. Timeout, early exit, or a
bad confirmation triggers rollback and relaunch of the previous version.
After stopping a failed child, the updater waits at most five seconds for Windows
to release its native files before removing them; persistent locks remain errors.
The backup is removed only after confirmation. Interrupted transactions recover
from the journal on the next setup or Hub start. If the backup cannot be
verified, both copies are preserved and the UI offers diagnostics; it does not
guess which damaged copy to delete.

Application, staging and backup paths are canonicalized and checked for links
before replacement or deletion. An installed updater copies verified helper
files outside the application before uninstall or recovery, avoiding Windows
file locks. Transaction-time window closing is blocked. Logs contain bounded
stage names and exception types, never project paths or credentials. Temporary
work cleanup respects active file leases and retains only recent work; the
application has at most one staging directory and one rollback backup.

#### Existing MSI Users

Close the old Hub and run `Lakona.Hub.Setup.exe` once. Setup detects legacy MSI
registration by its existing UpgradeCode, installs and confirms the per-user
copy first, registers its shortcut, and then offers **Remove old installation**.
Settings already reside in per-user application data and need no copying.
Only Windows Installer removes the old machine-owned files, which may require
one final authorization prompt. Canceling or failing that removal leaves the
new Hub available and migration pending. Run setup again to retry; migration
is complete only when MSI registration is gone. The new shortcut is registered
again after removal so legacy cleanup cannot leave it pointing at the old copy.

The historical `hub-v0.17.138` MSI remains a migration test fixture, downloaded
with a pinned SHA-256. It is not rebuilt or included in new releases. macOS DMG
and Linux DEB/RPM ownership and authorization remain unchanged.

#### Verification

Hub tests cover signed archive validation, lock contention, journal recovery,
startup-token validation, failed-start rollback, repair, downgrade rejection,
bounded download resume, and MSI-removal cancellation/failure/retry. Windows
release CI additionally runs the complete offline-setup smoke test and real
native installation, damaged-file repair, uninstall preservation, and migration
from the historical MSI before publication.

`WindowsInstallerAcceptanceTests` is explicitly enabled by
`LAKONA_HUB_ACCEPTANCE_PACKAGE` (the signed update ZIP) and
`LAKONA_HUB_ACCEPTANCE_MSI` (the verified historical MSI). Pass both as absolute
file paths; the test checks that both files exist before changing installation
state. It refuses to replace an existing installation and must run on an
otherwise uninstalled test host.
For local release acceptance also exercise an actual previous-version update
through `UpdaterLaunch`; unit tests alone do not establish cross-process
handoff behavior. Local Windows acceptance is not a claim of physical power-loss
testing, clean-VM coverage, Authenticode trust, or absence of security prompts.

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
For Unity and Tuanjie this includes the compatible-editor, source-free NuGet
restore and verification transaction; Hub does not maintain a second restore
path. If the required editor cannot start or restore all packages, creation
reports the failure and no final project directory is published.

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
