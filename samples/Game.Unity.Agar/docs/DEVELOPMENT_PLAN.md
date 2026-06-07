# 开发计划

这份文档只记录阶段计划、验收点和执行顺序。玩法规则和架构判断放在 `GAMEPLAY_DESIGN.md`，避免两个文档重复解释同一件事。

## 工作流规则

这个样例的功能开发按以下顺序推进：

1. 更新 `samples/Game.Unity.Agar/docs/GAMEPLAY_DESIGN.md`、`ART_DIRECTION.md` 以及 `docs/features/` 下对应的功能子文档。
2. 更新本开发计划。
3. 实现代码改动。
4. 验证被影响的运行路径。

小改动可以让设计说明和计划说明保持简短，但只要行为或架构发生变化，就应该同步更新这两个核心文档。

共享协议变更时，先改 `Shared` 合同，再从 `samples/Game.Unity.Agar` 目录重新生成 RPC 代码：

```powershell
dotnet build Server/Gateway/Gateway.csproj
```

常规验证基线：

```powershell
dotnet build Shared/Shared.csproj -f net10.0
dotnet build Server/State/State.csproj
dotnet build Server/Gateway/Gateway.csproj
dotnet test tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj
```

## 当前基线

仓库已经不再是最早的单进程场地样例，当前基线包括：

- `Shared/Gameplay/ArenaSimulation.cs` 中的共享小球吞噬玩法模拟。
- 单机和联机共用同一套模拟逻辑。
- 质量、半径、速度之间的成长循环。
- 食物刷新、玩家吞噬、死亡、复活、单局计时和排名。
- Unity 场景和脚本作为工作流来源，不再恢复编辑器侧自动烘焙玩法场景的做法。
- 控制面的 WebSocket RPC。
- 实时面的 KCP RPC。
- 用于实时会话绑定的 `AttachRealtimeAsync`。
- 基于 Dapper + PostgreSQL 的状态持久化。
- 基于状态服务的匹配队列。
- 持久化的房间和会话分配，包含运行时网关端点信息。
- 支持运行时网关绑定的仅实时连接本地会话注册。
- `SessionDirectory` / `PlayerService` 已接入 `Lakona.Game.Server.Sessions`，控制连接和实时连接绑定由框架会话目录保存 opaque callback。
- 可靠匹配推送已按 `GameSessionKey` 隔离 pending record，ack 路径已接入 `IReliablePushAckService` 的 accepted / duplicate / state lost / session mismatch outcome。
- Unity 客户端可靠推送和 reconnect/state-lost 状态已由 `ReliablePushInbox` 与 `ClientSessionController` 管理。
- 包含 PostgreSQL 和 Redis 的本地 compose 基线。
- `DisconnectedSessionCleanupHostedService` 用于后台清理过期会话。
- `DotArenaMetaProgression` 已按关注点拆分为 Models、Persistence、Queries、Rules 四个 partial 文件。
- 客户端排行榜 UI 已通过 `IPlayerService.GetLeaderboardAsync` 连接服务端真实排行榜数据。
- `InputMessage` 只包含玩家、移动方向和 tick，不再包含 dash。
- 旧食物协议命名已删除，食物行为统一为 `PickupType.MassPoint` 质量成长。
- 战斗内 HUD、实时排名和结算只展示整型质量；玩家可见的实时排名只有质量列，不再出现“分数”或分数列。
- `PlayerState` 应只发布位置、速度、生死、整型质量、半径和移动速度；旧分数字段已从共享协议、服务端快照和客户端读取路径中彻底删除。
- 用户状态已持久化 `VictoryPoints`，排行榜服务已提供周榜查询、周期重置和最近两周归档。
- 排行榜周期重置已按榜单当地时间周一 00:00 计算，旧 `PeriodStartUtc` 字段仅作为兼容字段保留。
- `RoomRuntime.PersistMatchEndAsync` 已按排名发放胜利积分，AI 玩家不获得积分。
- 游戏暂不包含任务、商店和记录功能；大厅不应展示或跳转到 Tasks / Shop / Records 页面。
- 面向玩家的 Unity 界面应使用中文文案；联机大厅和战斗内 HUD 不显示 DEBUG 信息、调试面板、tick、连接细节、内部状态枚举、同步视图数或快捷键提示。将来需要排查问题时只通过 Unity Console、服务端日志或客户端日志打印，不在玩家 UI 中保留。
- 战斗中需要实时显示当前对局玩家排名，排名框固定在画面右侧，背景必须低遮挡半透明，避免遮挡游戏画面和影响玩家判断。
- sample 自动化测试当前为 31 个（`ArenaSimulationRulesTests` 17 个测试用例、排行榜测试 6 个、`MatchmakingQueuePolicyTests` 4 个、`SessionDirectoryCleanupTests` 4 个）；框架会话、可靠推送和客户端 session controller 另由 `Tests/tests.slnx` 覆盖。
- `samples/Game.Unity.Agar/docs/ART_DIRECTION.md` 已定义整体美术、UI 设计、素材生成、Unity 接入和验收标准。

已经从当前计划中移除的方向：

- 把本地内存状态存储作为目标服务端状态模型。
- 把只限本机的状态服务部署作为目标部署模型。
- 把网关本地匹配队列作为目标匹配队列模型。
- 把旧的击退、冲刺、强化战斗循环作为目标玩法模型。
- 为客户端架构、生产基础设施和网关重构继续维护分散的一次性文档。

## 已知技术债务

以下问题存在于当前代码库中，需要在对应阶段处理：

### 协议与玩法变体

- `ArenaMapVariant` 和 `ArenaRuleVariant` 枚举各有多个值；当前代码已有多个单机预设，但联机仍只走默认共享规则。后续如果不会继续扩展联机规则变体，可以把联机侧字段收敛为简单常量。
- RPC 生成代码需要在共享协议变更后重新生成，Unity 客户端命名空间必须保持为 `Rpc`，否则会遮蔽手写的 `WebSocketRpcClientFactory` / `KcpRpcClientFactory`。
- 旧局内分数字段与质量语义重复。后续应继续让战斗内实时排名、HUD 和结算只使用质量；旧分数字段需要从共享协议、模拟状态、服务端快照、客户端模型和玩家可见路径中保持删除状态。

### 客户端结构

- `DotArenaGame.cs` 有 93 个实例字段，分散在 12 个 partial 文件中。`UiSurface`、`UiActions`、`Presentation`、`Views` 之间的职责边界不清晰。
- `DotArenaSceneUiPresenter` 仍偏大，负责运行时控件构建（`Layout.cs`、`Layout2.cs`）、刷新（`Refresh.cs`）、样式（`Styling.cs`）、大厅（`Lobby.cs`）。
- 联机大厅的任务、商店和记录入口已从玩家可见路径移除；旧场景对象仅作为兼容占位隐藏，不再绑定玩家导航动作。
- 玩家可见 UI 已收敛为中文，并隐藏 debug 面板、连接端点、内部状态枚举和快捷键提示；后续继续确保联机大厅和战斗内 HUD 不展示任何 DEBUG 信息。
- `DotArenaMetaProgression` 的拆分已完成（Models、Persistence、Queries、Rules），本计划不再对其安排额外拆分工作。

### 测试缺口

当前已有 31 个自动化测试。仍缺少覆盖：网络会话生命周期、房间运行时行为、客户端流程状态，以及更完整的网关清理语义。

## 待办

这一节只记录尚未完成、后续执行计划时真正需要处理的事项。已经完成的工作放在后面的“已完成记录”中。

### 当前优先级

