# Lakona.Game 游戏样例

这个样例用于验证 Lakona 在轻量多人对战游戏中的接入方式。

它同时包含本地单机、多人联机、可靠业务推送、三节点 Cluster 拓扑。

## 文档入口

- [玩法与架构设计](docs/GAMEPLAY_DESIGN.md)（总索引，各主题子文档在 `docs/topics/` 下）
- [生产上线规划](docs/PRODUCTION_LAUNCH_PLAN.md)

`README.md` 只保留项目入口、运行方式和代码索引；玩法规则、胜利积分系统、客户端服务端边界、联机流程和分布式架构判断都放在 `docs/topics/` 下的主题子文档里，避免重复维护。

## 样例内容

玩家控制一个小球，在方形场地中移动、吃食物成长，并吞掉体型足够小的其他玩家。单局按时间结束，质量更高的玩家获胜；局内排名和展示只使用质量，并按整型展示。

当前客户端提供两个入口：

- 单机：不连接服务器，客户端本地运行完整玩法模拟，适合离线验证和快速调试。
- 联机：连接网关，登录后进入匹配；服务端按 20Hz 组帧，并以 5Hz、有上限的连续批次转发输入。客户端用同一随机种子和连续帧在本地推进确定性模拟；短时丢包会由后续批次补齐，不会触发无上限重发。

联机入口还验证短时网络切换恢复：WS 控制连接和 KCP 实时连接会建立新的
RPC Session，同时恢复原 Game Session；控制端 reliable push 会在恢复后的
下一次框架 heartbeat 中按序补发；KCP 重连会先取得对局启动参数和有界帧历史，再追上实时输入帧。

基础操作：

- `W/A/S/D` 控制移动。

## 代码位置

```txt
samples/Game.Unity.Agar
 ├─ Shared
 │  ├─ Gameplay
 │  │  ├─ ArenaConfig.cs
 │  │  ├─ ArenaSimulation.cs
 │  │  ├─ FrameSyncSimulation.cs
 │  │  └─ VictoryPointAwards.cs
 │  ├─ Interfaces
 │  │  └─ IPlayerService.cs
 │  └─ State
 │     ├─ Leaderboard
 │     ├─ Users
 │     └─ MatchmakingContracts.cs
 ├─ Server
 │  ├─ App
 │  │  ├─ Program.cs
 │  │  └─ State
 │  │     ├─ Leaderboard
 │  │     ├─ Matchmaking
 │  │     ├─ Rooms
 │  │     └─ Users
 │  └─ Hotfix
 │     ├─ Timers
 │     ├─ Gameplay
 │     └─ Services
 │        └─ PlayerService.cs
 ├─ Client
 │  └─ Assets
 │     └─ Scripts
 │        ├─ Gameplay
 │        │  ├─ DotArenaGame.cs
 │        │  └─ DotArenaNetworkSession.cs
 │        └─ Rpc
 ├─ docker-compose.yml
 └─ infra
```

目录职责：

- `Server/App`：strict host shell、generated binding、actor state shells。
- `Server/Hotfix`：RPC services、lifecycle handlers、actor behaviors、timer callbacks、matchmaking ticks、frame relay、settlement metadata。
- `Shared`：client and server 共用的 DTOs，以及客户端使用的确定性模拟与帧同步模块。

关键职责：

- `Shared/Gameplay/ArenaSimulation.cs`：玩法规则内核，单机和联机共用。
- `Shared/Gameplay/FrameSyncSimulation.cs`：联机客户端的启动、乱序缓冲、连续帧推进和确定性随机数协议。
- `Shared/Interfaces/IPlayerService.cs`：客户端和服务端共用的 RPC 协议。
- `Server/Hotfix/Operations/AgarOperationsHttpService.cs`：内网用户查询的 HTTP 路由、响应和可热更逻辑。
- `Server/Hotfix/Players/PlayerService.cs`：可热更的控制面 RPC 业务服务，直接编排 actor 行为。
- `Server/Hotfix/Matchmaking/MatchmakingTimerCallbacks.cs`：通过 LakonaTimer 驱动默认匹配队列的 periodic runtime loop。
- `Server/Hotfix/Rooms/BattleRuntimeTimerCallbacks.cs`：通过 LakonaTimer 向 room actor mailbox 投递 20Hz 组帧请求；不运行玩法模拟。
- `Server/App/Users/UserActor.cs`：用户资料和胜利积分的稳定状态 shell。
- `Server/App/Leaderboard/LeaderboardActor.cs`：胜利积分排行榜的稳定状态 shell。
- `Client/Assets/Scripts/Gameplay/DotArenaGame.cs`：客户端主流程、输入、渲染、模式切换和网络会话编排。
- `Client/Assets/Scripts/Gameplay/DotArenaNetworkSession.cs`：客户端控制连接、实时连接和重连参数封装。

