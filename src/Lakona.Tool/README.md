# Lakona.Tool

Command-line creation and packaging for Lakona.Game projects. Generated
workspaces include shared contracts, server App and Hotfix projects, a selected
client, deployment files, documentation, and compatible Agent Skills.

## Install

```bash
dotnet tool install -g Lakona.Tool
```

## Create a Project

```bash
lakona-tool new
```

For scripted creation:

```bash
lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack
```

Use `lakona-tool new --help` for the installed version's options. Follow the
generated project's README for its build, startup, client editor, and readiness
steps. Commit its `.agents/skills/` directory with the project.

## Guides

- [Default project experience](https://github.com/bruce48x/Lakona/blob/main/docs/tool/default-experience.md): generated layout, configuration, and runtime model.
- [Packaging and deployment](https://github.com/bruce48x/Lakona/blob/main/docs/deployment.md): server and Hotfix packages, installation, activation, rollback, and rollout.
- [Cluster](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md): Membership providers and split-node configuration.
- [Actors and Hotfix](https://github.com/bruce48x/Lakona/blob/main/docs/actor.md): generated Actor APIs and behavior authoring.
- [Agent Skills](https://github.com/bruce48x/Lakona/blob/main/docs/tool/agent-skills.md): bundled workflows and compatibility.
- [Lakona Hub](https://github.com/bruce48x/Lakona/blob/main/docs/tool/lakona-hub.md): graphical project creation and inspection.