0. 阶段 6：优先整理并执行客户端巨型类拆分。当前 partial 文件只是把大类切成多个文件，没有形成真正的职责边界，后续功能会继续增加维护成本。
1. 阶段 2：完成启动/登录/匹配/结算 UI 的人工视觉回归，确认裁切后的 UI sprite、实时排名面板和运行时布局在 1200x600、960x540 下不再出现视觉边界错位。
2. 阶段 4：完成第一轮基础美术资产的 Unity 实机验收，确认玩家球、拾取物、背景、UI 基础件、图标、吸收环和出生波纹是否继续沿用或需要重做。
3. 阶段 5：完成单机和联机全流程手动回归。
4. 后续进入阶段 7 及之后的清理语义生产回归、跨网关路由、测试扩展和阶段 13 Redis 排行榜迁移。

### 阶段 2：启动界面视觉修复

待办：

- 在 Unity 游戏视图手动检查入口/模式选择、联机登录、匹配中、大厅和结算界面。
- 在 Unity 游戏视图手动检查对局中的右侧实时排名面板。
- 在 Unity 游戏视图复查右侧实时排名面板的新半透明背景 `UI_HUD_Rank_Panel_Translucent_01.png`，确认不再遮挡背后的玩家球、食物、边界和移动方向。
- 覆盖 1200x600 和 960x540，重点看文本、按钮、输入框与可见面板边框是否对齐。
- 确认裁切后的 `UI_Panel_Dark_01.png`、`UI_Button_Primary_Normal.png`、`UI_Button_Primary_Pressed.png` 不再造成 `RectTransform` 逻辑尺寸和视觉边界错位。
- 如果现有 UI 面板或按钮风格仍不达标，按 `ART_DIRECTION.md` 的透明边缘规范重新生成或重做最小 UI 基础件。

验收标准：

- 启动到模式选择界面时，首屏能看到正式深色竞技场背景图。
- 三个模式按钮在同一中轴线上，尺寸一致，垂直间距一致，文字居中且不溢出。
- 联机登录界面的账号/密码标签与输入框形成居中窄列，登录/返回按钮等宽对齐，游客登录按钮居中且不压到底部边框。
- 非对局 UI 背景不覆盖实际对局画面。
- 对局中的右侧排名面板使用低遮挡半透明背景；透过面板仍能看清玩家球、食物、边界和移动方向，不遮挡核心玩法区域。
- UI sprite 的 alpha 包围盒贴合肉眼可见边框，不再用大透明 padding 充当布局留白。

### 阶段 4：AI 生成美术资产

待办：

- 为默认本地玩家小球接入蓝、橙、绿三色随机表现，要求每局开始时选色，同一局内保持稳定；不改共享协议和服务端权威状态。
- 对第一轮基础美术资产做 Unity 实机验收：
  - `Skin_Jelly_Cyan.png`
  - `Skin_Jelly_Crimson.png`
  - `Skin_Jelly_Sunburst.png`
  - `Pickup_Mass_Teal_01.png`
  - `Pickup_Mass_Gold_01.png`
  - `BG_Arena_Grid_Dark_01.png`
  - `UI_Panel_Dark_01.png`
  - `UI_HUD_Rank_Panel_Translucent_01.png`
  - `UI_Button_Primary_Normal.png`
  - `UI_Button_Primary_Pressed.png`
  - `Icon_Leaderboard_01.png`
  - `FX_Absorb_Ring_01.png`
  - `FX_Spawn_Wave_01.png`
- 验证玩家球、拾取物、背景和特效不会降低玩法判断效率，不遮挡玩家名、质量、HUD 和排行榜信息。
- 如用户确认第一轮资产方向成立，再生成下一批 UI 基础件：Secondary/Danger 按钮、输入框状态、排行榜/设置等图标、HUD 背板和列表行。
- 先接入入口/模式选择、匹配中、结算三个关键界面；通过后再扩展大厅、排行榜和设置。

验收标准：

- `ART_DIRECTION.md` 作为后续资产生成、UI 设计、AI 生成提示词、Unity 接入和验收的唯一风格标准。
- 第一轮基础资产在 Unity 游戏视图中通过手动验收。
- 默认本地玩家小球在多次开始对局时可以出现蓝、橙、绿三种颜色之一，且单局内不会跳色。
- 所有正式 UI sprite 的透明外边距符合文档约束。
- UI 文字不溢出，按钮状态清楚，图标 32x32 可识别。
- 九宫格拉伸后边框不变形。

### 阶段 5：玩法回归与协议清理

待办：

- 在 Unity 编辑器中验证单机启动、食物成长、玩家吞噬、死亡、复活和结算全流程。
- 在状态服务和 gateway 运行时，验证联机登录、匹配、实时绑定、输入、世界快照和结算全流程。
- 验证联机大厅不展示 DEBUG 信息，不保留调试面板、连接端点、连接细节、内部状态枚举、同步视图数、快捷键提示或开发诊断文本。
- 验证单机和联机对局中的右侧实时排名面板随世界状态变化正确刷新，且只展示整型质量；表头、行内容和排序都不出现“分数”。
- 验证右侧实时排名面板背景已改为低遮挡半透明，不再因为背景框过深或过实影响游戏画面可读性。
- 验证战斗内 HUD 不展示 DEBUG 信息：不出现 tick、endpoint、连接细节、内部状态枚举、同步视图数、快捷键提示或开发诊断文本。
- 将局内质量改为整型展示和排序口径；如果共享协议仍使用浮点质量，先明确取整规则，再安排协议清理。
- 删除旧分数字段和相关读取路径；玩家可见文案、HUD、结算和单机/联机实时排名不再出现“分数”，统一使用“质量”。
- 将旧食物协议命名改为 `PickupType.MassPoint`。
- 决定是否继续保留 `ArenaMapVariant` / `ArenaRuleVariant` 的联机扩展点；如果短期不扩展联机规则变体，将联机侧字段收敛为简单常量。

验收标准：

- 本地单机对局可以完整游玩到结束。
- 联机对局可以通过匹配开始，并持续收到实时世界快照。
- 单机和联机对局中的实时排名与当前玩家整型质量和生死状态一致。
- 单机和联机对局中的实时排名面板背景半透明且低遮挡，不影响玩家观察场地和移动。
- 战斗内 HUD 只显示玩家决策需要的信息，不显示 DEBUG 信息。
- 联机大厅只显示玩家决策需要的信息，不显示 DEBUG 信息；开发诊断只允许进 console/log。
- 可见 UI 路径不再依赖已移除的旧玩法概念。
- 生成的 RPC 代码与协议变更一致。

### 阶段 6：客户端拆分

目标：

- 把 `DotArenaGame` 收敛为 Unity 场景级组合根，只负责生命周期入口、组件装配和高层调度。
- 把 `DotArenaSceneUiPresenter` 收敛为 UI 根协调器，不再直接拥有所有面板字段、控件创建、样式和刷新细节。
- 用小类边界替代继续增加 partial 文件。partial 只允许作为短期迁移手段，不能作为最终架构。
- 保持样例易读：拆分后的类名、文件名和职责应能直接对应玩家流程，而不是把逻辑隐藏到过度抽象的工具层。

拆分原则：

- 单一职责优先于按 Unity 事件函数分文件。`Update`、按钮回调、网络回调和 UI 刷新都应流向明确的流程对象。
- 网络生命周期继续由 `DotArenaNetworkSession` 管理，不能把 RPC 连接细节散回 UI 或场景脚本。
- 服务端回调缓存和主线程消化继续由 `DotArenaCallbackInbox` 管理，Unity 对象只在主线程被修改。
- `SampleClient.Rpc` 不能依赖 `SampleClient.Gameplay`，生成的 RPC 代码不能手写修改。
- 新增类优先放在 `Client/Assets/Scripts/Gameplay`；只有传输或生成代码辅助才放到 `Client/Assets/Scripts/Rpc`。

