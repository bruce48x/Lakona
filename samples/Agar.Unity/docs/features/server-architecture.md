# 服务端架构

这份文档描述服务端网关和状态服务的职责边界、联机流程和分布式设计。核心玩法见 [`gameplay-rules.md`](gameplay-rules.md)，胜利积分系统见 [`victory-points.md`](victory-points.md)。

## 服务端边界

`samples/Agar.Unity/Server/Gateway` 是 RPC 网关和房间运行时宿主。

当前职责：

- 控制面 RPC：登录、登出、匹配和低频业务接口。
- 实时面 RPC：对局输入和实时会话绑定。
- 维护网关本地的在线会话和回调对象。
- 通过 `RoomRuntimeHost` 和 `RoomRuntime` 承载房间运行时。
- 推送世界状态、死亡事件、结算事件和匹配状态。
- 对局结束时按排名发放胜利积分到用户状态服务。

`samples/Agar.Unity/Server/State` 承载状态服务。

当前职责：

- 用户身份和胜场持久化。
- 用户胜利积分持久化（`UserState.VictoryPoints`）。
- 玩家会话状态。
- 匹配队列状态。
- 房间分配和房间快照状态。
- 排行榜聚合查询与 Redis 排行榜索引协调。

PostgreSQL 是状态持久化后端，状态服务通过 sample-local Dapper storage provider 读写。

排行榜服务职责：

- 接收排行榜查询请求，读取 Redis sorted set 中由结算写入维护的排行榜积分索引。
- 接收结算后的 `RecordVictoryPointsAsync` 写入，更新 Redis 当前周期胜利积分 zset、胜场索引和玩家快照。
- 从 Redis 取候选集合后，按积分降序、胜场降序、玩家标识升序排序后返回 top N。
- 榜单当地时间周一 00:00 触发重置：归档上周 Redis top 100，切换当前周期 key，并按数据模型要求同步处理用户状态中的当前周期胜利积分。
- 排行榜服务自身只保留周期协调和兼容所需状态，不再把完整排行榜排序索引作为主数据源。

## Docker 部署边界

生产环境目标使用 Docker 部署。当前 `docker-compose.yml` 只作为本地开发基础设施，负责启动 PostgreSQL 和 Redis；生产部署还需要补齐 gateway 和状态服务镜像、生产 compose 配置、健康检查、日志、密钥和回滚流程。

生产 Docker 拓扑的目标形态：

- `state` 容器运行 `Server/State/State.csproj` 的发布产物，承载状态服务。
- `gateway` 容器运行 `Server/Gateway/Gateway.csproj` 的发布产物，承载控制面 RPC、实时 RPC 和房间运行时。
- `postgres` 容器或托管 PostgreSQL 保存持久化状态，必须使用持久化 volume 或外部数据库。
- `redis` 容器或托管 Redis 用于胜利积分排行榜 sorted set；后续也可承载跨网关路由或在线状态，必须启用密码和持久化策略。
- 可选反向代理或负载均衡负责 WebSocket/TLS 入口；KCP 实时端口需要按传输要求单独暴露。

生产配置必须通过环境变量、env 文件或部署平台 secret 注入，不把生产连接串、数据库密码、Redis 密码、token secret 或公网主机名写死在 `appsettings.json` 中。

## 联机流程

控制连接流程：

1. 客户端连接控制面 RPC。
2. 客户端登录。
3. 客户端发起匹配。
4. 网关调用匹配服务。
5. 匹配和房间分配服务分配房间与运行时网关。
6. 网关可靠推送匹配状态，并携带实时连接信息。

实时连接流程：

1. 客户端打开实时 RPC 连接。
2. 客户端用玩家、会话、房间和对局令牌调用 `AttachRealtimeAsync`。
3. 运行时网关登记实时回调。
4. 客户端通过实时 RPC 发送输入。
5. 房间运行时通过实时回调广播世界状态。

排行榜查询流程：

1. 客户端在登录后或模式入口界面通过控制面 RPC 请求排行榜。
2. 网关将请求转发到排行榜服务。
3. 排行榜服务检查当前周期（若已过周一 00:00 则触发重置）。
4. 排行榜服务从 Redis sorted set 读取候选集合，按排行榜口径排序后返回 top N。
5. 网关将结果返回客户端渲染。

## 联机同步边界

- 客户端发送输入。
- 服务端推进模拟。
- 服务端广播快照。
- 客户端对玩家位置做插值，减少快照跳动。

客户端输入消息包含：

```txt
InputMessage
{
    playerId
    moveX
    moveY
    tick
}
```

服务端广播的世界状态包含：

```txt
WorldState
{
    tick
    respawnDelaySeconds
    players[]
    pickups[]
}
```

`players[]` 包含位置、速度、生死状态、整型质量、半径和移动速度。战斗内实时排名只读取并展示整型质量，单机和联机都不再展示独立分数字段；协议和代码里不得保留旧分数字段。`pickups[]` 描述当前仍在地图上的食物。

## 分布式边界

已经分布式或持久化的部分：

- 持久化状态通过 Dapper 写入 PostgreSQL。
- 匹配队列状态在状态服务中。
- 房间分配携带明确的运行时网关信息。
- 客户端收到明确的实时连接目标，不假设控制网关一定拥有房间。
- 实时绑定不再要求本地已有控制连接回调。
- 胜利积分存储在用户状态中，跨网关读写均通过状态服务。
- 排行榜查询通过控制面 RPC 进入网关，再转发到排行榜服务，由排行榜服务协调 Redis sorted set 查询。

仍然局限在单个网关进程内的部分：

- 活跃 RPC 回调对象。
- 活跃房间模拟。
- 世界状态广播扇出。
- 部分断线、登出和离房清理语义。

下一步分布式架构重点是网关到网关的输入和事件路由。候选方式包括 Redis 发布订阅、Lakona.Game.Cluster 或应用自定义消息总线。
