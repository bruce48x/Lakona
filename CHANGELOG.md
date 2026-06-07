# Changelog

Lakona 于 2026-06-07 将原来的 ULinkGame、ULinkActor、ULinkRpc
三个仓库合并为单一 monorepo。本 changelog 从此开始。

## 2026-06-07

### 合并 Lakona.Rpc.Starter 到 Lakona.Tool

`Lakona.Tool` 现在是 Lakona 唯一的 .NET CLI 工具，一个命令完成所有项目生成。

```bash
dotnet tool install -g Lakona.Tool
lakona-tool new --name MyGame --client-engine unity --transport websocket --serializer json
```

- 原先分散在 `lakona-starter` 的 RPC 工作区生成逻辑已移入 `Lakona.Tool` 内部，不再需要单独安装第二个工具。
- 工具命令名从 `lakona` 改为 `lakona-tool`，帮助文本和文档已同步更新。
- 启动器生成代码位于 `src/Lakona.Tool/RpcStarter/`，作为内部 `Lakona.Tool.RpcStarter` 模块运行。
- 删除了独立的 `src/Lakona.Rpc.Starter` 项目和 `tests/Lakona.Rpc.Starter.Tests`，测试已合并到 `tests/Lakona.Tool.Tests/RpcStarter/`。
- CI 的 Godot 每日验证合并为单一 `lakona-tool` 任务。
- 文档更新为 `lakona-tool` 作为唯一的项目生成入口。
