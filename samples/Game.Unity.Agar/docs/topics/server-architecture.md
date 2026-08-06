# 服务端架构

这份文档描述服务端网关和状态服务的职责边界、联机流程和分布式设计。核心玩法见 [`gameplay-rules.md`](gameplay-rules.md)，胜利积分系统见 [`victory-points.md`](victory-points.md)。

## 服务端边界

`samples/Game.Unity.Agar/Server/App` 是稳定宿主、RPC 网关、actor state shell、session 和 cluster infrastructure。

当前职责：

- 控制面 RPC：登录、登出、匹配和低频业务接口。
- 实时面 RPC：对局输入和实时会话绑定。
- 维护网关本地的在线会话和回调对象。
- 通过稳定 actor runtime 承载房间 actor；房间只拥有最近输入、客户端服务端帧接收游标、帧号、有界重放历史和成员状态。
- 以 20Hz 对最近输入组帧，并向实时会话转发启动参数和输入帧。
- 接受客户端确定性模拟产生的结算报告，并按报告排名发放胜利积分到用户状态服务。

`samples/Game.Unity.Agar/Server/App/{Users,Rooms,Matchmaking,Leaderboard}` 按业务模块承载稳定 actor、状态和持久化 port；具体数据库 adapter 位于 `Server/App/Infrastructure`。

当前职责：

- 用户身份和胜场持久化。
- 用户胜利积分持久化（`UserState.VictoryPoints`）。
- 玩家会话状态。
- 匹配队列状态。
- 房间分配和房间生命周期快照状态。
- 排行榜聚合查询、周期检查和 Redis 排行榜索引。

PostgreSQL 是用户状态持久化后端，稳定的 `PostgresUserStore` 通过
Dapper + Npgsql 读写；Npgsql data source 由 `Server.App` root provider
拥有，不随 Hotfix reload 重建。

排行榜服务职责：

- 接收排行榜查询请求，从 Redis sorted set/hash 读取由结算写入维护的排行榜积分索引。
- 接收结算后的 `RecordVictoryPointsAsync` 写入，更新当前周期胜利积分、胜场索引和玩家快照。
- 从 Redis 取候选集合后，按积分降序、胜场降序、玩家标识升序排序并返回 top N。
- 榜单当地时间周一 00:00 触发重置：切换当前周期 key，并重置上一周期 Redis 索引中玩家的 PostgreSQL 当前周期胜利积分。活跃用户通过 `UserActor` 串行更新；没有活动 actor 的历史用户直接从 `IUserStore` 加载并写回 PostgreSQL，不能阻塞排行榜查询。旧周期 key 的保留和清理由部署策略决定。
- Redis connection multiplexer 和 PostgreSQL data source 都由稳定层拥有，
  并由 `AgarPostgresModule`、`AgarRedisModule` 管理；任一连接或 PostgreSQL schema 初始化失败，
  节点都不会打开监听器或发布 cluster Ready。

## Docker 部署边界

当前 `Server/Dockerfile` 可以构建服务端镜像，`docker-compose.yml` 同时提供单节点 profile 和 `data-1`、`gateway-1`、`battle-1` 三节点拓扑，并启动 PostgreSQL 与 Redis。Compose 是本地验证拓扑，不等同于完成生产部署；生产仍需补齐 secret、TLS、外部域名、日志采集、监控告警、备份恢复和发布回滚策略。

生产 Docker 拓扑的目标形态：

- `data-1` 运行 `Server.App` 发布产物，托管 user、matchmaking 和 leaderboard actor，并连接 PostgreSQL/Redis。
- `gateway-1` 运行同一发布产物，承载 WebSocket 控制面 RPC 和 session delivery，不托管业务 actor。
- `battle-1` 运行同一发布产物，托管 room actor 和 KCP 实时 RPC。
- `postgres` 容器或托管 PostgreSQL 保存持久化状态，必须使用持久化 volume 或外部数据库。
- `redis` 容器或托管 Redis 保存胜利积分排行榜 sorted set/hash，必须启用密码和持久化策略；实时 callback 和房间路由不依赖 Redis。
- 可选反向代理或负载均衡负责 WebSocket/TLS 入口；KCP 实时端口需要按传输要求单独暴露。