相关单元测试位于 `samples/Game.Unity.Agar/tests/BusinessLogic.Tests`。仓库根目录 `Tests` 目录只包含 Lakona.Game 框架测试。

## 运行方式

单进程开发时启动默认网关服务即可。用户、会话、匹配、房间和排行榜状态通过 Lakona.Game.Server.Actors 串行执行；匹配、输入组帧、帧重放和结算元数据位于 `Server.Hotfix`，战斗计算位于 Unity 客户端，`Server.App` 只保留严格宿主、generated binding 和 actor state shell。

```powershell
dotnet run --project Server/App/Server.App.csproj
```

然后用 Unity 打开 `Client` 目录，运行游戏场景。

### OpenTelemetry

`Server.App` 默认注册 OpenTelemetry SDK，并通过 OTLP 导出 Lakona traces、
metrics、logs、.NET Runtime 指标和 Application HTTP 请求追踪。直接运行服务端时，
可用标准环境变量连接任意 OpenTelemetry Collector：

```powershell
$env:OTEL_SERVICE_NAME = "lakona-game-unity-agar"
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://127.0.0.1:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
dotnet run --project Server/App/Server.App.csproj
```

未设置 `OTEL_SERVICE_NAME` 时使用 `lakona-game-unity-agar`；每个进程的
`service.instance.id` 使用 `Lakona:Node:Id`。Docker Compose 默认把各节点遥测发送到
宿主机的 `http://host.docker.internal:4317`，也可在执行 `server-ctl.ps1 start` 前用
同名 `OTEL_*` 环境变量覆盖 endpoint、protocol 和 headers。Unity 客户端本身不引入
OpenTelemetry SDK；本节只配置服务端遥测。

Unity MCP 单节点验证脚本会优先复用已就绪的服务；如果本机 PostgreSQL
或 Redis 尚未启动，会自动调用 `server-ctl.ps1 start -Topology single`，使用完整
单节点 Compose 拓扑提供数据库、Redis 和游戏服务。验证完成后运行脚本的 `-Stop`
参数，只清理由该流程托管的本地服务。

本地 sample 拓扑可通过 `server-ctl.ps1` 统一管理。`start` 默认启动
`data-1`、`gateway-1`、`battle-1`、Postgres 和 Redis 三节点拓扑；使用
`-Topology single` 时启动一个承载全部 Actor 和两个客户端 endpoint 的
`single-1` 节点：

```powershell
./server-ctl.ps1 start
./server-ctl.ps1 start -Topology single
./server-ctl.ps1 status
./server-ctl.ps1 status -Topology single
./server-ctl.ps1 logs
./server-ctl.ps1 logs gateway-1
./server-ctl.ps1 stop
./server-ctl.ps1 help
```

`start` 默认使用 `-Topology three`，构建镜像并启动完整拓扑，随后轮询三个节点各自的
`/_lakona/health/ready`；只有 `data-1`、`gateway-1` 和 `battle-1` 都返回
HTTP `200` 才报告成功。`-Topology single` 只等待 `single-1` ready；切换
拓扑时会先停止另一拓扑的 Lakona 节点，避免 endpoint 端口冲突。已有镜像无需重新构建时可使用
`./server-ctl.ps1 start -NoBuild`。`logs` 默认显示最近 200 行并持续跟随，使用
`-NoFollow` 仅查看当前日志，或在命令后指定一个或多个 Compose service。
`stop` 会移除容器和网络，但保留 PostgreSQL、Redis volume 中的业务数据。
`start` 会先停止所有 Agar 游戏节点，再使用本地开发数据库账号重复执行 Lakona 唯一的
`database/postgresql/membership.sql`，最后才启动所选拓扑。生产环境中的游戏进程不应
拥有建表权限；应由独立部署任务在所有节点停止后执行同一份 SQL。

