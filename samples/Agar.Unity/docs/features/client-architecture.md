# 客户端架构

这份文档描述 Unity 客户端的职责边界、关键代码位置、依赖方向、已知重构目标和表现原则。核心玩法见 [`gameplay-rules.md`](gameplay-rules.md)，服务端架构见 [`server-architecture.md`](server-architecture.md)。

## 客户端边界

Unity 客户端负责：

- 启动菜单和模式切换。
- 本地单机模拟。
- 联机输入发送。
- 世界状态接收和渲染。
- HUD、玩家表现、食物表现和结算界面。
- 控制连接、实时连接、断线处理和可靠推送确认。
- 本地元进度展示和存储。
- 排行榜拉取和展示。

重要代码位置：

- `Client/Assets/Scenes/Gameplay.unity`：场景来源。
- `Client/Assets/Scripts/Rpc`：传输创建、source generator 标记和 RPC 访问入口。
- `Client/Assets/Scripts/Gameplay`：主流程、同步、视图、HUD 和本地进度。

依赖方向：

```txt
Shared -> Lakona.Rpc.Analyzers compiler output -> SampleClient.Rpc -> SampleClient.Gameplay
```

`Gameplay` 可以依赖 RPC 辅助代码；RPC 辅助代码不能反向依赖玩法界面代码。

## Prefab 使用策略

当前 Unity 客户端刻意少用 prefab。`Client/Assets/Prefabs` 里只保留少量 placeholder prefab；实际玩家视图、食物视图、HUD、启动菜单、匹配面板、大厅和结算界面主要由 `Client/Assets/Scripts/Gameplay` 下的运行时代码创建和刷新。

对当前样例阶段，这个选择有利于开发效率：

- 运行时对象结构和网络状态流集中在代码中，便于 review、diff 和复制到其他样例。
- 单机和联机都依赖动态世界状态，玩家、食物、结算和排行榜界面会随会话状态频繁重建或刷新，用代码工厂更容易保持确定性。
- 示例项目对跨机器打开、CI 构建和协议演示的要求高，少量编辑器侧手工引用可以降低场景、prefab 或 Inspector 引用损坏的概率。
- 玩法表现仍可接入正式 Sprite、材质和字体资源；少 prefab 不等于少资源，而是把对象装配和状态绑定放在代码侧。

长期维护上，完全依赖运行时 `new GameObject` / `AddComponent` 也会累积风险：

- UI 尺寸、位置、字体、颜色和层级散落在 C# 中，视觉调试效率低。
- 美术、设计或非程序成员难以直接在 Unity 编辑器中调整稳定结构。
- 稳定组件的层级在运行前不可见，复杂界面增长后不利于理解和回归。
- 按钮、面板、玩家视图、拾取物视图和常用特效这类稳定结构如果长期全部写在代码里，会增加重复和改动面。

因此后续维护应采用混合策略：运行时变化和网络逻辑继续留在代码里，稳定视觉结构逐步沉淀为 prefab 或可复用资源。

适合继续由代码创建的内容：

- 服务端世界状态驱动的玩家、食物、临时特效和对象池。
- 网络会话状态机、输入发送、世界同步和 UI 状态刷新。
- 纯逻辑 GameObject、一次性占位对象和由数据表驱动的轻量实体。

适合逐步引入 prefab 的内容：

- 稳定 UI 面板、按钮模板、输入框模板、HUD/Overlay 模板。
- 玩家 View 根节点、Pickup View 根节点和固定子节点组合。
- 常用 FX 组合、九宫格 UI 基础件和需要编辑器调参的视觉结构。

阶段 6 客户端拆分时，可以优先从 `DotArenaSceneUiPresenter` 中抽出重复 UI 构建逻辑，先用小型 UI 工厂或面板 presenter 收敛重复代码；当结构稳定且需要可视化调参时，再把稳定控件模板资源化。目标不是把客户端改成全 prefab，而是减少运行时控件构建代码，让 prefab 或工厂承担稳定视觉结构，代码承担动态状态和网络行为。

## 已知重构目标

