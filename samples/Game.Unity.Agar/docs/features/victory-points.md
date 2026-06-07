# 胜利积分与排行榜

这份文档描述胜利积分系统的规则、排行榜机制和跨场数据流。核心玩法规则见 [`gameplay-rules.md`](gameplay-rules.md)，服务端架构见 [`server-architecture.md`](server-architecture.md)。

## 积分规则

每场对局结束后，玩家根据最终排名获得"胜利积分"（Victory Points）。

积分规则（仅前五名获得）：

| 排名 | 胜利积分 |
|------|----------|
| 第1名 | 10 |
| 第2名 | 7 |
| 第3名 | 5 |
| 第4名 | 3 |
| 第5名 | 1 |
| 第6名及以后 | 0 |

胜利积分跨对局累计，记录在玩家持久化数据中。单机模式受限于没有真实对手，不发放胜利积分；所有以 `AI` 为前缀的 bot 玩家也不发放胜利积分。

## 排行榜

全体玩家的排行榜按胜利积分降序排列。积分相同则按胜场数降序，再相同则按玩家标识升序兜底。

排行榜周期：每周一当地时间 00:00 重置。这里的“当地时间”指榜单所属地区或目标玩家所在地区的本地时区；当前样例面向本地/区域玩家体验时，不应以 UTC 00:00 作为玩家可感知的重置点。重置时所有玩家的胜利积分归零，上一周期的排名数据存档到历史记录（最初可只保留最近两周）。

如果未来扩展为多地区排行榜，每个地区榜应使用各自的本地时区独立计算周期。服务端仍可以用 UTC 存储绝对时间戳，但周期边界、剩余时间和客户端展示必须按榜单当地时区计算。

排行榜查询由客户端在登录、重连和联机结算后通过控制面 RPC 主动拉取，不通过实时通道推送。服务端接口为 `IPlayerService.GetLeaderboardAsync`。

生产目标方案中，当前周期排行榜索引使用 Redis sorted set 实现。排行榜服务不再把完整排序索引保存在内部状态中，而是作为排行榜协调者负责周期检查、Redis key 管理、归档和查询聚合。Unity 客户端仍只通过 `IPlayerService.GetLeaderboardAsync` 查询，不直接连接 Redis。

## 跨场数据流

```txt
对局结束 → RoomRuntime 计算排名 → 按排名发放胜利积分 → 用户状态服务持久化
                                           ↓
                                 排行榜服务协调写入 Redis 排行榜索引
                                           ↓
客户端拉取排行榜 ← 排行榜服务从 Redis 查询并聚合 top N
```

胜利积分存储在用户状态中，作为用户持久化状态的一部分。当前周期排行榜排序索引应迁移到 Redis sorted set；客户端不遍历用户列表。

## Redis 排行榜设计

当前周期 Redis key：

- `agar:leaderboard:{period}:points`：sorted set，member 为 `playerId`，score 为当前周期胜利积分。
- `agar:leaderboard:{period}:wins`：sorted set 或 hash，保存当前周期胜场，用于积分相同时的第二排序条件。
- `agar:leaderboard:{period}:players`：hash，保存查询 top N 需要的玩家快照。
- `agar:leaderboard:current`：当前周期、本地周一日期和榜单时区。
- `agar:leaderboard:archive:{period}`：上周期 top 100 归档。

排序口径保持：胜利积分降序、胜场降序、玩家标识升序。Redis sorted set 只天然支持单 score 排序，因此查询 top N 时需要从 points zset 取候选集合，再在服务端按完整口径做稳定排序。候选集合大小需要大于 top N；如果同分边界过大，后续应使用 Lua 脚本或扩大候选窗口保证确定性。

## 排行榜协调

排行榜服务：

- **写入**：接收 `RecordVictoryPointsAsync(string playerId, int victoryPoints, int winCount)`，在结算后调用 Redis 排行榜 store 更新该玩家的当前周期索引。
- **查询**：接收 `GetLeaderboardAsync(int topN)`，从 Redis 排行榜 store 读取候选集合，按积分降序、胜场降序、玩家标识升序排序后返回 top N。
- **周期检查**：记录当前周期标识（`yyyy-MM-dd` 格式的本地周一日期）和榜单时区。每次查询或写入时按榜单当地时区检查是否已过周一 00:00，若是则先执行重置。
- **重置**：归档 Redis 上周 top 100（保留最近两周），切换当前周期 key，并按数据模型要求同步处理用户状态中的当前周期胜利积分。
- **条目结构**：`PlayerId`、`VictoryPoints`、`WinCount`、`Rank`。

## 积分发放时机

在 `RoomRuntime.PersistMatchEndAsync` 中，对局结算时依次执行：

1. 计算排名（已有逻辑，通过 `RoomSettlementEntry.Rank` 获得）。
2. 根据排名映射胜利积分（1→10, 2→7, 3→5, 4→3, 5→1, 其余 0）。
3. 过滤 AI 玩家（以 `VictoryPointAwards.BotPrefix` 即 `"AI"` 开头）。
4. 对剩余玩家调用用户状态服务增加积分并持久化。
5. 读取用户 profile，并调用排行榜服务更新 Redis 排行榜索引。

## 当前实现状态

已完成：

- `UserState.VictoryPoints` 持久化字段（`[Id(9)]`）。
- 用户状态服务的积分增加和重置能力。
- 排行榜服务、排行榜状态和最近两周周榜归档。
- `IPlayerService.GetLeaderboardAsync` 控制面 RPC。
- 客户端登录、重连和联机结算后的排行榜刷新。
- 本地 mock 假条目已移除。

待继续验证：

- 阶段 13：将当前排行榜内部积分索引迁移为 Redis sorted set。
- 联机实机对局结束后的积分发放和排行榜刷新。
- 周一当地时间 00:00 后首次查询触发重置的持久化路径。
- 排行榜服务已按榜单当地时区计算周一 00:00 周期；`PeriodStartUtc` 旧字段仅作为兼容字段保留，体验口径使用 `PeriodStartLocalDate`。
