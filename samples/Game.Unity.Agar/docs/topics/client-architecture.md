# 客户端架构

这份文档描述 Unity 客户端的当前职责边界、对象装配方式和表现约束。核心玩法见 [`gameplay-rules.md`](gameplay-rules.md)，服务端架构见 [`server-architecture.md`](server-architecture.md)。

## 客户端边界

Unity 客户端负责：

- 启动菜单、登录、模式切换、匹配和结算流程。
- 单机 `ArenaSimulation` 和联机 `FrameSyncSimulation` 的本地推进。
- 联机输入发送、启动消息和输入帧接收。
- 从本地模拟结果生成世界状态、死亡事件、胜负结果和渲染表现。
- 控制连接、实时连接、断线处理和可靠推送确认。
- HUD、玩家/食物表现、本地元进度和排行榜展示。

客户端不决定服务端会话身份、房间归属、帧序号、胜利积分写入或排行榜持久化。联机结算由客户端计算后上报，但服务端只接受当前房间真人成员的数据；该结果仍属于不可信输入。

## 代码边界

重要位置：

- `Client/Assets/Scenes/Gameplay.unity`：场景入口。
- `Client/Assets/Prefabs/UI`：入口、登录、匹配、大厅、结算和场景 UI prefab。
- `Client/Assets/Scripts/Rpc`：传输创建、source generator 标记和 RPC 访问入口。
- `Client/Assets/Scripts/Gameplay`：客户端流程、模拟适配、表现和 UI 绑定。
- `Shared/Gameplay`：单机和联机共同使用的确定性玩法内核。

主要协作对象：

```txt
DotArenaGame
  Unity 生命周期和客户端组合根

DotArenaNetworkSession / ClientSessionController
  控制连接、实时连接、RPC 调用和可靠推送会话

DotArenaCallbackInbox
  跨线程 callback 入队和主线程批量消费

DotArenaSinglePlayerController
  单机模拟创建、推进和重开

FrameSyncSimulation
  联机启动、乱序帧缓存、连续帧推进和本地战斗结果

DotArenaWorldSynchronizer / DotArenaGame.Views.cs
  本地 WorldState 到玩家、食物和表现状态的同步

DotArenaSceneUiPresenter / DotArenaGameUiSurface
  场景 prefab 绑定、UI 快照刷新和玩家命令路由

DotArenaMetaProgression
  本地展示状态和服务端排行榜缓存
```

`DotArenaGame` 使用 partial 文件按输入、会话、回调、单机、表现、UI 和测试入口分区。partial 不是新的 ownership boundary；新增状态应优先进入拥有明确生命周期的协作对象，组合根只负责装配和跨组件调度。

依赖方向：

```txt
Shared -> Lakona.Rpc.Analyzers compiler output -> SampleClient.Rpc -> SampleClient.Gameplay
```

`Gameplay` 可以依赖 RPC 辅助代码；RPC 辅助代码不能反向依赖玩法界面代码。`Shared` 不依赖 Unity UI、客户端传输或场景对象。

## Prefab 与运行时对象

稳定屏幕结构由 prefab 持有：

- `SceneUI.prefab` 组合各屏幕面板和 HUD overlay。
- `ModeSelectPanel.prefab`、`LoginPanel.prefab`、`MatchmakingPanel.prefab`、`LobbyPanel.prefab` 和 `SettlementPanel.prefab` 是独立嵌套 prefab。
- 稳定按钮、输入框、列表位置和文本节点在编辑器中可见，运行时 presenter 不修补这些布局。

运行时创建只用于数据驱动或数量不固定的对象：

- 玩家、食物、临时特效和局内排名行。
- 网络会话与模拟所需的纯逻辑对象。
- 由状态快照决定数量和生命周期的轻量视图。

UI 样式通过 `DotArenaUiFactory`、`DotArenaUiStyleCatalog` 和程序生成的圆角/圆形 Sprite 统一。当前客户端不提交 PNG、JPG、SVG 等图片资产；如果未来改变该约束，必须同时更新 EditMode 资源守卫和 [`../ART_DIRECTION.md`](../ART_DIRECTION.md)。

## 帧同步主线程流程

1. 实时 attach 回复或 callback 提供 `FrameSyncStart` 和可选重放帧。
2. `DotArenaCallbackInbox` 在线程安全队列中收集启动消息和帧。
3. Unity 主线程创建 `FrameSyncSimulation`，按连续帧推进。
4. 每个模拟 step 产生本地 `WorldState`、死亡事件和可选 `MatchEnd`。
5. 表现层只消费本地结果，不接收服务端世界快照。
6. 回合结束后客户端提交 `FrameSyncMatchResult`，等待上报完成再关闭实时连接。

断线重连只依赖同一启动参数和有界帧历史。缺少开局帧或历史不连续时，客户端不能猜测世界状态，应返回明确的恢复或失败路径。

## 表现原则

- 玩家显示大小跟随 `Radius`。
- HUD 强调名字、质量、排名、倒计时和存活状态。
- 战斗内排名只展示整型质量，不使用独立“分数”口径。
- 排名面板保持低遮挡半透明，不能覆盖核心玩法判断区域。
- 玩家界面不显示 endpoint、tick、内部枚举、同步对象数、快捷键提示或调试入口。
- 食物小且数量多；玩家、食物和边界通过颜色、轮廓和层级保持可读。
- 本地玩家默认颜色在每局开始时从蓝、橙、绿中随机选择。该随机值只属于表现层，不进入帧同步状态。
- 当前玩家文案与项目字体使用英文；字体回退使用项目内的 Liberation Sans TMP 资源。
- 任务、商店、记录和复杂装备养成不属于当前玩家 UI。
