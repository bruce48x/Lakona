# 胜利积分与排行榜

这份文档描述胜利积分规则、排行榜存储和跨场数据流。核心玩法见 [`gameplay-rules.md`](gameplay-rules.md)，服务端边界见 [`server-architecture.md`](server-architecture.md)。

## 积分规则

联机对局结束后，真人玩家根据客户端上报的最终排名获得胜利积分：

| 排名 | 胜利积分 |
| --- | --- |
| 第 1 名 | 10 |
| 第 2 名 | 7 |
| 第 3 名 | 5 |
| 第 4 名 | 3 |
| 第 5 名 | 1 |
| 第 6 名及以后 | 0 |

胜利积分跨对局累计并持久化到用户状态。单机不提交服务端结算；AI 不是房间真人成员，因此不会写入用户状态或排行榜。

排行榜按胜利积分降序、胜场数降序、玩家标识升序稳定排序。

## 结算边界

```txt
客户端 FrameSyncSimulation 计算最终 WorldState
  -> MatchSettlementRules 生成排名
  -> SubmitMatchResultAsync 上报 FrameSyncMatchResult
  -> RoomBehavior 校验 session、room、match、frame 和真人成员
  -> 用户状态服务持久化胜场与胜利积分
  -> LeaderboardBehavior 更新 Redis 排行榜索引
```

服务端不重放战斗，也不独立计算排名。`RoomBehavior` 只使用当前房间真人成员对应的结果项：

- 胜者必须是房间真人成员，才会增加胜场。
- 每名真人玩家最多处理一个结果项。
- `VictoryPointAwards` 按上报排名映射胜利积分。
- AI 或伪造的非成员玩家标识不会进入用户状态服务。

客户端结果仍是不可信输入。成员、帧号和会话校验只能阻止越权或过早提交，不能证明排名来自未篡改的模拟。

## 排行榜存储

用户资料和累计状态由 `PostgresUserStore` 通过 Dapper + Npgsql 写入 PostgreSQL。当前周期排行榜索引由 `RedisLeaderboardStore` 维护：

- `agar:leaderboard:{period}:scores`：sorted set，member 为 `playerId`，score 为当前周期胜利积分。
- `agar:leaderboard:{period}:wins`：hash，保存当前周期胜场。
- `agar:leaderboard:current-period`：当前周期的本地周一日期。

`LeaderboardBehavior` 从 Redis 加载候选集合，再按胜利积分、胜场和玩家标识进行稳定排序。Unity 客户端只通过控制面 `IPlayerService.GetLeaderboardAsync` 查询，不直接连接 Redis。

## 周期规则

排行榜以服务端宿主的当地时区计算周期，每周一 00:00 开始新周期。查询和写入都会先检查当前周期：

1. Redis 没有周期标识时，写入当前周期。
2. 周期未变化时，继续读写当前 key。
3. 周期变化时，加载上一周期 Redis 索引中的玩家并重置这些用户的 PostgreSQL 当前周期积分；若用户没有活动 `UserActor`，则通过 `IUserStore` 直接加载并保存持久化用户，数据库中已不存在的历史成员安全跳过。
4. 更新 `agar:leaderboard:current-period`，新写入进入新周期 key。

旧周期 Redis key 当前不会自动删除，可作为后续归档或保留策略的输入，但系统不承诺长期历史榜单。

`PeriodStartLocalDate` 是周期日期的实际语义。`PeriodStartUtc` 为现有 wire contract 的兼容字段，当前镜像同一个本地日期字符串；它不是 UTC 时间戳。

## 查询与刷新

客户端在登录、重连和联机结算后主动拉取排行榜。查询回复包含：

- 周期起始本地日期。
- 距离下次重置的秒数。
- 排名、玩家标识、胜利积分和胜场数。

排行榜不通过实时战斗通道推送。Redis 或 PostgreSQL 初始化失败时，托管这些依赖的节点不会发布 Ready。