- `DotArenaGame.cs` 的第一轮拆分已把联机身份/请求状态收敛到 `DotArenaMultiplayerState`，把本地模拟推进收敛到 `DotArenaSinglePlayerController`，并把 UI 快照构建移动到独立 partial。`DotArenaGame` 仍分散在多份 partial 文件中，后续还需要继续收敛输入、世界表现、资源/皮肤和 UI 命令边界。
- `DotArenaSceneUiPresenter` 的第一轮拆分已引入 `DotArenaUiFactory` 和 `DotArenaUiStyleCatalog`，把重复文本、按钮、面板和输入框创建/样式逻辑从 Layout/Styling 文件中抽出。Presenter 仍偏大，后续需要继续把入口、登录、匹配中、大厅、HUD、对局排名和结算拆成独立面板 presenter 或稳定 prefab。
- 后续拆分的目标是让 `DotArenaGame` 退回 Unity 场景组合根，让会话流程、单机模拟、输入、世界表现、UI 快照、资源/皮肤和本地元进度分别拥有清晰所有者。
- `DotArenaSceneUiPresenter` 应退回 UI 根协调器；入口/模式选择、登录、匹配中、大厅、HUD、对局排名和结算应由独立 presenter、稳定 prefab 或小型工厂承载。调试面板不作为玩家 UI 保留。
- `DotArenaMetaProgression` 已拆分完成（Models、Persistence、Queries、Rules）。本地 mock 排行榜已淘汰，`GetLeaderboardSummary` 现在展示服务端 RPC 刷新后的缓存数据。
- 计划中的拆分工作见 `DEVELOPMENT_PLAN.md` 阶段 6。

建议的目标结构：

```txt
DotArenaGame
  Unity 生命周期入口、组件装配、跨组件调度

DotArenaClientFlowState
  当前模式、前端流程、入口菜单、忙碌状态、匹配计时和待处理 UI 请求

DotArenaMultiplayerState / DotArenaMultiplayerFlow
  登录、游客登录、匹配、取消匹配、实时绑定、断线恢复、可靠推送确认

DotArenaSinglePlayerController
  本地模拟创建、tick 推进、结算和重开

DotArenaViewRegistry / DotArenaPresentationCatalog
  玩家视图、拾取物视图、渲染状态、sprite、shader 和皮肤选择

DotArenaUiSnapshotBuilder / DotArenaUiCommandRouter
  从状态构建 UI 快照，把按钮和输入框请求转成明确命令

DotArenaSceneUiPresenter
  UI 根对象绑定和面板 presenter 调度

DotArenaEntryMenuPresenter / DotArenaLoginPresenter / DotArenaMatchmakingPresenter
DotArenaLobbyPresenter / DotArenaHudPresenter / DotArenaSettlementPresenter
DotArenaMatchRankingPresenter
  各面板字段、布局、刷新和按钮绑定

DotArenaUiFactory / DotArenaUiStyleCatalog
  重复文本、按钮、面板、九宫格和字体样式创建
```

## 表现原则

- 玩家显示大小跟随 `Radius`。
- HUD 强调名字、质量、排名和存活状态。
- 战斗内实时排名只展示整型质量。单机和联机共用同一套排名口径，表头、行内容和排序都不出现“分数”。
- 战斗内实时排名面板使用低遮挡半透明背景，不能用不透明或过深的背景框遮挡玩法画面；本地玩家行高亮也必须保持半透明。
- 联机大厅和战斗内 HUD 不显示 DEBUG 信息。`tick`、连接端点、连接状态细节、内部状态枚举、同步视图数、快捷键提示和开发诊断文本只能进入 Unity Console、服务端日志或客户端日志，不能出现在玩家界面，也不保留调试面板入口。
- 食物要小且数量多。
- 界面文案使用质量、排名、成长和存活语义，不使用冲刺或技能强化作为核心表达。
- 玩家碰撞和成长可以有果冻感表现，但眩晕、击退不应被当作当前核心玩法。
- 排行榜界面展示当前周期剩余时间、玩家排名、胜利积分和胜场数。
- 当前美术接入优先使用 `Client/Assets/Art` 下已导入 Sprite：玩家皮肤、质量拾取物、竞技场背景、UI 面板/按钮、排行榜图标和吸收/出生 FX 都由 Gameplay 运行时视图按需挂载，缺失资产时回退到脚本生成的占位 sprite。未上线的任务、商店和记录页面不属于当前玩家 UI；相关任务和商店后台模型已从本地进度中移除。
- 本地玩家默认小球颜色由客户端在每局开始时随机决定，候选色为蓝色、橙色、绿色。该随机值只影响表现层，不写入 `Shared` 协议，也不改变服务端权威世界状态。玩家明确装备非默认皮肤时，仍按皮肤自己的颜色表现。