该拓扑由 `docker-compose.yml` 定义。三个节点共享同一个环境专属的 PostgreSQL Membership Table；它们不需要静态 peer 列表，也不会在游戏进程之间选举 membership leader。节点先以 Joining 身份登记，完成双向连通与恢复检查后才成为 Active。Actor Directory 根据 Membership view 管理内存中的虚拟分区，节点变化时直接迁移目录范围，迁移中断时从存活 activation 恢复；session id 自带精确 gateway locator。PostgreSQL 在这里承担两种边界清晰的职责：Membership Table 保存框架节点元数据，而 Agar 业务表保存用户状态；只有 `data-1` 获得 Agar 业务连接串和 Redis 客户端，`gateway-1` 与 `battle-1` 只获得 membership 连接串，不会创建业务数据库客户端。

直接在本机运行 `./server-ctl.ps1 start` 时，battle KCP endpoint 默认向宿主机客户端广告 `127.0.0.1:20001`。如果 Unity 运行在另一台机器，可在启动前设置 `AGAR_BATTLE_ADVERTISED_HOST` 为 Docker 主机可达的 IP 或 DNS 名称。

### 内网 HTTP 用户查询

`data-1` 提供一个完全声明并实现在 Hotfix 内的 Application HTTP 示例：

```text
GET /internal/users/{account}
```

单进程配置监听 `127.0.0.1:21000`。账号登录过并写入 PostgreSQL 后，可以查询：

```powershell
curl.exe http://127.0.0.1:21000/internal/users/guest-example
```

成功响应只包含账号、登录次数、创建/最后登录时间、胜场和胜点，不返回密码哈希、
Session Token、连接 id 或其他凭据。未知账号返回 `404`，非法 account 返回
`400`。

Docker Compose 默认也只把该端口发布到宿主机回环地址。需要让内网运营系统访问时，
把发布地址显式设置为 Docker 主机的私网地址：

```powershell
$env:AGAR_OPERATIONS_BIND_HOST = "192.168.1.20"
$env:AGAR_OPERATIONS_PORT = "21000"
./server-ctl.ps1 start
```

然后从内网访问
`http://192.168.1.20:21000/internal/users/{account}`。Lakona 不把被动的
“Public/Internal”标签当作安全边界；该首版示例没有内置认证或 TLS，不能直接暴露到
公网，生产环境仍需绑定地址、私网 ACL、防火墙或可信内部代理保护。

### 本地三节点一键验收

本地开发机可以用脚本启动真实三节点拓扑并通过 Unity PlayMode 测试驱动现有客户端：

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

脚本会启动 Docker Compose 中的 `data-1`、`gateway-1`、`battle-1`、Postgres 和 Redis，然后用 `Client` Unity 项目运行 PlayMode smoke test。测试会走现有 Unity 客户端流程：游客登录、开始匹配、连接 KCP 实时节点、接收输入帧并在客户端生成 world state。

需要验收完整的 20 客户端生命周期时，指定专用 PlayMode 测试：

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 `
  -TestFilter "SampleClient.Gameplay.Tests.DotArenaTwentyClientLifecyclePlayModeTests.TwentyClientsCompleteMatchBattleSettlementAndLeaderboard" `
  -TimeoutSeconds 600
```

该测试会创建 20 个独立 WebSocket/KCP 客户端，形成两个 10 人房间，持续提交真实战斗输入直至 120 秒回合结束，然后按最终 world state 独立计算结算账本。测试会等待排行榜写入收敛，严格核对排名、胜点和胜场，再重新登录全部账号验证 Profile 持久化。成功时会额外生成 `.tmp/agar-three-node/lifecycle-report.json`，其中包含 20 名玩家的房间、最终排名、期望/实际胜点、期望/实际胜场和最终排行榜。

要求本机已安装 Docker 和 Unity 2022 LTS。如果 Unity 不在默认 Unity Hub 目录，可显式传入路径：

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 -UnityPath "$env:ProgramFiles\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
```

常用参数：

- `-TimeoutSeconds <seconds>`：设置 Docker readiness 和 Unity PlayMode 测试的总等待时间，默认 600 秒。
- `-ProjectName <name>`：指定隔离的 Docker Compose project name，默认 `lakona-agar-three-node-test`。
- `-TestFilter <full-name>`：选择要运行的 Unity PlayMode 测试；默认仍运行单客户端 multiplayer smoke。
- `-ReuseEnvironment`：复用已存在的容器和 volume，不在启动前执行清理。
- `-SkipBuild`：复用已有镜像，不让 Docker Compose 重新 build。
- `-KeepEnvironment`：测试结束后保留容器和 volume，便于排查。