待办：

- 盘点 `DotArenaGame*.cs` 中的字段和方法，按所有权分成会话、单机模拟、输入、世界表现、UI 快照、资源/皮肤、本地元进度七组；每组记录迁移目标类。
- 继续收敛现有轻量状态模型；`DotArenaMultiplayerState` 已集中保存联机会话身份、待处理 UI 请求、重连标记、匹配计时、实时连接和可靠 push/session controller，但 `FrontendFlowState`、`EntryMenuState` 等仍有多个 partial 直接写入。
- 从 `DotArenaGame.Session.cs` / `DotArenaGame.Callbacks.cs` 中继续抽出联机流程编排，优先复用并扩展现有 `DotArenaMultiplayerFlow`；可靠推送确认和 state-lost 已接入框架 helper，剩余目标是让登录、游客登录、匹配、取消匹配、实时绑定和断线恢复有单一入口。
- 从 `DotArenaGame.SinglePlayer.cs` 中抽出单机对局控制器，负责创建本地模拟、推进 tick、处理结算和重开；`DotArenaGame` 只转发输入意图和接收渲染状态。
- 从 `DotArenaGame.Views.cs` / `DotArenaGame.Presentation.cs` 中抽出视图注册表和表现资源目录，减少 `_views`、`_renderStates`、`_pickupViews`、sprite、shader、皮肤列表等字段直接挂在 `MonoBehaviour` 上。
- 从 `DotArenaGame.UiSurface.cs` / `DotArenaGame.UiActions.cs` 中抽出 UI 快照构建器和 UI 命令处理器。UI 快照只表达要显示什么，UI 命令只表达玩家请求什么，避免 UI 代码直接改网络或模拟状态。
- 将 `DotArenaSceneUiPresenter` 拆成根协调器和面板 presenter：入口/模式选择、登录、匹配中、大厅、HUD、对局排名、结算分别拥有自己的字段和刷新方法；不再保留调试面板 presenter。
- 将 `DotArenaSceneUiPresenter.Layout.cs`、`Layout2.cs`、`Styling.cs` 中重复的运行时 UI 构建逻辑移动到 `DotArenaUiFactory`、`DotArenaUiStyleCatalog` 或稳定 prefab/资源中；短期先用工厂类，后续需要可视化调参时再资源化。
- 删除拆分后不再需要的 partial 文件，避免留下空壳文件或只转发一行方法的文件。
- 每一小步拆分后都做 Unity 脚本编译检查；触碰共享协议时重新构建服务端并让 Unity 重新编译，RPC 胶水由 `Lakona.Rpc.Analyzers` 在编译期生成。

验收标准：

- `DotArenaGame` 只保留场景生命周期、组件装配和少量跨组件协调字段；实例字段数量从当前 93 个降到 25 个以内。
- `DotArenaGame` 不再需要 12 个 partial 文件；保留的 partial 文件不超过 4 个，且每个文件都有明确职责。
- 模式入口、登录、匹配、对局中、结算和返回大厅状态切换都能从一个流程对象或状态模型中追踪，不需要跨多个 partial 文件查找同一状态的写入点。
- `DotArenaSceneUiPresenter` 不再直接持有所有面板控件字段；每个主要面板有独立 presenter 或稳定 prefab 作为边界。
- `DotArenaSceneUiPresenter` 的运行时控件构建逻辑减少至少 30%（以行数计），并且重复按钮、文本、面板样式创建集中在一个工厂或样式目录中。
- 不引入 `SampleClient.Rpc` 和 `SampleClient.Gameplay` 之间的循环依赖。
- 生成的 RPC 代码保持不被手写修改。
- 单机开始、联机登录、匹配、取消匹配、实时绑定、世界刷新、断线返回和结算按钮路径都能通过手动回归。
- Unity 脚本编译无错误；受影响的服务端和共享项目在本阶段未改动时不强制重建。

### 阶段 7：网关清理语义生产回归

待办：

- 审核 `PlayerService.cs`、`SessionRegistration.cs`、`SessionDirectory.cs`、`DisconnectedSessionCleanupHostedService.cs` 中的登出、断线、取消匹配、离开房间和对局结束清理路径；其中会话绑定、resume 和 ack state-lost 已接入 Lakona.Game 框架，剩余重点是业务资源和生产回归。
- 回归控制连接和实时连接可以独立解绑（解绑实时连接不意外断开控制连接，反之亦然），并覆盖 stale connection id 不会解绑新连接。
- 确认玩家会话、房间状态和 `SessionDirectory` 本地业务状态在失败后能收敛（重复登出不残留、匹配取消后票据不堆积）。
- 确认 `RoomRuntime.RemovePlayerAsync` 返回 `remaining == 0` 后，`RoomRuntimeHost` 或调用方正确释放房间资源。
- 为登出、断线、重复匹配、session mismatch 和 state-lost 流程增加聚焦测试，或者留下可追踪的手动验证步骤文档。

验收标准：

- 过期匹配票据会被清理。
- 过期实时回调会被移除。
- 房间运行时会在断线或离房后移除玩家。
- 重复登录、登出、匹配和取消匹配不会留下重复的本地注册。
- `DisconnectedSessionCleanupHostedService` 的后台清理周期性运行且日志可追踪。

### 阶段 8：胜利积分与排行榜

待办：

- 做联机实机回归，确认对局结束后真实玩家按排名获得胜利积分。
- 验证客户端排行榜展示服务端真实排名、当前周期剩余时间和本地玩家积分变化。
- 验证 AI 玩家不会获得胜利积分。
- 验证榜单当地时间周一 00:00 后首次查询会触发自动重置，并能查看最近归档数据。
- 继续迁移客户端文案和字段命名中 `PeriodStartUtc` 这类体验口径；服务端兼容字段可保留，但 UI 不应继续表达为 UTC。

验收标准：

- 联机对局结束后，玩家按排名获得对应胜利积分（1st=10, 2nd=7, 3rd=5, 4th=3, 5th=1）。
- AI 玩家不获得胜利积分。
- 客户端排行榜展示真实的全局玩家排名。
- 日志可追踪积分发放和排行榜查询。
- 排行榜在榜单当地时间周一 00:00 后首次查询时自动重置，最近归档数据可查。
- 单机模式不发放胜利积分。

### 阶段 9：跨网关实时路由设计

待办：

- 选择网关到网关的事件机制：Redis 发布订阅、Lakona.Game.Cluster 或应用自定义消息总线。
- 定义输入转发、世界状态扇出、断线事件和背压的所有权。
- 定义顺序、重试和运行时所有者过期时的行为。
- 实现前先更新 `samples/Game.Unity.Agar/docs/GAMEPLAY_DESIGN.md`。

验收标准：

- 控制连接在网关 A 的玩家，可以把输入送到网关 B 上的房间运行时。
- 对局事件可以送回正确的在线客户端连接。
- 失败行为足够明确，可以编写测试或手动验证。

### 阶段 10：跨网关实时路由实现

待办：

- 实现阶段 9 选定的路由层。
- 保持房间模拟在同一时刻只由一个运行时所有者权威推进。
- 不把实时回调对象序列化到 Redis、PostgreSQL 或任何持久化状态中。
- 增加路由决策和投递失败日志。

验收标准：

- 多网关部署可以处理控制连接、实时连接和房间运行时位于不同网关的情况。
- 世界状态广播可以到达连接在不同网关节点上的玩家。
- 断线、登出和离房清理在跨网关所有权下仍然正确。

