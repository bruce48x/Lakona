# Lakona.Game.Server

Server hosting for Lakona game applications: RPC, sessions, reliable push,
Actors, Hotfix, and cluster coordination. Reference this package from the stable
server application; it also supplies the matching Hotfix compiler extension.

## Install

```powershell
dotnet add package Lakona.Game.Server
```

Add the transport and serializer packages selected by your client-facing
endpoints. New applications can start with `lakona-tool new`.

## Guides

| Task | Documentation | Agent workflow |
| --- | --- | --- |
| Create and run a project | [Project setup](https://github.com/bruce48x/Lakona/blob/main/docs/tool/default-experience.md) | [Skill Pack](https://github.com/bruce48x/Lakona/blob/main/docs/tool/agent-skills.md) |
| Configure nodes and endpoints | [Configuration](https://github.com/bruce48x/Lakona/blob/main/docs/configuration.md), [Cluster](https://github.com/bruce48x/Lakona/blob/main/docs/cluster.md) | |
| Implement RPC services | [Hotfix service binding](https://github.com/bruce48x/Lakona/blob/main/docs/hotfix/service-binding.md) | [Implement service](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-service/SKILL.md) |
| Initialize application resources | [Application modules](https://github.com/bruce48x/Lakona/blob/main/docs/application-modules.md) | [Implement module](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-module/SKILL.md) |
| Expose HTTP routes | [Application HTTP](https://github.com/bruce48x/Lakona/blob/main/docs/http.md) | [Implement HTTP service](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-http-service/SKILL.md) |
| Own state and schedule work | [Actors and Hotfix](https://github.com/bruce48x/Lakona/blob/main/docs/actor.md) | [Implement Actor](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-actor/SKILL.md), [Implement timer](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-timer/SKILL.md) |
| Handle sessions and notifications | [Sessions and reliable push](https://github.com/bruce48x/Lakona/blob/main/docs/session.md) | [Session lifecycle](https://github.com/bruce48x/Lakona/blob/main/skills/lakona-implement-session-lifecycle/SKILL.md) |
| Diagnose and deploy | [Logging](https://github.com/bruce48x/Lakona/blob/main/docs/logging.md), [Observability](https://github.com/bruce48x/Lakona/blob/main/docs/observability.md), [Guardrails](https://github.com/bruce48x/Lakona/blob/main/docs/guardrails.md), [Deployment](https://github.com/bruce48x/Lakona/blob/main/docs/deployment.md) | |

The linked documents define configuration, lifecycle, recovery, and delivery
contracts. Generated projects include compatible project-scoped skills under
`.agents/skills/`; use that snapshot for the project's framework version.