脚本会确保 `gateway-1` 和 `battle-1` 的客户端广告地址都是 `127.0.0.1`，让宿主机上的 Unity 客户端可以连接；cluster endpoint 仍然走 Compose 网络内的节点地址，成员发现统一通过 PostgreSQL Membership Table 完成。

### 跨平台客户端多实例压测

在 Windows、macOS 或 Linux 压测机安装 Unity 2022.3 后，可以一键构建当前平台的客户端并启动 10 个无图形实例：

```powershell
pwsh -NoProfile -File samples/Game.Unity.Agar/client-stress.ps1 `
  -InstanceCount 10
```

每个实例会使用游客账号自动登录、进入匹配，并在一局结束后继续匹配。默认构建产物和独立实例日志位于
`artifacts/agar-client-stress/`。Windows 产物为 `.exe`，macOS 产物为 `.app`，Linux 产物为无扩展名可执行文件。
首次构建后可传 `-SkipBuild` 复用客户端；只构建不启动时使用
`-BuildOnly`；需要观察窗口时使用 `-ShowWindow`（仍兼容旧参数 `-ShowWindows`）。Unity 不在默认 Hub 目录时传入
`-UnityPath <Unity.exe>` 或设置 `UNITY_PATH`。未传连接参数时，脚本会读取
`Server/App/appsettings.json` 中第一个 WebSocket endpoint 的 Host、Port 和 Path；远程环境可用
`-Host <网关地址> -Port <网关端口>` 覆盖。远程部署时，服务端广告的 KCP 地址也必须能从压测机访问。

启动完成后，脚本默认保持运行并周期显示每个实例的 PID、运行时长、登录/匹配/战斗/结算状态、当前 tick、
单局进度、排行榜结果和最后日志时间。战斗 tick 超过 30 秒没有推进时会显示 `Stalled`。按 `Ctrl+C` 会停止
本次脚本启动的全部客户端；可用 `-DurationSeconds 300` 做五分钟定时压测，使用
`-StatusIntervalSeconds 10` 调整刷新间隔。需要恢复原先的后台启动方式时使用 `-Detach`。每次运行的日志写入
`artifacts/agar-client-stress/logs/yyyyMMdd-HHmmss-fff-PID/client-XXXX.log`，避免读取到上一次压测的旧状态。
运行 `./client-stress.ps1 --help`、`./client-stress.ps1 -h` 或 `./client-stress.ps1 -Help` 可查看完整参数说明和示例。
使用 `-Detach` 后，可随时运行 `./client-stress.ps1 -StopRun`，自动发现并停止本机全部仍在运行的 Agar 压测客户端；
该命令不需要服务器在线，也不会触发 Unity 构建。

失败时脚本会把 Unity 日志、测试结果和 Docker Compose 日志写到 `.tmp/agar-three-node/`，主要包括 `TestResults.xml`、`unity-editor.log`、`docker-compose.log` 和 `docker-compose.ps.json`；完整生命周期测试还会生成 `lifecycle-report.json`。脚本会在失败摘要中标出阶段，例如 Docker 不可用、Unity 未找到、Postgres 未 healthy、gateway 端口不可达、登录未进入多人大厅、KCP 实时连接未 attach、或未收到 world state。

### Actor 调用语义

RPC services and actor behaviors use generated behavior-first actor selectors.
`HotfixServiceCall.Actors` is the node-local actor runtime for the process that
is currently executing the RPC service or actor behavior. It is only for
current-node work. When actor placement may be local or remote, Agar code should
call the generated typed selector instead of treating the node-local runtime as
a distributed actor facade.

`Route(id)` is the default business path and owns actor-directory lookup plus
node selection. `Local(id)` is reserved for code that has already proved
current-node ownership, such as battle runtime room input after realtime
attach validation.

`HotfixServiceCall.Actors` and `self.Context.Runtime` remain framework escape
hatches. Agar business code must not use raw `AskAsync` or `TellAsync` for
ordinary actor behavior calls.

```csharp
await actors.Route<RoomActor>(roomId).CallAsync(static behavior => behavior.JoinAsync, request, ct);    // 默认业务路径，由 Route 负责查目录和选节点
await actors.Local<RoomActor>(roomId).PostAsync(static behavior => behavior.RunFrameAsync, request, ct); // 已确认本地归属后，只在当前节点组帧并转发
await actors.Startup<MatchmakingActor>(queueId).CallAsync(static behavior => behavior.EnqueueAsync, request, ct); // 按固定 selector 选择就绪副本
```

Matchmaking 是 Startup service group 的示例。`HotfixStartup.ConfigureActors`
注册带 `MatchmakingQueueId` key 的固定 selector；每个允许托管 `matchmaking`
的节点创建一个副本，并从 `[ActorStart]` 创建 LakonaTimer periodic timer。
key 只用于选择亲和性，不是物理 actor id。当前三节点拓扑只有 `data-1`
具备该能力；增加第二个 capable 节点即可增加副本。故障切换不会复制内存队列，
队列允许清空。RPC service 不应在 enqueue/cancel 前调用 `EnsureCreatedAsync`。

本地 `docker-compose.yml` 会把 `infra/postgres/init` 挂载到 Postgres
`/docker-entrypoint-initdb.d`，其中 `001-agar-users.sql` 创建用户持久化表。
`Server.App` 通过 Dapper + Npgsql 持久化用户，并通过 Redis sorted set/hash
持久化排行榜；`AgarPostgresModule` 和 `AgarRedisModule` 由 Lakona
自动发现，在最终 DI provider 创建前注册稳定 adapter，并在启动阶段建立连接。
模块始终提供 Hotfix 构造所需的稳定 Store 接口，但只在对应连接字符串存在时创建
客户端和建立连接；未配置时使用不会连接外部资源的 fail-fast adapter，并视为模块
启动成功。如果业务被错误路由到该节点，adapter 会报告明确的拓扑错误。已配置的
连接或 schema 初始化失败时，节点不会加载初始 Hotfix、发布 Ready 或打开 RPC
监听器。Redis multiplexer 连接成功后作为最终根 DI provider 中的唯一 singleton
提供，排行榜 Store 只注入 `IDatabase`，不依赖生命周期 Module。Lakona
cluster 不创建 SQL directory schema；生产业务表仍应通过受控迁移更新。

## 开发命令

共享协议变更后，不再手动生成 RPC 源码。服务端 `dotnet build` 会通过 `Lakona.Rpc.Analyzers` 生成服务端绑定；Unity 客户端重新编译时会通过框架默认的 source generator 配置生成 `Client.Generated` 客户端 API，项目中不再包含本地 RPC 生成标记文件。

常用构建和测试命令：

```powershell
dotnet build Shared/Shared.csproj -f net10.0
dotnet build Server/App/Server.App.csproj
dotnet build Server/Hotfix/Server.Hotfix.csproj
dotnet test tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