### 阶段 11：测试扩展

待办：

- 为 `RoomRuntime` 增加测试：玩家加入/离开、输入提交、对局结束结算、空房间清理。
- 在现有 `SessionDirectoryCleanupTests` 基础上补充测试：framework-backed 控制注册、session generation、stale connection 解绑、回调获取和过期清理。
- 为 `PlayerService` 增加测试：登录、游客登录、重复登录、匹配、取消、实时绑定、ack state lost 和 session mismatch。
- 为 `DotArenaNetworkSession` 增加测试：连接生命周期、重连参数、实时绑定、ack 携带 session id/generation。
- 为 `ArenaSimulation` 补充测试：食物刷新边界、吞噬比例边界、AI 补位、多人同时死亡。
- 为 `DotArenaMetaProgression` 增加测试：经验升级边界、首胜断言，以及阶段 1 后仍保留的资料、设置、排行榜缓存和最近对局趋势路径。
- 为排行榜服务补足测试：AI 过滤、全周期路径、未来全量用户目录接入后的全用户重置路径。

验收标准：

- 自动化测试数量从 9 个增加到至少 30 个。（当前 31 个）
- 核心业务逻辑路径（模拟、匹配、房间、会话）有可运行的测试覆盖。

### 阶段 12：验证与打包

待办：

- 每轮触碰服务端或共享协议后，构建 `Shared/Shared.csproj`、`Server/State/State.csproj`、`Server/Gateway/Gateway.csproj`。
- 每轮触碰业务逻辑后，运行已有自动化测试和新增测试。
- 每轮触碰 Unity 客户端脚本或资源后，触发 Unity 资源刷新/脚本编译并检查控制台错误。
- 每轮发布前，手动冒烟测试单机和联机流程。
- 当命令、端口或架构事实变化时，同步 README。

验收标准：

- 被触碰的项目可以编译。
- 所有自动化测试通过。
- 文档中的运行说明和当前代码一致。
- 不再引用已经删除的文档。

### 阶段 13：Redis Sorted Set 胜利积分排行榜

目标：

- 将胜利积分排行榜的排序索引从排行榜服务内部状态迁移到 Redis sorted set。
- 继续保留 `IPlayerService.GetLeaderboardAsync` 作为客户端唯一查询入口，不让 Unity 客户端直接依赖 Redis。
- 让排行榜实现更接近 Docker 生产部署形态，为后续多 gateway 和生产上线计划打基础。

设计原则：

- 胜利积分发放仍由服务端结算路径触发，客户端不能直接写排行榜。
- 用户状态服务继续保存用户资料、胜场和胜利积分等持久化用户状态。
- Redis 负责当前周期排行榜索引，不替代用户 profile 存储。
- 排行榜服务退回为协调者：周期检查、Redis key 管理、归档、查询聚合和兼容现有 RPC 合同。
- 排序口径保持不变：胜利积分降序、胜场降序、玩家标识升序。

待办：

- 设计 Redis key：
  - 当前周期积分 zset：`agar:leaderboard:{period}:points`，member 为 `playerId`，score 为当前周期胜利积分。
  - 当前周期胜场 zset 或 hash：`agar:leaderboard:{period}:wins`，用于积分相同时的第二排序条件。
  - 当前周期玩家快照 hash：`agar:leaderboard:{period}:players`，保存查询 top N 所需的 playerId、胜利积分、胜场和显示字段。
  - 周期元数据 key：`agar:leaderboard:current`，保存当前周期、本地周一日期和时区。
  - 历史归档 key：`agar:leaderboard:archive:{period}`，保存归档 top 100。
- 定义排序实现：
  - 写入时同时更新 points zset、wins zset/hash 和玩家快照。
  - 查询时从 points zset 取候选集合，再按胜利积分、胜场和玩家标识在服务端内存中做最终稳定排序。
  - 明确候选 overfetch 规则，避免 top N 边界处大量同分玩家导致排序不稳定；必要时用 Lua 脚本或更大的候选窗口保证确定性。
- 增加 Redis 配置：
  - 在 Gateway/State 配置中增加 Redis connection string、password、database、key prefix、operation timeout。
  - 本地 compose 继续使用现有 Redis；生产 compose 在生产上线追加计划中补齐持久化和密码。
  - Redis 不可用时，排行榜写入和查询要返回明确错误或降级结果，不能影响对局结算主流程崩溃。
- 调整服务端实现：
  - 新增 Redis 排行榜存储接口，例如 `ILeaderboardStore`。
  - 实现 `RedisLeaderboardStore`，封装 zset/hash 写入、查询、周期清理和归档读取。
  - 保留现有排行榜查询合同，让它调用 Redis store，而不是继续维护完整积分索引。
  - `RoomRuntime.PersistMatchEndAsync` 仍按排名调用用户状态服务增加积分，再通过排行榜协调层更新 Redis 索引。
  - `PlayerService.GetLeaderboardAsync` 对外合同保持不变。
- 周期重置：
  - 周期边界仍按榜单当地时间周一 00:00 计算。
  - 首次查询或写入发现过期时，先归档上个周期 top 100，再切换到新周期 key。
  - 旧周期 key 设置合理 TTL，只保留最近两周归档。
  - 明确 Redis 当前榜清空后，用户状态中当前周期胜利积分是否同步重置；如果继续把 `UserState.VictoryPoints` 作为当前周期字段，则必须在阶段 13 同步修正重置语义。
- 兼容迁移：
  - 为已有排行榜状态提供一次性迁移路径，把当前周期索引写入 Redis。
  - 迁移完成后，旧内部积分索引只作为兼容读取或废弃，不再作为排行榜主数据源。
  - README 或功能文档说明本地开发需要 Redis 才能验证排行榜。
- 测试：
  - 增加 Redis store 单元或集成测试：写入、累加、top N、同分胜场排序、同分同胜场 playerId 排序。
  - 增加周期重置测试：周一当地时间切换、归档 top 100、只保留最近两周。
  - 增加 Redis 不可用测试：写入失败日志、查询失败返回、对局结算不崩溃。
  - 更新排行榜测试，验证排行榜服务通过 Redis store 返回结果。
  - 增加 Docker compose Redis 回归步骤。
- 文档：
  - 更新 `docs/features/victory-points.md`，把排行榜索引设计改为 Redis sorted set。
  - 更新 `docs/features/server-architecture.md`，说明 Redis 是胜利积分排行榜当前周期索引的生产组件。
  - 更新 `PRODUCTION_LAUNCH_PLAN.md` 和本计划中的生产上线追加计划，阶段 13 完成后 P6 不再把 Redis 排行榜职责作为待决项。

验收标准：

- 对局结算后，玩家胜利积分会写入 Redis sorted set 排行榜索引。
- 客户端通过 `GetLeaderboardAsync` 看到的 top N 来自 Redis 排行榜索引。
- 排序口径保持胜利积分降序、胜场降序、玩家标识升序。
- 周一当地时间周期切换会归档上周期 top 100，并切换到新周期 Redis key。
- Redis 不可用时，服务端有明确日志；排行榜接口返回可解释错误或空结果；对局结算主流程不因排行榜写入失败而整体崩溃。
- 相关自动化测试通过，并且 Docker 本地 Redis 路径可手动验证。

### 生产上线追加计划

本节排在原阶段计划之后，用于把 `PRODUCTION_LAUNCH_PLAN.md` 落成更细的上线执行任务。除非发现会阻塞前面阶段的安全问题，否则先完成阶段 2 到阶段 13，再按 P0 到 P15 推进生产上线工作。

