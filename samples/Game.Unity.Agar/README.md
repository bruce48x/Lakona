# Lakona.Game 游戏样例

这个样例用于验证 Lakona.Game 在轻量多人对战游戏中的接入方式。它同时包含本地单机、RPC 联机、控制连接、实时连接、可靠业务推送，以及基于 Lakona.Actor 的进程内状态执行路径。

## 文档入口

- [玩法与架构设计](docs/GAMEPLAY_DESIGN.md)（总索引，各功能子文档在 `docs/features/` 下）
- [开发计划](docs/DEVELOPMENT_PLAN.md)
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
 │  └─ Interfaces
 │     └─ IPlayerService.cs
 ├─ Server
 │  ├─ State.Contracts
 │  │  └─ Leaderboard
 │  ├─ State
 │  │  ├─ Leaderboard
 │  │  ├─ Matchmaking
 │  │  ├─ Rooms
 │  │  ├─ Sessions
 │  │  └─ Users
 │  ├─ Gateway
 │  │  ├─ Realtime
 │  │  │  └─ RoomRuntime.cs
 │  │  └─ Services
 │  │     └─ PlayerService.cs
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

关键职责：

- `Shared/Gameplay/ArenaSimulation.cs`：玩法规则内核，单机和联机共用。
- `Shared/Interfaces/IPlayerService.cs`：客户端和服务端共用的 RPC 协议。
- `Server/Gateway/Services/PlayerService.cs`：控制面 RPC 网关服务。
- `Server/Gateway/Realtime/RoomRuntime.cs`：服务端房间模拟和世界状态广播。
- `Server/State/StateStores.cs`：把 sample 业务状态接入 Lakona.Actor actor runtime。
- `Server/State/Users/UserActor.cs`：用户登录、资料和胜利积分状态。
- `Server/State/Leaderboard/LeaderboardActor.cs`：胜利积分排行榜周期、排序和归档。
- `Client/Assets/Scripts/Gameplay/DotArenaGame.cs`：客户端主流程、输入、渲染、模式切换和网络会话编排。
- `Client/Assets/Scripts/Gameplay/DotArenaNetworkSession.cs`：客户端控制连接、实时连接和重连参数封装。

相关单元测试位于 `samples/Game.Unity.Agar/tests/BusinessLogic.Tests`。仓库根目录 `Tests` 目录只包含 Lakona.Game 框架测试。

## 运行方式

启动网关服务即可。用户、会话、匹配、房间和排行榜状态当前都在同一个 Gateway 进程内通过 Lakona.Actor 串行执行。

```powershell
dotnet run --project Server/Gateway/Gateway.csproj
```

然后用 Unity 打开 `Client` 目录，运行游戏场景。

## 开发命令

共享协议变更后，不再手动生成 RPC 源码。服务端 `dotnet build` 会通过 `Lakona.Rpc.Analyzers` 生成服务端绑定；Unity 客户端重新编译时会通过 `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs` 中的 assembly 标记生成客户端 API。

常用构建和测试命令：

```powershell
dotnet build Shared/Shared.csproj -f net10.0
dotnet build Server/State/State.csproj
dotnet build Server/Gateway/Gateway.csproj
dotnet test tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

### Core Runtime Model

- Actor state: `Server/State/*/*Actor.cs` owns user, session, room, matchmaking, and leaderboard state behind the Lakona.Game actor facade.
- Hotfix rules: `Server/Hotfix/Gameplay/*System.cs` contains reloadable gameplay rules invoked through stable wrappers.
- RPC services in `Server/Gateway/Services` are adapters; they should not own long-lived mutable game state directly.

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
