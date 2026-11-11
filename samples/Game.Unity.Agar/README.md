# Game.Unity.Agar

轻量小球吞噬样例，演示共享 C# 协议、单机玩法、联网匹配、可靠推送及三节点部署。
单机模式离线运行；联机模式由客户端执行确定性模拟，服务端组帧、转发输入并处理业务状态。

- [设计与边界](docs/GAMEPLAY_DESIGN.md)
- [生产上线门槛](docs/PRODUCTION_LAUNCH_PLAN.md)
- [本地集群教程](https://bruce48x.github.io/Lakona/posts/agar/)

## 快速运行

需要 Docker、PowerShell 7 和 Unity 2022 LTS。在本样例目录执行：

```powershell
pwsh -NoProfile -File ./server-ctl.ps1 start -Topology single
```

脚本构建镜像、启动数据库和游戏服务，并等待 readiness 成功。
用 Unity 打开 [Client](Client)，运行 `Assets/Scenes/Game.unity`，选择联机并登录。
单机模式无需启动服务端；移动使用 `W/A/S/D`。

三节点运行省略 `-Topology single`。状态、日志、停止及其他参数见脚本帮助：

```powershell
./server-ctl.ps1 help
```

默认配置和端口见 [appsettings.json](Server/App/appsettings.json) 与
[docker-compose.yml](docker-compose.yml)。远程客户端需要可达的广告地址，
在启动前设置 `AGAR_BATTLE_ADVERTISED_HOST`。遥测配置遵循
[Observability](../../docs/observability.md#opentelemetry-setup)，服务端已注册 SDK。

## 本机源码开发

另需系统 .NET 10 SDK。若已启动 Compose 游戏节点，先执行
`./server-ctl.ps1 stop`，避免默认端口冲突。随后在本样例目录执行：

```powershell
docker compose up -d --wait postgres redis
dotnet build Server/Server.slnx
dotnet run --project Server/App/Server.App.csproj --no-build
```

解决方案构建会生成 App 和 Hotfix；单独运行 App 不会生成 Hotfix。
更改 Hotfix 后重新构建 Hotfix 项目。若通过 `.env` 或环境变量更改数据库
端口、账号或密码，需同步服务端连接串。

## 代码导航

- [Shared](Shared)：共享协议与确定性模拟。
- [Client](Client)：Unity 客户端与表现。
- [Server/App](Server/App)：稳定宿主、绑定、Actor 内存状态。
- [Server/Hotfix](Server/Hotfix)：按业务领域组织的可热更逻辑。
- [infra](infra)：部署配置；[小规模环境部署](infra/terraform-small-test/README.md)保留该环境专属操作。

Application HTTP 示例见
[AgarOperationsHttpService](Server/Hotfix/Operations/AgarOperationsHttpService.cs)：
`GET /internal/users/{account}`。该接口没有内置认证或 TLS，部署时须限制访问；
它与框架 management/readiness listener 分离。框架约定见
[Application HTTP](../../docs/http.md)。

## 验证入口

以下命令从仓库根目录运行；业务测试需要系统 .NET 10 SDK：

```powershell
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

持久化测试使用真实 PostgreSQL/Redis；可先在样例目录启动上述数据库服务，
或通过 `AGAR_TEST_POSTGRES`、`AGAR_TEST_REDIS` 指定专用实例。
普通规则测试不需要数据库。

三节点 E2E 的参数和排障见
[专用验证 skill](../../.agents/skills/agar-three-node-e2e/SKILL.md)；
测试只能串行运行，环境复用仍会重启和替换节点。
完整生命周期及人工验收要求见[生产上线门槛](docs/PRODUCTION_LAUNCH_PLAN.md#验证门槛)。

客户端压测从仓库根目录运行，完整参数由脚本帮助维护：

```powershell
pwsh -NoProfile -File samples/Game.Unity.Agar/client-stress.ps1 -Help
```