#### P0：首发范围冻结

目标：

- 锁定首发只交付登录、匹配、对局、结算、胜利积分和排行榜主链路。
- 防止后续上线准备阶段重新引入任务、商店、记录或复杂玩法。

具体任务：

- P0.1：对照 `GAMEPLAY_DESIGN.md`、`PRODUCTION_LAUNCH_PLAN.md` 和 README，统一首发范围。
- P0.2：扫描客户端入口、导航按钮和页面跳转，确认任务、商店、记录没有玩家可见路径。
- P0.3：列出首发必须可玩的用户路径：单机一局、联机登录、匹配、取消匹配、实时对局、结算、排行榜刷新、退出返回。
- P0.4：列出首发延期项：任务、商店、记录、复杂皮肤、分裂、吐质量、病毒、组队、长期历史榜单。
- P0.5：在 README 当前状态中说明样例仍处于生产上线准备阶段，不能直接视为生产部署模板。

验收标准：

- 首发范围和延期范围在所有入口文档中一致。
- 玩家 UI 不展示首发范围外入口。
- 后续上线任务都能映射到 P0.3 的主链路。

#### P1：Docker 镜像工程

目标：

- 为状态服务和 gateway 生成可复现、可部署、可回滚的 Docker 镜像。

具体任务：

- P1.1：决定 Dockerfile 结构：两个独立 Dockerfile，或一个支持 `silo` / `gateway` target 的多目标 Dockerfile。
- P1.2：固定 .NET SDK 和 runtime 镜像版本，不使用 floating tag 作为生产基线。
- P1.3：把 restore、build、publish 和 runtime 分成多阶段构建。
- P1.4：确保 build context 不依赖开发机绝对路径、`bin`、`obj` 或本地 NuGet 缓存。
- P1.5：运行容器使用非 root 用户。
- P1.6：镜像只复制发布产物和必要配置，不把源码、测试产物或本地 secrets 带进 runtime 层。
- P1.7：定义镜像 tag 规则：`agar-silo:<version>-<sha>`、`agar-gateway:<version>-<sha>`。
- P1.8：写出本地构建命令和 CI 构建命令。

验收标准：

- 干净环境可以构建状态服务和 gateway 镜像。
- 状态服务和 gateway 镜像可以打印版本信息或启动到配置校验阶段。
- 镜像不包含生产 secrets，不依赖本地绝对路径。

#### P2：Docker Compose 拓扑

目标：

- 建立可用于小规模生产演练的 Docker Compose 拓扑。

具体任务：

- P2.1：保留现有 `docker-compose.yml` 作为本地 PostgreSQL/Redis 开发基础设施。
- P2.2：新增生产 compose 或 compose override，加入 `silo`、`gateway`、`postgres`、`redis` 和可选反向代理。
- P2.3：为 PostgreSQL 和 Redis 配置持久化 volume。
- P2.4：定义 Docker network，区分容器内端口和公网暴露端口。
- P2.5：明确控制面 WebSocket 端口暴露方式。
- P2.6：明确 KCP 实时端口暴露方式，按实际传输要求配置 UDP/TCP。
- P2.7：为 `silo`、`gateway`、`postgres`、`redis` 配置 restart 策略。
- P2.8：为服务之间的启动顺序增加 healthcheck 或显式等待策略，避免只依赖容器启动顺序。
- P2.9：提供单状态服务 + 单 gateway 的 compose 启动命令。
- P2.10：提供单状态服务 + 双 gateway 的 compose 启动命令或 scale 说明。

验收标准：

- 一条 Docker Compose 命令可以启动 PostgreSQL、Redis、状态服务和 gateway。
- PostgreSQL/Redis 数据写入持久化 volume。
- 容器重启后服务可以恢复到可连接状态。

#### P3：生产配置与密钥

目标：

- 把开发配置迁移为环境变量驱动，生产 secrets 不写入仓库。

具体任务：

- P3.1：梳理状态服务必填配置：PostgreSQL 连接串、ServiceId、AdvertisedIPAddress、服务端口。
- P3.2：梳理 gateway 必填配置：ServiceId、`Lakona.Game:Node:Id`、`Lakona.Game:Endpoints` 中的 websocket 控制端点和 kcp realtime 端点。
- P3.3：梳理业务配置：房间容量、匹配超时、断线保留时间、可靠 push 保留时间、排行榜时区、排行榜归档周期。
- P3.4：新增或更新 `.env.example`，仅保留开发默认值。
- P3.5：新增生产 env 模板，所有密码、token secret、公网 host、连接串都要求显式填写。
- P3.6：让 appsettings 支持环境变量覆盖，不需要改代码即可切换开发和生产类环境。
- P3.7：定义 secret 管理策略：本地 compose 用 env 文件，生产主机使用受控 secret 文件或部署平台 secret。

验收标准：

- 不改代码即可切换开发 compose 和生产 compose 配置。
- 仓库中不出现生产密码、生产连接串或 token secret。
- 缺少生产必填配置时，服务启动失败并给出明确日志。

#### P4：健康检查与生命周期

目标：

- Docker 和运维系统能判断服务是否可启动、可连接、可接流量。

具体任务：

- P4.1：为状态服务定义健康检查：进程存活、配置有效、PostgreSQL 可连接、状态服务可启动。
- P4.2：为 gateway 定义健康检查：进程存活、状态服务可连接、控制面 RPC server 可监听、实时端口可监听。
- P4.3：在 compose 中接入状态服务和 gateway healthcheck。
- P4.4：定义优雅停止行为：收到 SIGTERM 后停止接受新连接、清理本地会话、释放房间 runtime。
- P4.5：验证容器 restart 后不会留下不可恢复的服务注册或本地注册状态。

验收标准：

- `docker compose ps` 能看到关键容器进入 healthy。
- 关闭 gateway 容器后，日志中能看到清理路径。
- 重启 gateway 后可以重新登录并进入匹配。

#### P5：PostgreSQL 迁移、备份与恢复

目标：

- 生产数据库升级和恢复不依赖手工复制 init SQL。

具体任务：

- P5.1：盘点现有 `infra/postgres/init` SQL 的职责和执行顺序。
- P5.2：选定迁移方式：脚本化 SQL 版本目录、迁移工具，或容器启动前一次性迁移 job。
- P5.3：建立迁移版本表或等价记录，避免重复执行破坏性 SQL。
- P5.4：写出初始化新库流程。
- P5.5：写出从旧版本升级到新版本流程。
- P5.6：写出 PostgreSQL 备份命令。
- P5.7：写出 PostgreSQL 恢复演练步骤。
- P5.8：验证排行榜和持久化状态在恢复后可读取。

验收标准：

- 空库可以按脚本初始化。
- 已初始化库可以安全重复执行迁移入口。
- 备份文件可以恢复到新 PostgreSQL 实例，并通过状态服务启动校验。

#### P6：Redis 用途与生产配置

目标：

- 明确 Redis 在首发中的职责，确保阶段 13 的排行榜 sorted set 方案进入生产部署边界。

具体任务：

- P6.1：确认阶段 13 的 Redis 排行榜 key、TTL、归档和不可用降级策略已经实现并写入配置。
- P6.2：为 Redis 配置密码、持久化、连接池、operation timeout 和 key prefix。
- P6.3：确认 Docker compose 中 Redis 开启持久化 volume，不把排行榜当前周期数据写入临时容器层。
- P6.4：确认 Redis 不可用时排行榜查询返回可解释错误或空结果，对局结算主流程不整体崩溃。
- P6.5：验证内存 reliable push outbox 在网关重启后的玩家体验和客户端恢复路径；框架和 sample 已接入 state lost / new session 语义。
- P6.6：明确 Redis 后续是否继续承担跨网关路由、在线状态或限流；未实现的职责不要写成当前能力。