生产配置必须通过环境变量、env 文件或部署平台 secret 注入，不把生产连接串、数据库密码、Redis 密码、token secret 或公网主机名写死在 `appsettings.json` 中。

## 联机流程

控制连接流程：

1. 客户端连接控制面 RPC。
2. 客户端登录。
3. 客户端发起匹配。
4. 网关调用匹配服务。
5. 匹配和房间分配服务分配房间与 actor owner 网关。
6. 网关可靠推送匹配状态，并携带实时连接信息。

实时连接流程：

1. 客户端打开实时 RPC 连接。
2. 客户端用玩家、会话、房间和对局令牌调用 `AttachRealtimeAsync`。
3. actor owner 网关登记实时回调。
4. 客户端通过实时 RPC 发送输入。
5. room actor 的 LakonaTimer periodic loop 以 20Hz 生成连续服务端帧，将每位玩家最近输入按座位和玩家标识稳定排序；每个客户端按自己报告的 `LastReceivedServerTick` 收到下一批缺失帧。
6. 客户端用启动随机种子和连续帧本地推进模拟；普通实时推送通过接收游标补齐缺帧，重连客户端从 `AttachRealtimeAsync` 回复中的有界帧历史重放到当前帧。

排行榜查询流程：

1. 客户端在登录后或模式入口界面通过控制面 RPC 请求排行榜。
2. 网关将请求转发到排行榜服务。
3. 排行榜服务检查当前周期（若已过周一 00:00 则触发重置）。
4. 排行榜服务从 Redis 读取当前周期候选集合，按 Hotfix 排行榜口径排序后返回 top N。
5. 网关将结果返回客户端渲染。

## 联机同步边界

- 客户端发送最近移动意图和 `LastReceivedServerTick`；该游标只确认已收到的服务端权威帧，不决定输入在哪个 tick 生效。
- 服务端校验实时会话身份，接受输入意图，并在组帧时以当前连续帧号写入 `ServerTick`；客户端输入不存在 stale-tick 拒绝路径。
- 服务端下一次推送使用单个 `FrameSyncPush` 返回区间 `(LastReceivedServerTick, LatestServerTick]` 内仍在有界历史中的全部帧。例如客户端报告 95、服务端最新为 100 时，批量推送 96 到 100。
- 客户端缓存乱序帧，只在拿到下一连续帧后推进 `ArenaSimulation`。
- 客户端从本地 `WorldState` 更新玩家、食物、死亡、胜负和渲染插值。

客户端输入消息包含：

```txt
InputMessage
{
    playerId
    moveX
    moveY
    addCheatMass
    lastReceivedServerTick
}
```

服务端在每个帧内为输入填写 `serverTick`，并以批次推送：

```txt
FrameSyncPush
{
    frames[]
        matchId
        frame
        inputs[]
            playerId
            moveX
            moveY
            serverTick
            addCheatMass
}
```

启动消息 `FrameSyncStart` 固定协议版本、match id、随机种子、步长、房间人数和真人座位。服务端不发送玩家位置、食物、质量、碰撞或胜负快照；这些状态由每个客户端从相同启动参数和帧流生成。帧历史上限为 4096，覆盖当前 120 秒、20Hz 回合并防止房间状态无界增长。

## 分布式边界

已经分布式或持久化的部分：

- 持久化状态通过 Dapper 写入 PostgreSQL。
- 匹配队列状态在状态服务中。
- 房间分配携带明确的运行时网关信息。
- 客户端收到明确的实时连接目标，不假设控制网关一定拥有房间。
- 实时绑定不再要求本地已有控制连接回调。
- 胜利积分存储在用户状态中，跨网关读写均通过状态服务。
- 排行榜查询通过控制面 RPC 进入网关，再转发到排行榜服务，由排行榜服务查询 Redis 索引。

仍然局限在单个网关进程内的部分：

- 活跃 RPC 回调对象。
- 活跃房间输入帧历史和广播扇出。
- 部分断线、登出和离房清理语义。

房间 actor 通过现有 activation directory 固定在 battle owner；实时 attach 已验证 current-node ownership，输入、组帧和回调扇出都在该 owner 上完成。跨节点调用继续使用 Lakona 生成的 actor selector，不另建 Redis 实时路由。
