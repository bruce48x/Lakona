# Game.Unity.Agar 生产上线门槛

本文只记录 `samples/Game.Unity.Agar` 尚未关闭的生产上线门槛。当前玩法、客户端和服务端边界分别以 [`topics/gameplay-rules.md`](topics/gameplay-rules.md)、[`topics/client-architecture.md`](topics/client-architecture.md) 和 [`topics/server-architecture.md`](topics/server-architecture.md) 为准；已经落地的实现不在这里重复维护。

## 上线范围

首个生产版本定位为轻量小球吞噬对战：

- 单机模式可完全离线游玩。
- 联机模式包含账号或游客登录、匹配、实时对局、结算、胜利积分和周榜。
- 联机采用 20Hz 确定性帧同步；服务端排序并转发输入帧，客户端执行完整战斗模拟。
- 服务端以单节点或 `data-1`、`gateway-1`、`battle-1` 三节点拓扑运行，并使用 PostgreSQL 和 Redis。
- 玩家界面只展示玩家可理解的信息，不暴露 endpoint、tick、内部状态或调试入口。

分裂、吐质量、病毒、组队、任务、商店、观战、回放和长期历史榜单不属于首发范围。

## 当前生产阻塞项

### 帧同步可信度与恢复

- 客户端提交的 `FrameSyncMatchResult` 本质上不可信。面向不可信公网玩家前，必须选择受信玩家场景、增加独立裁判，或引入可验证状态证明；字段校验和限流不能替代反作弊。
- 增加低频确定性状态摘要和 desync 诊断，能够定位同一帧在不同客户端产生不同状态的问题。
- 明确帧缺口、实时断线、当前回合重放数据不足和长时间离线时的玩家体验。帧历史有界，不能承诺无限重放。
- 验证结算报告重试、重复提交和实时连接关闭之间的幂等关系。

### 会话与资源清理

- 对登录、登出、重复登录、取消匹配、匹配超时、控制连接断开、实时连接断开、离房和对局结束建立固定回归。
- 验证 stale connection id 不会解绑新连接，控制连接和实时连接可以独立清理。
- 验证匹配票据、房间成员、实时 callback、可靠推送 pending 记录和 frame relay timer 都有明确 owner、上限和终止条件。
- 验证空房间停止 frame relay timer，并释放帧历史和本地 callback。
- outbox 过期或服务端状态丢失时，客户端必须进入 new-session 路径，不能停留在过期 UI。

### 安全与滥用防护

- 将当前直接 SHA-256 密码摘要替换为带独立 salt 的自适应密码哈希，并提供凭据升级策略。
- 为游客凭据、登录 token 和实时会话 token 定义有效期、撤销和轮换语义。
- 登录、匹配、输入提交、结算提交和排行榜查询需要有界限流。
- 所有 RPC 请求继续验证 player、session、room、match 和 connection 的绑定关系。
- 明确 WebSocket TLS、KCP 公网暴露、反向代理和来源限制的部署方式。

### 数据与配置

- 生产环境通过部署平台 secret 或环境变量注入数据库、Redis、token 和公网 endpoint 配置，不使用开发默认凭据。
- PostgreSQL 初始 schema 和后续升级使用版本化迁移流程；普通应用启动不承担生产 schema 升级。
- 建立 PostgreSQL 备份、恢复演练和数据保留策略。
- 明确周榜旧周期 Redis key 的保留和清理规则，以及 Redis 不可用时的产品行为。
- 当前周重置只覆盖上一周期 Redis 索引中的玩家；如果产品要求“所有历史用户积分归零”，必须补充全量用户目录或调整产品语义。

### 可观测性与发布

- 日志覆盖登录、匹配、房间创建、实时绑定、输入投递失败、帧广播失败、结算、积分发放和排行榜重置。
- 指标覆盖在线人数、匹配队列长度、房间数、帧广播频率、连续帧缺口、断线率、RPC 错误率和依赖错误率。
- readiness 必须真实反映 cluster、PostgreSQL 和 Redis 依赖状态；告警覆盖节点不健康、队列积压和错误率上升。
- 固定 .NET、Unity 和包版本，生成带版本号与 commit 的可复现服务端镜像和客户端构建。
- 发布流程包含灰度、回滚、数据库恢复和监控告警演练。

### 客户端交付质量

- 登录、匹配、取消、重连、状态丢失、服务器维护和结算失败都有明确玩家文案与返回路径。
- 异步按钮在请求、超时、失败和返回时保持一致的可操作状态，避免重复提交。
- 完成 1200x600、960x540 和目标发布分辨率的视觉回归。
- 检查英文文案、字体许可、第三方资源许可和目标平台输入行为。
- `DotArenaGame`、`DotArenaSceneUiPresenter` 等组合根继续保持职责边界；新功能不得重新把网络、模拟、表现和 UI 状态混入单一类。

## 验证门槛

从仓库根目录运行自动化验证：

```powershell
dotnet build samples/Game.Unity.Agar/Shared/Shared.csproj -f net10.0
dotnet build samples/Game.Unity.Agar/Server/App/Server.App.csproj
dotnet build samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj
dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1
```

三节点脚本的完整生命周期场景使用：

```powershell
pwsh -NoProfile -File scripts/game/ci/test-agar-three-node.ps1 `
  -TestFilter "SampleClient.Gameplay.Tests.DotArenaTwentyClientLifecyclePlayModeTests.TwentyClientsCompleteMatchBattleSettlementAndLeaderboard" `
  -TimeoutSeconds 600
```

上线候选还必须满足：

- Unity 脚本编译无错误，EditMode 测试通过。
- 单机主流程人工回归通过。
- 三节点联机主流程、断线和重连回归通过。
- 20 客户端两房间生命周期、结算、胜利积分和排行榜核对通过。
- 发布包、视觉、配置、许可、备份恢复和回滚演练通过。

## 当前非目标

- 权威服务器战斗模拟、预测回滚或服务端状态校正。
- 房间运行时自动迁移到另一 battle 节点。
- 无限时长帧历史、观战或通用回放系统。
- 任务、商店、复杂皮肤养成或长期历史战绩。
- 未经容量验证的自动扩缩容和跨区域部署。