验收标准：

- Redis 在首发中的职责有明确文档：当前至少承担胜利积分排行榜 sorted set。
- Redis 排行榜路径已由阶段 13 验证通过。
- reliable push 的进程重启行为有明确验收和玩家体验。
- Redis 密码和持久化策略进入生产配置。

#### P7：账号安全与会话安全

目标：

- 外部上线前消除明文凭据和基础越权风险。

具体任务：

- P7.1：检查用户状态服务当前账号和密码存储方式。
- P7.2：将密码改为不可逆哈希存储，选择 salt 和迭代策略。
- P7.3：为游客登录生成可撤销凭据或明确游客会话生命周期。
- P7.4：统一 token 生成、过期、刷新和失效策略。
- P7.5：校验 `playerId` 与 token 的绑定。
- P7.6：校验 `roomId`、`matchId` 与当前玩家会话的绑定。
- P7.7：禁止客户端伪造他人输入、房间绑定或排行榜敏感查询。
- P7.8：为重复登录和顶号行为定义服务端策略与客户端提示。

验收标准：

- 数据库中不保存明文密码。
- 伪造 playerId/token/roomId/matchId 的请求会被拒绝。
- 重复登录行为可预期，客户端有中文提示。

#### P8：限流与输入防滥用

目标：

- 防止登录、匹配、排行榜和输入接口被简单滥用拖垮服务。

具体任务：

- P8.1：定义登录尝试限流维度：账号、IP、连接、设备或 token。
- P8.2：定义匹配和取消匹配的冷却与重复请求处理。
- P8.3：定义排行榜查询频率限制和客户端缓存刷新间隔。
- P8.4：定义输入提交最大频率，超过频率时丢弃或采样。
- P8.5：服务端继续只信任输入方向，不信任客户端位置、质量、速度、排名。
- P8.6：记录被限流或拒绝的请求日志，避免玩家无反馈。

验收标准：

- 重复点击匹配/取消不会产生重复票据。
- 高频输入不会让服务端无限排队。
- 限流触发时服务端有日志，客户端有合理提示或静默降级。

#### P9：日志、指标与告警

目标：

- 上线后能定位玩家无法登录、无法匹配、无法进入房间、结算不发奖等问题。

具体任务：

- P9.1：统一日志字段：playerId、roomId、matchId、edgeNodeId、connectionId、requestId。
- P9.2：补登录成功/失败日志。
- P9.3：补匹配入队、取消、超时、成局日志。
- P9.4：补房间创建、runtime owner、玩家加入、玩家离开、空房释放日志。
- P9.5：补实时绑定、输入投递失败、世界状态广播失败日志。
- P9.6：补结算、胜利积分发放、排行榜查询和周重置日志。
- P9.7：定义指标：在线人数、匹配队列长度、房间数、输入延迟、快照频率、断线率、RPC 错误率、数据库错误率。
- P9.8：定义告警：Gateway/State unhealthy、PostgreSQL/Redis 不可用、匹配积压、房间异常退出、错误率升高。
- P9.9：确认 `docker logs` 能完成最小排障；后续可接入集中日志系统。

验收标准：

- 一次联机完整对局能通过日志串起登录、匹配、房间、结算和发奖。
- 核心异常路径有错误日志。
- 告警条件和阈值有文档记录。

#### P10：客户端网络体验补齐

目标：

- 生产环境网络失败时，玩家不会停在无反馈界面。

具体任务：

- P10.1：连接中、重连中、匹配中、取消中显示明确状态。
- P10.2：控制连接断开时提示并提供重试或返回入口。
- P10.3：实时连接断开时短暂等待，再进入重连或返回大厅。
- P10.4：服务器维护或服务不可用时给出中文提示。
- P10.5：回归 state lost / outbox 过期时清理旧会话，进入新登录或大厅路径；当前已由 `ClientSessionController` 和 sample reset path 承接。
- P10.6：回归可靠推送重复到达时保持幂等，不重复跳转或重复发起实时绑定；当前已由 `ReliablePushInbox` 承担基础去重。
- P10.7：回归 latest reliable sequence 绑定到当前玩家会话，新会话重置或隔离旧序列；当前已使用 session id/generation。
- P10.8：所有网络按钮在请求中禁用，失败或超时后恢复。

验收标准：

- 玩家看不到无响应按钮。
- 控制断线、实时断线、状态丢失都有可回收路径。
- 重复可靠推送不会造成重复绑定或重复 UI 状态跳转。

#### P11：单网关生产演练

目标：

- 在 Docker 单 gateway 拓扑下完成主链路回归。

具体任务：

- P11.1：用生产 compose 启动 PostgreSQL、Redis、状态服务、gateway。
- P11.2：启动 Unity 客户端，完成游客登录。
- P11.3：完成匹配、实时绑定和一局对战。
- P11.4：验证结算和胜利积分发放。
- P11.5：验证排行榜刷新，并确认排行榜数据来自 Redis sorted set 索引。
- P11.6：重启 gateway，验证玩家重新登录和匹配。
- P11.7：重启状态服务，验证服务恢复策略和日志。
- P11.8：导出本轮日志，确认排障字段齐全。

验收标准：

- Docker 单网关联机完整一局通过。
- Gateway/State 重启后的玩家体验符合设计。
- 日志能定位本轮对局的关键事件。

#### P12：跨网关实时路由设计

目标：

- 在实现前明确多 gateway 下输入、世界状态和断线事件的所有权。

具体任务：

- P12.1：选择网关间事件机制：Redis 发布订阅、Lakona.Game.Cluster 或应用自定义消息总线。
- P12.2：定义 room runtime owner 的创建、续租、过期和释放规则。
- P12.3：定义输入从非 owner gateway 转发到 owner gateway 的消息格式。
- P12.4：定义世界状态从 owner gateway 扇出到玩家所在 gateway 的消息格式。
- P12.5：定义断线、登出、取消匹配、离房在跨网关下的事件顺序。
- P12.6：定义背压策略：输入过期、队列满、慢客户端、广播失败。
- P12.7：定义重试策略和不可恢复失败的玩家体验。
- P12.8：更新 `features/server-architecture.md` 和 `GAMEPLAY_DESIGN.md`。

验收标准：

- 设计能解释控制连接在 gateway A、runtime 在 gateway B 的完整路径。
- 失败行为足够明确，可以编写测试和手动回归步骤。
- 没有要求序列化实时 RPC callback 对象。

#### P13：跨网关实时路由实现

目标：

- 支持两个 gateway 部署下的真实联机对局。

具体任务：

- P13.1：实现输入转发通道。
- P13.2：实现世界状态回送通道。
- P13.3：实现死亡事件和结算事件回送。
- P13.4：实现 runtime owner 不存在或过期时的失败处理。
- P13.5：实现跨网关断线、登出和离房清理。
- P13.6：为路由投递失败、队列背压、owner 过期增加日志。
- P13.7：增加单元测试或集成测试覆盖路由决策。
- P13.8：保留单网关路径，不因跨网关路由引入回归。

验收标准：

- 两个 gateway 下，玩家可以分别连接不同 gateway 并完成一局。
- 世界状态能送达不同 gateway 上的玩家。
- 关闭一个 gateway 时，受影响玩家有明确失败或重连体验，其他房间不受影响。

#### P14：发布产物与回滚

目标：

- 每次上线都有明确产物、版本、命令和回滚路径。

具体任务：

