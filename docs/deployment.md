# Packaging and Deployment

## Purpose

This document defines Lakona server packaging, package identity, artifact
layout, installation, activation, rollback, and multi-node rollout. Tooling and
Hotfix architecture documents link here instead of duplicating these
contracts.

Lakona produces two deployment artifacts:

- a **full package** for a first deployment or stable-host replacement;
- a **Hotfix package** for updating reloadable server behavior.

Packaging is implemented by `Lakona.ProjectSystem` and exposed through both
`lakona-tool` and Lakona Hub. Hub and Tool must not implement different package
rules.

## Package Identity

Package identity has two independent parts:

| Part | Owner | Format | Purpose |
| --- | --- | --- | --- |
| BuildTag | user | 1–64 ASCII letters or digits | Declares stable App/Hotfix compatibility |
| Version | tooling | UTC `yyyyMMdd-HHmmssZ` | Uniquely identifies one packaging attempt |

The user configures only the broad compatibility identity in
`Server/BuildTag.props`:

```xml
<Project>
  <PropertyGroup>
    <LakonaBuildTag>Release1</LakonaBuildTag>
  </PropertyGroup>
</Project>
```

`LakonaBuildTag` is case-sensitive and must match `^[A-Za-z0-9]{1,64}$`.
Symbols, whitespace, and non-ASCII letters are invalid. Both
`Server/App/Server.App.csproj` and `Server/Hotfix/Server.Hotfix.csproj` import
this one file. `LakonaBuildTag` is the only accepted property name.

`Server/BuildTag.props` is a deployment compatibility control, not one of the
routine code-editing areas in a generated project. Change it only when the
stable App and Hotfix compatibility boundary deliberately changes.

The package version is generated automatically from the packaging process's
UTC clock, to whole-second precision. Users do not configure it and the pack
commands do not accept a package-version option. For example:

```txt
20260730-153045Z
```

BuildTag changes are deliberate compatibility boundaries. Existing processes
must reject activation of a Hotfix whose manifest BuildTag differs from the
stable host's assembly metadata.

## Creating Packages

The default output directory for both commands is `Server/Build`.

Canonical names are:

```txt
Server.Full-{BuildTag}-{Timestamp}-{RID}.zip
Server.Hotfix-{BuildTag}-{Timestamp}.zip
```

Create a complete, self-contained Linux x64 package:

```bash
lakona-tool server pack --runtime linux-x64
```

The full package is RID-specific, untrimmed, and contains the published stable
host plus an installed initial Hotfix:

```txt
Server/Build/Server.Full-Release1-20260730-153045Z-linux-x64.zip
```

`--runtime` is required. `--configuration` defaults to `Release`;
`--project`, `--hotfix-project`, and `--output` may override their defaults.
The full-package path defaults to `Server/App/Server.App.csproj`, and the
initial Hotfix path defaults to `Server/Hotfix/Server.Hotfix.csproj`.
Publishing is always self-contained and does not enable trimming, single-file,
NativeAOT, or Docker image creation.

Create a follow-up Hotfix package:

```bash
lakona-tool hotfix pack
```

The output name identifies the artifact kind and BuildTag:

```txt
Server/Build/Server.Hotfix-Release1-20260730-153045Z.zip
```

Lakona Hub exposes the same operation from the **Package** button beside
**Open server**. It displays the inspected BuildTag as read-only project
metadata and does not ask for a package version. The shared ProjectSystem
packager starts build child processes without creating a separate console
window, whether packaging is initiated by Hub or `lakona-tool`.

Both writers stage and validate the zip before moving it to its final path. If
that exact file or directory already exists, packaging fails, preserves the
existing target, and does not overwrite it or invent a numeric suffix. A
whole-second timestamp collision is therefore visible instead of ambiguous.

## Full Package Layout

The zip root is directly executable after extraction:

```txt
Server.App
Server.App.dll
*.dll
*.json
lakona-server.json
hotfix/
  current.txt
  versions/
    20260730-153045Z/
      Server.Hotfix.dll
      Server.Hotfix.pdb
      Server.Hotfix.deps.json
      hotfix.json
      checksums.sha256
      READY
```

Platform-specific and optional publish files may differ. The stable manifest
records the package version, UTC build time, RID, configuration, entry
assembly, BuildTag, initial Hotfix version, and Tool version. The initial
Hotfix manifest and stable manifest must have the same BuildTag.

`current.txt` names an existing version directory, and `READY` is written only
after validation completes. Full-package validation verifies the entry
assembly, manifests, checksums, active Hotfix version, and BuildTag agreement.

## Hotfix Installation and Activation

Packaging happens in a developer or CI workspace. Installation and activation
are node-local operations after normal deployment automation copies the zip to
the node:

```bash
lakona-tool hotfix install \
  Server.Hotfix-Release1-20260730-153045Z.zip \
  --root /srv/agar/current/hotfix

lakona-tool hotfix activate 20260730-153045Z \
  --server http://127.0.0.1:20080

lakona-tool hotfix status \
  --server http://127.0.0.1:20080

lakona-tool hotfix rollback \
  --server http://127.0.0.1:20080
```

Install extracts to a staging directory, validates the manifest and SHA-256
checksums, moves the verified version under `hotfix/versions/`, and writes
`READY` last. Reinstalling identical content is idempotent; different content
under the same version fails.

Activate, status, and rollback use the running node's loopback-only HTTP admin
endpoint. V1 deliberately has no remote upload, public management endpoint, or
built-in cluster rollout command.

## Three-Node Rollout

Build the full or Hotfix artifact once and promote that immutable file through
all three nodes. Do not rebuild separately per node. Node-specific runtime
configuration and secrets are deployment inputs, not package identity.

A production rollout should:

1. verify the artifact checksum in the deployment system;
2. drain one node from new traffic;
3. install the same package on that node;
4. start the full package or activate the Hotfix through loopback;
5. wait for readiness and application smoke checks;
6. return the node to traffic;
7. repeat for the remaining nodes;
8. stop and roll back before continuing when validation fails.

Keep at least the previous stable directory and Hotfix version until the
rollout is accepted. For full deployments, use versioned release directories
and an atomically switched `current` link. For Hotfix rollback, use the
node-local rollback command.

## Runtime Configuration

The same artifact runs on every node. Select node-specific configuration with
`DOTNET_ENVIRONMENT`, for example:

```bash
DOTNET_ENVIRONMENT=battle1 ./Server.App
```

The host reads `appsettings.json`, then
`appsettings.{Environment}.json`, then environment variables, then command-line
arguments. Keep ordinary node topology in centrally managed environment files
or configuration-management templates. Keep secrets in the deployment
platform's secret store or protected environment injection, never in the
package or repository.

Process supervision is separate from packaging. `systemd` is the recommended
Debian service supervisor; Hotfix activation does not require restarting the
service, while stable-host replacement can switch the release directory and
restart the unit. If another supervisor is chosen, it must provide equivalent
restart policy, logging, shutdown, and boot behavior.

PostgreSQL and Redis are external application infrastructure. Their Docker
Compose topology, backup, persistence, security, and lifecycle remain outside
Lakona package artifacts. A three-node Lakona deployment may use one
PostgreSQL node and a six-node Redis Cluster, but those services must be
operated and validated independently.
