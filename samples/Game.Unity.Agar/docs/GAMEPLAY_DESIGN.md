# Agar 设计与边界

这个样例验证 Lakona 的多人游戏接入方式。精确规则由实现与边界测试表达，
本文保留职责、设计取舍和使用限制；运行步骤见 [README](../README.md)。

## 共享模拟与排名

单机和联机共用 [ArenaSimulation](../Shared/Gameplay/ArenaSimulation.cs)：
玩家移动、吃食物成长、吞噬较小玩家，在回合结束时结算。
规则预设在同一文件的 `ArenaSimulationOptions` 中维护，
不在文档复制参数值、字段或 AI 决策顺序。

排名按实际浮点质量降序，完全相同时按玩家标识的 ordinal 顺序兜底；
整数质量只用于展示，显示相同不代表并列。
[结算规则](../Shared/Gameplay/MatchSettlementResult.cs)与
[排名边界测试](../tests/BusinessLogic.Tests/MatchSettlementRulesTests.cs)保护此约定。

联机时客户端用相同随机种子和连续输入帧推进模拟，服务端不运行战斗模拟。
帧历史有界，短时缺帧和重连可以追赶，不能承诺无限回放或房间自动迁移。

## 节点与代码边界

| 角色 | 样例职责 |
| --- | --- |
| gateway | WebSocket 控制连接、登录入口与可靠推送 |
| data | 用户、匹配、排行榜及业务 PostgreSQL/Redis 访问 |
| battle | 房间 Actor、KCP 实时连接、输入组帧与转发 |

单节点配置合并这三种角色；三节点配置将其拆开，客户端按匹配返回的目标连接
房间所属 battle 节点。控制连接和实时连接各有自己的会话与回调，
不能依赖它们处于同一进程。

`Shared` 不依赖 Unity 引擎运行时、UI、服务端托管或数据库。
共享模拟使用的 Unity 值类型由 `MemoryPack.UnityShims` 支持，可在普通 .NET 环境中运行。
`Server/App` 持有稳定宿主、绑定、Actor 内存状态和稳定资源身份；
`Server/Hotfix` 持有 RPC/HTTP 业务入口、Behavior 和 Store。
内存状态跨热更保留，不等于进程重启后自动恢复。

框架约定直接参阅 [Actors](../../../docs/actor.md)、
[Cluster](../../../docs/cluster.md)、[Sessions](../../../docs/session.md)
和 [Hotfix Components](../../../docs/hotfix/components.md)。

## 结算与持久化

客户端根据最终模拟状态上报排名；服务端验证会话、房间、对局及真人成员，
再通过 [VictoryPointAwards](../Shared/Gameplay/VictoryPointAwards.cs)映射胜利积分。
单机和 AI 不获得服务端积分。客户端结果是不可信输入，成员校验不能证明模拟未被篡改。

PostgreSQL 保存用户累计状态；结算幂等记录与对应奖励在同一事务提交。
Redis 排行榜独立更新，失败时不能假定榜单与已提交的奖励同步。
数据库升级和生产迁移要求见[生产上线门槛](PRODUCTION_LAUNCH_PLAN.md#数据与配置)。

周榜按宿主本地时间划分周期。协议兼容字段 `PeriodStartUtc` 表示本地周期日期，
不能作为 UTC 时间戳；样例不承诺长期历史榜单或自动归档。
业务 SQL、榜单读写与重试行为分别见
[Users](../Server/Hotfix/Users)和 [Leaderboard](../Server/Hotfix/Leaderboard)。

## 客户端表现约束

保持深色低干扰场地、清晰小球轮廓与紧凑 HUD，玩法可读性优先于装饰。
稳定 UI 层级、布局和默认可见状态保存在 prefab；运行时负责绑定、状态更新和动态视图。
网络按钮须有请求中、失败与恢复状态，避免重复请求。

客户端使用英文玩家文案、Liberation Sans TMP 字体及程序生成图形，不提交图片资产；
资源和字体约束由 [Unity EditMode 测试](../Client/Assets/Tests/EditMode)保护。
颜色、尺寸、字体大小及协作类列表以资产和实现为准，不在文档复制。
上线前的视觉回归、资源许可与玩家体验验收见[生产上线门槛](PRODUCTION_LAUNCH_PLAN.md)。

## 样例范围

不包含权威服务器战斗模拟、预测回滚、房间运行时自动迁移、无限回放，
也不将玩法扩展成复杂技能、任务或商店系统。未关闭的生产要求统一维护在上线门槛文档。