- P14.1：确定服务端版本号来源。
- P14.2：生成状态服务和 gateway 镜像 tag。
- P14.3：写出 Docker build 命令。
- P14.4：写出 Docker push/pull 命令。
- P14.5：写出生产 compose up/down 命令。
- P14.6：写出查看日志和健康状态命令。
- P14.7：写出回滚到上一镜像 tag 的步骤。
- P14.8：写出数据库迁移失败时的停止和恢复策略。
- P14.9：写出 Unity 客户端构建版本和服务端版本兼容说明。

验收标准：

- 新版本可以按文档部署。
- 上一版本可以按文档回滚。
- 版本号能关联到 git commit 和镜像 tag。

#### P15：上线候选验收

目标：

- 把代码、Docker 部署、客户端、测试、文档和运维演练收敛成上线候选版本。

具体任务：

- P15.1：运行 `dotnet build Shared/Shared.csproj -f net10.0`。
- P15.2：运行 `dotnet build Server/State/State.csproj`。
- P15.3：运行 `dotnet build Server/Gateway/Gateway.csproj`。
- P15.4：运行 `dotnet test tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj`。
- P15.5：执行 Unity 脚本编译检查。
- P15.6：完成单机完整一局人工回归。
- P15.7：完成 Docker 单网关联机完整一局人工回归。
- P15.8：完成 Docker 双 gateway 联机完整一局人工回归。
- P15.9：完成断线场景回归：匹配中断线、控制连接断开、实时连接断开、gateway 重启、状态服务重启、重复登录。
- P15.10：完成视觉验收：入口、登录、匹配、大厅、HUD、局内排名、结算、排行榜。
- P15.11：确认素材、字体和第三方资源许可。
- P15.12：确认 README、`PRODUCTION_LAUNCH_PLAN.md`、`GAMEPLAY_DESIGN.md` 和功能文档与实现一致。
- P15.13：完成 PostgreSQL 备份和恢复演练。
- P15.14：完成发布和回滚演练。

验收标准：

- 所有自动化测试和人工回归通过。
- Docker 镜像、compose、env 模板、日志、健康检查和回滚步骤可用。
- 没有阻塞级风险留到上线后处理。

## 已完成记录

这一节记录已经完成的事实，供回溯使用；不要把这里的条目当作后续执行待办。

### 阶段 1：清理未上线与调试 UI

验收整理（2026-05-08）：

阶段 1 原待办共 8 项，当前 8 项完成。玩家可见路径和本地进度数据模型均已收敛到首发范围。

| 序号 | 原待办 | 状态 | 说明 |
| --- | --- | --- | --- |
| 1 | 移除 Tasks / Shop / Records 的玩家可见入口、页签、快捷按钮和跳转映射 | 完成 | 大厅只保留资料、排行榜、设置；旧按钮隐藏且不再绑定动作。 |
| 2 | 移除任务、商店、记录页面内容、按钮文案、状态提示和事件提示 | 完成 | 玩家可见 UI 不再展示任务、商店、记录相关页面或提示。 |
| 3 | 清理结算界面的任务摘要 | 完成 | 结算只展示结果、质量、奖励、下一步、再来一局和返回大厅。 |
| 4 | 清理商店/记录相关图标加载和挂载路径 | 完成 | 运行时不再加载或挂载 `Icon_Shop_01.png`。 |
| 5 | 删除 debug 面板、连接端点、内部状态、tick、同步视图数和快捷键提示 | 完成 | 玩家界面、联机大厅和战斗内 HUD 不再展示这些内部信息；开发诊断只进 console/log。 |
| 6 | 将玩家可见文案收敛为中文 | 完成 | 入口、登录、匹配、大厅、HUD、结算、排行榜、设置和错误状态已中文化；后续仅保留常规措辞打磨。 |
| 7 | 评估并清理 `DotArenaMetaProgression` 中任务、商店、记录相关模型 | 完成 | 已删除任务和商店模型/API；保留皮肤装备、最近对局趋势和排行榜缓存作为资料、表现和排行榜实际使用的数据。 |
| 8 | 同步 `ART_DIRECTION.md` 和客户端架构文档 | 完成 | 文档已明确当前 UI 验收范围暂不包含任务、商店和记录，玩家 UI 使用中文。 |

- 已从大厅玩家可见导航中移除任务、商店和记录入口；保留资料、排行榜和设置。
- 已禁用大厅快捷按钮到任务、商店和记录页面的跳转，旧场景按钮会被隐藏且不再绑定动作。
- 已从结算界面移除任务摘要；结算只展示结果、质量、奖励、下一步、再来一局和返回大厅。
- 已停止运行时 UI 加载和挂载 `Icon_Shop_01.png`。
- 已删除玩家界面上的 debug 面板，并清理联机大厅和备用 HUD 中的 endpoint、tick、同步视图数、快捷键提示等内部信息；联机大厅和战斗内 HUD 不应显示 DEBUG 信息。
- 已将入口、登录、匹配、大厅、对局 HUD、结算、排行榜、设置和错误状态的玩家可见文案收敛为中文。
- `DotArenaMetaProgression` 不再包含任务进度、任务领取、商店目录、购买或商店可购买性 API；旧本地存档中的任务字段在新模型中不再读取。
- `ART_DIRECTION.md` 和 `docs/features/client-architecture.md` 已同步当前 UI 验收范围：暂不包含任务、商店和记录，玩家 UI 使用中文。

### 阶段 3：战斗中实时排名面板

- 已在对局中增加右侧当前对局排名面板，非对局界面隐藏。
- 排名数据来自 `DotArenaGame` 的当前 `PlayerRenderState` 集合，单机和联机共用同一套展示逻辑。
- 排序规则需要按新口径调整为整型质量降序、玩家标识升序；旧分数不再参与单机或联机战斗内实时排名。
- 面板应展示名次、玩家名和整型质量，本地玩家行使用高亮背景和文字颜色；旧分数列、表头和行文本应彻底移除。
- 面板背景需要按新视觉要求改为低遮挡半透明；当前背景框遮挡游戏画面，影响用户体验，应作为后续 UI 修复项处理。
- 已为右侧实时排名面板生成并接入专用低遮挡背景 `UI_HUD_Rank_Panel_Translucent_01.png`；中心 alpha 约 9%，边缘 alpha 约 33%，角落透明，并在 UI 侧再乘 72% 整体透明度。
- 已降低排名行背景透明度：普通行约 6% 到 10%，本地玩家行约 20%，避免行底色继续遮挡游戏画面。
- 面板文案为中文，不展示 tick、连接状态、端点等内部字段。
- Unity 脚本刷新通过，控制台无编译错误；人工视觉回归仍在阶段 2/5 继续覆盖。

### 阶段 2：启动界面视觉修复

- 已接入非对局 UI 背景：入口、匹配、大厅和结算这些状态显示 `BG_Arena_Grid_Dark_01.png`，实际对局中隐藏菜单背景。
- 已为战斗内右侧实时排名面板接入专用半透明背景 `UI_HUD_Rank_Panel_Translucent_01.png`，不再复用通用深色面板。
- 已校正入口/模式选择按钮布局，三个模式按钮同轴、等尺寸、文字居中。
- 已移除联机登录界面的 `Enter account credentials` 和 `联机登录` 标题文案。
- 已把 `MultiplayerPanel` 从铺满面板背景改为独立内容安全区，登录表单控件相对安全区排布。
- 已把 UI sprite 透明边缘约束写入 `ART_DIRECTION.md`。
- 已扫描并裁切 `Assets/Art` 下透明外边距超过 5% 的正式 PNG：`UI_Panel_Dark_01.png`、`UI_Button_Primary_Normal.png`、`UI_Button_Primary_Pressed.png`、`Icon_Leaderboard_01.png`、`Icon_Shop_01.png`、`FX_Absorb_Ring_01.png`、`FX_Spawn_Wave_01.png`、`Pickup_Mass_Gold_01.png`、`Pickup_Mass_Teal_01.png`。
- 资源裁切后重新扫描正式 PNG，透明外边距违规列表为空。
- Unity 资源刷新和脚本编译检查通过，控制台无错误。