`Server/Hotfix` 构建会把 `Server.Hotfix.dll`、PDB 和 deps 文件复制到
`Server/App/bin/<Configuration>/net10.0/hotfix/`，并最后写入 `reload.signal`。
如果只重新构建或运行 `Server/App`，运行中的服务可能仍然加载旧的 hotfix 快照。

### Core Runtime Model

- Stable actor state lives with its business module under `Server/App/{Users,Rooms,Matchmaking,Leaderboard}`; PostgreSQL owns durable user profiles and Redis owns durable leaderboard rankings.
- Reloadable behavior, notifications, and timer callbacks live together under the matching `Server/Hotfix/<Module>` directory.
- RPC and HTTP ingress adapters live in their owning Hotfix modules; App-side RPC configurators bind generated stable proxies to hotfix dispatch.

## 当前状态

已完成：

- 单机与联机双入口。
- 单机和联机共用同一套玩法规则。
- 成长、吞噬、复活、AI 补位和胜负判定。
- 控制连接和实时连接的联机样例。
- 登录重连参数、可靠业务推送和玩家碰撞表现。
- 旧 dash / buff 协议清理；客户端输入提交移动意图和 `LastReceivedServerTick`，权威输入 tick 由服务端组帧时决定，缺失帧按接收游标批量补发。
- 服务端胜利积分、周榜查询、最近两周归档和客户端真实排行榜展示。
- 自动化测试 31 个，覆盖模拟规则、匹配队列、会话清理和胜利积分基础规则。

仍需继续验证：

- Unity 编辑器内完整单机流程回归。
- 联机模式下 UI 交互、积分发放、排行榜刷新和视觉细节的最终打磨。
- 跨网关实时路由设计与实现。
