# Lakona.Game 游戏样例

这个样例用于验证 Lakona 在轻量多人对战游戏中的接入方式。

它同时包含本地单机、多人联机、可靠业务推送、三节点 Cluster 拓扑。

## 文档入口

- [玩法与架构设计](docs/GAMEPLAY_DESIGN.md)（总索引，各功能子文档在 `docs/features/` 下）
- [生产上线规划](docs/PRODUCTION_LAUNCH_PLAN.md)

`README.md` 只保留项目入口、运行方式和代码索引；玩法规则、胜利积分系统、客户端服务端边界、联机流程和分布式架构判断都放在 `docs/features/` 下的功能子文档里，避免重复维护。

## 样例内容

玩家控制一个小球，在方形场地中移动、吃食物成长，并吞掉体型足够小的其他玩家。单局按时间结束，质量更高的玩家获胜；局内排名和展示只使用质量，并按整型展示。

当前客户端提供两个入口：

- 单机：不连接服务器，客户端本地运行完整玩法模拟，适合离线验证和快速调试。
- 联机：连接网关，登录后进入匹配，由服务端推进房间模拟并推送世界状态。

基础操作：

- `W/A/S/D` 控制移动。

## 代码位置

```txt
samples/Game.Unity.Agar
 ├─ Shared
 │  ├─ Gameplay
 │  │  ├─ ArenaConfig.cs
 │  │  ├─ ArenaSimulation.cs
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
 │     ├─ Features
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
- `Server/Hotfix`：RPC services、lifecycle handlers、actor behaviors、hotfix feature descriptors、matchmaking ticks、room ticks、settlement。
- `Shared`：client and server 共用的 DTOs，以及 reload-safe simulation state。

关键职责：

- `Shared/Gameplay/ArenaSimulation.cs`：玩法规则内核，单机和联机共用。
- `Shared/Gameplay/ArenaSimulationState.cs`：服务端房间 tick 可跨 hotfix reload 保留的模拟状态。
- `Shared/Interfaces/IPlayerService.cs`：客户端和服务端共用的 RPC 协议。
- `Server/Hotfix/Services/PlayerService.cs`：可热更的控制面 RPC 业务服务，直接编排 actor 行为。
- `Server/Hotfix/Features/MatchmakingFeature.cs`：声明 `matchmaking` 业务 feature；`matchmaking` feature 拥有默认匹配队列 actor 的固定 tick。
- `Server/Hotfix/Features/BattleRuntimeFeature.cs`：声明 battle runtime 节点上的活跃房间 actor tick。
- `Server/App/State/Users/UserActor.cs`：用户资料和胜利积分的稳定状态 shell。
- `Server/App/State/Leaderboard/LeaderboardActor.cs`：胜利积分排行榜的稳定状态 shell。
- `Client/Assets/Scripts/Gameplay/DotArenaGame.cs`：客户端主流程、输入、渲染、模式切换和网络会话编排。
- `Client/Assets/Scripts/Gameplay/DotArenaNetworkSession.cs`：客户端控制连接、实时连接和重连参数封装。

相关单元测试位于 `samples/Game.Unity.Agar/tests/BusinessLogic.Tests`。仓库根目录 `Tests` 目录只包含 Lakona.Game 框架测试。

## 运行方式

单进程开发时启动默认网关服务即可。用户、会话、匹配、房间和排行榜状态通过 Lakona.Game.Server.Actors 串行执行；业务决策、匹配 tick、房间 tick、结算和状态变更位于 `Server.Hotfix`，`Server.App` 只保留严格宿主、generated binding 和 actor state shell。

```powershell
dotnet run --project Server/App/Server.App.csproj
```

然后用 Unity 打开 `Client` 目录，运行游戏场景。

三节点 sample 拓扑可通过 `docker-compose.yml` 启动 `data-1`、`gateway-1`、`battle-1`、Postgres 和 Redis。`data-1` 使用 `Lakona:Cluster:Directory` 把 cluster node directory 接到 Postgres，并在 data 进程内提供共享 route directory；Agar 自身持久化配置位于 `Agar:Persistence`。`gateway-1` 和 `battle-1` 通过 `Lakona:Cluster:Seeds` 使用 seeded directory clients 访问 data 节点。远程客户端通知通过 battle/data 侧的 `ClusterClientNotificationDispatcher` 调用 gateway cluster endpoint 上的 binder，再由 gateway 的本地 session callback 发给客户端。当前阶段 Postgres 用于 cluster membership；route directory 是 sample V1 的 data-local in-memory 实现；完整 gameplay state 持久化和 Redis 排行榜索引仍是后续 sample 工作，不应把回调对象或会话 callback 状态写入 Postgres/Redis。

### Actor 调用语义

样例里的 `call.Actors`、`services.LocalActors` 和 actor behavior 中的
`self.Context.Runtime` 都是当前进程的本地 actor runtime（node-local actor runtime）。RPC service 从 `call.Actors` 直接取出的本地 runtime 命名为
`nodeLocalActors`；从依赖聚合传递的本地 runtime 命名为
`services.LocalActors`。这类调用只投递到当前节点的本地 mailbox，不会自动跨节点路由。

RPC service 编排业务 actor 时，应假设目标 actor may be local or remote。
需要表达分布式 actor 放置时，使用生成的 typed selector：

```csharp
await rooms.Get(roomId).JoinAsync(request, ct);            // 先查本地，再通过 ActorDirectory 路由
await rooms.Local(roomId).JoinAsync(request, ct);          // 只调当前节点
await rooms.Remote(nodeId, roomId).JoinAsync(request, ct); // 固定调指定远端节点
```

Matchmaking 是 remote-capable actor 的示例。单进程默认配置启用
`matchmaking` feature，`Server.Hotfix` 中的 `MatchmakingFeature` 声明
`MatchmakingActor("default")` 的本地 actor 创建和固定 tick。固定 actor tick 只向
已经存在的 actor 投递 tick，不负责隐式创建；默认匹配队列 actor 的创建由 feature
lifecycle 中的显式 actor 声明完成。三节点拓扑中 `data-1` 启用 `matchmaking`
feature，因此默认匹配队列属于 data 节点。RPC service 不应在每次 enqueue/cancel
前调用 `EnsureCreatedAsync`；创建、放置和迁移是 feature/业务 lifecycle 的职责，
普通调用只应该路由到已经存在的 actor。

本地 `docker-compose.yml` 会把 `infra/postgres/init` 挂载到 Postgres
`/docker-entrypoint-initdb.d`，其中 `001-lakona-cluster-nodes.sql` 创建
Lakona cluster node directory 表，`002-dapper-grain-storage.sql` 创建 sample
状态表。`data-1` 启动时默认只验证 schema 是否可用，不执行建表；只有显式设置
`Lakona:Cluster:Directory:EnsureSchemaOnStartup=true` 时才会用当前连接执行
`CREATE TABLE IF NOT EXISTS`。这个开关只用于本地开发、测试或一次性 admin
bootstrap，不是生产运行建议。已有旧本地 Postgres volume 的开发环境不会自动补跑
新的 init SQL；可重建本地 volume、用 admin-capable 账号手动执行
`001-lakona-cluster-nodes.sql`，或临时用上述开关做一次 admin/bootstrap 启动。

## 开发命令

共享协议变更后，不再手动生成 RPC 源码。服务端 `dotnet build` 会通过 `Lakona.Rpc.Analyzers` 生成服务端绑定；Unity 客户端重新编译时会通过 `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs` 中的 assembly 标记生成客户端 API。

常用构建和测试命令：

```powershell
dotnet build Shared/Shared.csproj -f net10.0
dotnet build Server/App/Server.App.csproj
dotnet build Server/App/Server.App.csproj
dotnet test tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

### Core Runtime Model

- Actor state: `Server/App/State/*/*Actor.cs` owns user, session, room, matchmaking, and leaderboard state behind the Lakona.Game actor facade.
- Hotfix rules: `Server/Hotfix/Gameplay/*Behavior.cs` contains reloadable gameplay behavior invoked through stable wrappers.
- RPC business services live in `Server/Hotfix/Services`; App-side RPC configurators bind generated stable proxies to hotfix dispatch.

## 当前状态

已完成：

- 单机与联机双入口。
- 单机和联机共用同一套玩法规则。
- 成长、吞噬、复活、AI 补位和胜负判定。
- 控制连接和实时连接的联机样例。
- 登录重连参数、可靠业务推送和玩家碰撞表现。
- 旧 dash / buff 协议清理，输入只保留移动方向和 tick。
- 服务端胜利积分、周榜查询、最近两周归档和客户端真实排行榜展示。
- 自动化测试 31 个，覆盖模拟规则、匹配队列、会话清理和胜利积分基础规则。

仍需继续验证：

- Unity 编辑器内完整单机流程回归。
- 联机模式下 UI 交互、积分发放、排行榜刷新和视觉细节的最终打磨。
- 跨网关实时路由设计与实现。