### 阶段 4：AI 生成美术资产

- 第一批玩家球皮肤已生成并接入运行时：
  - `Skin_Jelly_Cyan.png`
  - `Skin_Jelly_Crimson.png`
  - `Skin_Jelly_Sunburst.png`
- 第一轮基础美术资产已进入 Unity 项目目录，并已完成脚本侧运行时接入：玩家球皮肤、质量拾取物、竞技场背景、入口/匹配/大厅/结算按钮基准图、排行榜图标、吸收环和出生波纹。
- `ART_DIRECTION.md` 已作为后续资产生成、UI 设计、AI 生成提示词、Unity 接入和验收的风格标准。

### 阶段 5：玩法回归与协议清理

- 已完成 HUD 文案扫描和清理，可见 UI 路径不再引用冲刺、强化、眩晕等旧玩法语义。
- 已从 `InputMessage` 中移除 `Dash` 字段。
- 已从 `PlayerLifeState` 中移除 `Dash` 和 `Stunned`。
- 已从 `ArenaSimulationOptions.EnabledPickupTypes` 默认值和 `PickupType` 枚举中移除旧强化拾取物，并将质量拾取物命名改为 `MassPoint`。
- 已从 `PlayerState` 中移除旧强化剩余时间字段。
- Unity 脚本刷新通过，KCP factory 引用保持正常。
### 阶段 6：客户端拆分

- `DotArenaMetaProgression` 已按关注点拆分为 Models、Persistence、Queries、Rules 四个 partial 文件。
- 已完成第一轮客户端职责拆分：新增 `DotArenaMultiplayerState` 集中保存联机会话身份、认证资料、待处理 UI 请求、重连标记、匹配计时、实时连接和可靠推送/session controller；`DotArenaGame` 通过转发属性兼容现有 partial，后续还需继续减少直接状态写入。
- 已新增 `DotArenaSinglePlayerController`，负责本地模拟创建、无敌模式选项、本地输入提交、tick catch-up 和模拟推进；`DotArenaGame.SinglePlayer.cs` 改为把单机结果接回现有表现和结算流程的适配层。
- 已新增 `DotArenaUiFactory` 和 `DotArenaUiStyleCatalog`，将 `DotArenaSceneUiPresenter.Layout*.cs` 与 `Styling.cs` 中重复的运行时文本、按钮、面板、输入框创建和样式逻辑抽到共享工厂/样式目录。
- 已把 UI 快照构建移动到 `DotArenaGame.UiSnapshot.cs`，让 `DotArenaGame.UiSurface.cs` 更集中于 UI 命令处理。
- 本轮客户端脚本编译通过：`dotnet build samples/Game.Unity.Agar/Client/SampleClient.Gameplay.csproj --no-restore`，0 警告、0 错误。
- 尚未达到阶段 6 的最终验收硬指标：`DotArenaGame` 仍有多份 partial 文件和较多转发状态，`DotArenaSceneUiPresenter` 仍未拆成独立面板 presenter；完整单机/联机手动回归仍需在 Unity 游戏视图执行。

### 阶段 7：网关清理语义与框架迁移

- `Lakona.Game.Server.Sessions` 已提供 `GameSessionKey`、`SessionEndpointKey`、`IGameSessionDirectory`、in-memory directory、resume decision、token validator hook、authoritative state probe 和 cleanup hosted service。
- `Lakona.Game.Server.ReliablePush` 已提供 session/generation-aware ack outcome、`IReliablePushAckService` 和 `GameSessionKey` owner key 扩展。
- `Lakona.Game.Client` 已提供 `ReliablePushSession`、`ReliablePushInbox`、cursor store、`ClientSessionController` 和 deterministic transition tests。
- Agar Unity 的 `SessionDirectory` / `PlayerService` 已迁移到框架会话目录：控制端点和实时端点以 endpoint name 绑定 opaque callback，reconnect 通过 framework resume decision 判断 state lost。
- Agar Unity 的可靠匹配推送已按 `GameSessionKey` 隔离 publish/replay/ack；ack 请求携带 session id/generation，old generation 或未知 sequence 会映射为 state lost / session mismatch。
- Unity 客户端 reconnect、state-lost 和 reliable push cursor 已接入 `ClientSessionController` / `ReliablePushInbox`，新会话会隔离旧可靠推送序列。
- 本轮验证通过：`dotnet test Tests/tests.slnx --no-restore`、`dotnet test samples/Game.Unity.Agar/tests/BusinessLogic.Tests/BusinessLogic.Tests.csproj --no-restore`、`dotnet build samples/Game.Unity.Agar/Server/Gateway/Gateway.csproj --no-restore`、`dotnet build samples/Game.Unity.Agar/Client/SampleClient.Gameplay.csproj --no-restore`。
- 剩余工作不是重新实现会话可靠性，而是补充 `PlayerService`、`RoomRuntime`、`DotArenaNetworkSession` 和端到端断线/重启路径的生产回归覆盖。

### 阶段 8：胜利积分与排行榜

- 已在用户状态合同、`UserLoginResult` 和 `UserProfileSnapshot` 中增加胜利积分相关合同。
- 已在用户状态服务中持久化 `VictoryPoints` 并实现积分增加。
- 已新增排行榜服务合同和实现。
- 排行榜服务已维护由对局结算写入的排行榜积分索引，并按积分、胜场和玩家标识排序。
- 排行榜周期已改为榜单当地时间周一 00:00；排行榜服务当前使用 `TimeZoneInfo.Local`。
- 周期重置已归档上周 top 100，并只保留最近两周归档。
- 服务端合同已新增 `PeriodStartLocalDate`；旧 `PeriodStartUtc` 字段暂保留兼容。
- `RoomRuntime.PersistMatchEndAsync` 已按排名发放胜利积分，并过滤 AI 玩家。
- `IPlayerService` 已新增 `GetLeaderboardAsync`，`PlayerService` 已转发到排行榜服务。
- 客户端已从本地 mock 排行榜切换到服务端真实排行榜数据缓存展示。
- 排行榜 UI 已展示当前周期剩余时间。

### 阶段 11：测试扩展

- 当前自动化测试数量为 31 个。
- `ArenaSimulationRulesTests` 已覆盖 17 个模拟规则测试用例。
- `MatchmakingQueuePolicyTests` 已覆盖 4 个匹配队列策略测试。
- 排行榜测试已覆盖 6 个排序、当地时区周重置、归档保留和旧 UTC 周期迁移测试。
- `SessionDirectoryCleanupTests` 已覆盖 4 个房间和实时注册清理测试。
- AI 过滤由 `VictoryPointAwards` 测试覆盖。

### 阶段 12：验证与打包

- 本轮已通过 `Shared/Shared.csproj` 构建。
- 本轮已通过 `Server/State/State.csproj` 构建。
- 本轮已通过 `Server/Gateway/Gateway.csproj` 构建。
- 本轮已通过已有自动化测试，31/31。
- Unity 脚本刷新和控制台错误检查已通过；完整手动游玩仍需按待办执行。
- README 已同步到当时的命令、端口和架构事实。
