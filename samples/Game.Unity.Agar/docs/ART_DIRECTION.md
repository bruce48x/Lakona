# 美术与 UI 方向

这份文档定义 Game.Unity.Agar 样例的美术、UI、资产生成和 Unity 接入标准。目标不是把样例做成重资产项目，而是让它具备完整游戏应有的视觉一致性、可读性和交互反馈。

## 目标定位

Game.Unity.Agar 的视觉定位是：

> 轻量、清晰、带果冻质感的多人吞噬竞技游戏。

项目首先服务于 Lakona.Game / Lakona.Rpc 框架演示，因此美术方案必须轻量、易维护、易替换。所有资产和 UI 都应优先服务玩法判断：位置、体型、速度、可吞噬关系、危险边界、质量和对局状态。

## 风格关键词

- 深色竞技场。
- 发光细胞。
- 半透明果冻质感。
- 清爽街机 UI。
- 青色与柔金强调。
- 友好但有竞技感。
- 高可读性。
- 少量科技感，不做厚重科幻。

稳定英文风格描述：

```text
clean neon cellular arena style, dark sci-fi arcade palette, soft glow, jelly-like translucent material, cyan and soft gold accents, high readability, friendly competitive mood
```

避免方向：

- 写实细胞、医学显微镜、病毒恐怖。
- 厚重机甲、硬科幻、赛博城市。
- 大面积角色插画、营销首页、复杂 3D 场景。
- 血腥、尖刺、枪械或当前玩法没有定义的旧强化概念。
- 图片内嵌文字、水印、签名。

## 视觉原则

### 可读性优先

- 玩家、敌人、食物、边界和 UI 信息必须在移动中快速识别。
- 小尺寸物体不能依赖复杂细节表达身份。
- 背景、粒子和 UI 不能遮挡玩家主体、名称、质量、HUD 或排行榜。
- 特效持续时间应短，不能长期覆盖核心视野。
- 敌我关系通过颜色、描边、名称和 HUD 信息共同表达。

### 统一性优先

- 玩家皮肤可以多色，但轮廓语言必须统一。
- 食物、拾取物和特效共享发光、圆润、轻量的视觉语法。
- UI 面板、按钮、图标和文本共享边框、透明度、字号和强调色。
- 背景只衬托主体，不能成为视觉中心。

### 样例友好

- 优先使用 2D PNG、Sprite、材质参数、ParticleSystem 和少量 Shader。
- 避免大型资源包、复杂骨骼动画和强依赖第三方美术插件。
- 每类资产至少保留一个可替换的基准版本。
- 脚本负责状态和布局，资产负责质感和识别。

## 调色板

当前代码已经采用深色背景、明亮玩家色、金色强调和青色拾取物。后续只扩展，不重做成完全不同色系。

| 用途 | 推荐方向 |
|------|----------|
| 背景 | 深蓝黑、低饱和蓝灰 |
| 地面/网格 | 暗蓝灰、低透明细胞纹理 |
| 玩家 | 高饱和果冻色：青、绿、红、黄、蓝、紫 |
| 本地玩家 | 更明亮的青蓝或玩家选中皮肤色 |
| 食物/质量点 | 青色、薄荷色、淡蓝色 |
| 危险提示 | 低透明红色 |
| 边界强调 | 暖金色或琥珀色 |
| UI 背景 | 深蓝黑半透明 |
| UI 主行动 | 青色 |
| UI 奖励/标题 | 柔金色 |
| UI 成功 | 绿色 |
| UI 禁用 | 蓝灰色 |

推荐 UI 色值：

| 用途 | 色值 |
|------|------|
| 页面底色 | `#05080D` 到 `#0A1018` |
| 面板底色 | `#111A24`，90% 到 96% 不透明 |
| 输入框底色 | `#24303F` |
| 主文字 | `#F5FAFF` |
| 次级文字 | `#D6E6F5` |
| 弱文字 | `#BACCDC` |
| 主行动青色 | `#38E6FF` |
| 奖励柔金 | `#FFEAB3` |
| 危险红 | `#FF4A5C` |
| 成功绿 | `#58F0A2` |

限制：

- 不做单一紫色、单一蓝色或大面积米色调。
- 金色只做强调，不做大面积底色。
- 红色只用于危险、失败、取消。

## 游戏资产标准

### 玩家球

玩家球是最重要的资产。

- 圆形或近圆形轮廓。
- 半透明果冻质感。
- 柔和高光、内发光或轻微渐变。
- 透明背景边缘干净，无白边黑边。
- 缩小到实际游戏尺寸后仍可辨。
- 不出现复杂噪点、文字、水印。

建议首批皮肤：

- `Skin_Jelly_Cyan`
- `Skin_Jelly_Crimson`
- `Skin_Jelly_Sunburst`
- `Skin_Jelly_Mint`
- `Skin_Jelly_Glacier`
- `Skin_Jelly_Violet`

### 食物与拾取物

当前协议使用 `PickupType.MassPoint` 表示质量拾取物；玩家界面只表达质量。

- 小型发光颗粒或圆润碎片。
- 不使用复杂图案。
- 不使用与玩家完全相同的尺寸和质感。
- 多个食物聚集时不能形成刺眼噪声。

### 场地与边界

- 深色地面。
- 低透明网格或细胞纹理。
- 边界明确但不刺眼。
- 安全区、危险区和场地外区域可通过透明覆盖表达。
- 背景不能影响玩家球颜色判断。

### 特效

首批需要：

- 吸收食物：粒子汇入、亮点闪烁或短促收缩。
- 吞噬玩家：环形波纹、柔和爆散或溶解。
- 复活/生成：透明到实体，带一圈扩散波纹。
- 成长：轻微弹性缩放和高光闪动。
- 匹配/等待：轻量循环动效。

标准：

- 单次反馈 0.15 到 0.45 秒。
- 不遮挡玩家当前位置判断。
- 不使用血腥或攻击性过强表现。
- 粒子数量控制在编辑器和移动端都可接受。

## UI 标准

UI 应像完整游戏界面，而不是调试面板。当前阶段需要支持入口、登录、匹配、HUD、结算、资料/大厅、排行榜和设置。任务、商店和记录仍属于未上线方向，不作为当前玩家 UI 验收范围。

联机大厅和战斗内 HUD 都是玩家界面，不保留 DEBUG 信息。连接端点、tick、内部状态枚举、同步对象数、快捷键提示、开发诊断文本和调试面板都应从 UI 中删除；将来确实需要排查问题时，通过 Unity Console、服务端日志或客户端日志打印，不在玩家界面保留入口或占位。

### 组件语言

- 面板：深色半透明，6 到 8 像素圆角，薄描边，轻微内外发光。
- 按钮：圆角矩形，状态变化清楚。
- 标签：选中态明显，可用青色下划线、柔光边框或底色提升。
- 输入框：深蓝灰底，聚焦态用青色细边框，错误态用红色细边框。
- 图标：线性或半实心，圆润，透明背景，32x32 下可识别，不含文字。
- 进度条：蓝灰轨道；青色表示进度，绿色表示完成，金色表示奖励。

按钮层级：

| 类型 | 用途 | 视觉 |
|------|------|------|
| Primary | 开始、匹配、确认 | 青色边框或青色填充 |
| Secondary | 返回、查看、普通导航 | 深色底、蓝灰边框 |
| Danger | 取消、退出、失败重试 | 暗红边框或红色重点 |
| Tab | 大厅分页 | 选中态明显，普通态克制 |
| IconButton | 设置、关闭、小操作 | 正方形，图标居中 |

按钮和面板图片不包含文字。所有文字由 TextMesh Pro 渲染。

### UI Sprite 视觉边界

UI 面板、按钮、输入框、列表行、标签页和 HUD 背板的 PNG 透明边缘必须尽可能贴合肉眼可见的真实边框。透明留白过大会让 Unity `RectTransform` 的逻辑尺寸大于视觉边界，导致文本、按钮和边框的相对位置看起来不规范。

生产要求：

- 视觉外边框应尽量贴近图片 alpha 包围盒，透明外边距只保留抗锯齿、柔光和少量安全像素。
- 面板和按钮不允许为了“居中好看”在四周保留大块透明 padding；布局留白应由 Unity `RectTransform`、LayoutGroup 或内容安全区控制。
- 常规 UI sprite 的透明外边距建议不超过图片短边的 3%，硬上限为 5%；超过 5% 必须裁切或重新导出。
- 发光边缘可以保留，但 glow 仍应被视作视觉边界的一部分；不要让 glow 之外再有明显空白。
- 九宫格素材的可切片边框应与可见描边/圆角/发光位置一致，不能把大片透明区计入 border。
- 导入或接入前应检查 alpha 包围盒；若 `RectTransform` 尺寸和肉眼看到的边框明显不一致，优先修正图片源文件，而不是用脚本坐标补偿。

以当前 `UI_Panel_Dark_01.png` 为反例：512x512 图片的可见 alpha 包围盒约为 450x441，四边透明留白约 31 到 36 像素，透明边缘偏大。后续生成或重做 UI 面板时应裁到更贴近视觉边框的尺寸。

### 字体与文案

默认使用 `DotArenaCJK SDF`。设计基准为 1200x600：

| 类型 | 字号 | 样式 |
|------|------|------|
| 大标题 | 22-26 | Bold |
| 面板标题 | 18-22 | Bold |
| 主要按钮 | 13-15 | Bold |
| 正文/详情 | 13-14 | Regular |
| 小标签 | 11-12 | Regular |
| HUD 核心数字 | 16-22 | Bold |
| HUD 辅助信息 | 11-13 | Regular |

文案规则：

- 按钮用动作词，例如“开始匹配”“再次匹配”“返回大厅”。
- 状态短而明确，例如“匹配中 00:12”“已登录”“连接失败”。
- 不放解释美术风格、快捷键说明或功能介绍型装饰文案。
- 文本必须支持省略号或换行，不能溢出按钮和面板。

### 核心界面

| 界面 | 结构重点 | 素材重点 |
|------|----------|----------|
| 入口/模式选择 | 居中面板，模式按钮，登录状态 | 面板、Primary 按钮、模式图标 |
| 登录/联机 | 账号密码输入、登录、游客登录、返回 | 输入框、Secondary/Danger 按钮、账号图标 |
| 大厅 | 顶部状态、标签导航、快捷操作、详情区、底部状态 | 大厅面板、Tab、列表行、功能图标 |
| 匹配中 | 标题、等待指示、队列/房间/耗时、取消 | 匹配面板、Loading、Cancel 图标 |
| HUD | 左上状态，顶部倒计时，右上排行榜，短事件提示 | 紧凑背板、排行榜行、质量/时间/网络图标 |
| 结算 | 结果、质量、奖励、再来一局、返回大厅 | 结算面板、胜负光效、奖励行 |
| 资料/大厅 | 玩家摘要、联机就绪、开始匹配、退出登录 | 大厅面板、Tab、列表行、本地玩家行 |
| 排行榜 | 列表行、名次、玩家、胜利积分、本地高亮 | 表头、交替行、本地玩家行 |
| 设置 | 音量、语言、全屏 | 滑杆、开关、下拉、设置图标 |

战斗内 HUD 只允许展示玩家决策需要的信息：名字、质量、排名、倒计时、存活/复活状态和短事件提示。HUD 不显示 DEBUG 信息，不显示 tick、endpoint、连接细节、内部状态枚举、同步对象数、快捷键提示或开发诊断文本。

战斗内实时排名面板必须使用低遮挡半透明背景，不能使用接近不透明的大块深色背景遮住游戏画面。建议面板底色 alpha 控制在 35% 到 55%，行高紧凑，边框和分隔线弱化；本地玩家行可以轻微高亮，但仍要保持半透明。验收时应能透过排名框看清玩家球、食物、边界和移动方向，排名框不得遮挡核心玩法判断。

当前实时排名面板使用专用素材 `UI_HUD_Rank_Panel_Translucent_01.png`，中心区域比普通 UI 面板更透明，边缘只保留弱青色轮廓。素材中心 alpha 约 9%，边缘 alpha 约 33%，并在 UI 侧再乘低透明度；排名行背景也必须保持低 alpha，不能形成实心横条。该素材只用于战斗内排名框，不应替代入口、匹配、大厅或结算的通用面板。

### UI 状态与交互

网络或异步按钮必须有明确状态：

- 点击后立即禁用相关按钮。
- 显示 `Submitting`、`Matching`、`Canceling` 等状态。
- 成功后切换界面。
- 失败后恢复可操作并显示错误。
- 不允许重复点击发起重复请求。

典型状态：

| 流程 | 状态 |
|------|------|
| 登录 | Idle / Submitting / Success / Failed |
| 匹配 | Idle / Queued / Matched / CancelPending / Failed |
| 设置保存 | Idle / Dirty / Saving / Saved / Failed |

### UI 布局与动效

- 当前窗口基准：1200x600。
- 需要检查 960x540、1280x720、1920x1080。
- 中央弹窗最大宽度建议 420 到 520 Unity UI 单位。
- 大厅左右边距至少 36。
- HUD 距边缘至少 12 到 16，不能遮挡玩法中心。
- 可点击目标不低于 30x30。
- 表格列宽固定，数字右对齐。

动效建议：

| 动效 | 时长 |
|------|------|
| 面板出现 | 0.12-0.2 秒 |
| 面板消失 | 0.08-0.15 秒 |
| 按钮按下 | 0.06-0.1 秒 |
| 标签切换 | 0.1-0.16 秒 |
| 奖励出现 | 0.18-0.3 秒 |
| 错误提示 | 0.18 秒 |
| 匹配等待 | 1.0-1.4 秒循环 |

实现优先级：`CanvasGroup` alpha、`RectTransform` 轻微缩放、`Image.color`、简单序列帧。除非界面稳定并需要复用，不优先引入复杂 Animator 状态机。

## 资产目录

推荐目录：

```txt
Client/Assets/Art
 ├─ Backgrounds
 ├─ FX
 ├─ Icons
 │  └─ _Source
 ├─ Materials
 ├─ Pickups
 │  └─ _Source
 ├─ Sprites
 │  ├─ Pickups
 │  └─ Skins
 └─ UI
    ├─ _Source
    ├─ Buttons
    ├─ HUD
    ├─ Inputs
    ├─ Lists
    ├─ Panels
    ├─ Progress
    └─ Tabs
```

如果现有脚本暂时只引用 `Art/UI` 根目录，可以先放根目录，稳定后再分目录迁移。不要把正式资产放到 `Textures` 根目录，除非代码或导入历史已依赖。

## 命名规则

```txt
Skin_Jelly_Cyan.png
Pickup_Mass_Teal_01.png
BG_Arena_Grid_Dark_01.png
FX_Absorb_Ring_01.png
UI_Panel_Lobby_Dark_01.png
UI_Button_Primary_Normal.png
UI_Button_Primary_Pressed.png
UI_Input_Error_01.png
UI_HUD_Rank_Row_01.png
Icon_Leaderboard_01.png
Icon_Settings_01.png
```

规则：

- 使用 PascalCase 或下划线分段，不使用空格。
- UI 用 `UI_` 前缀，图标用 `Icon_` 前缀。
- 状态写在末尾：`Normal`、`Hover`、`Pressed`、`Disabled`、`Selected`、`Error`。
- 后缀数字用于同类型变体。
- 不使用 `final`、`new`、`copy`、`test`。
- `_Source` 只用于源图，正式 Unity Sprite 不带 `_Source`。
- 占位资产可保留 `Placeholder`，正式资产不要使用。

## 规格与导入

| 类型 | 推荐尺寸 | 背景 | 说明 |
|------|----------|------|------|
| 玩家皮肤 | 512x512 | 透明 | 可缩放到不同体型 |
| 食物/拾取物 | 128x128 或 256x256 | 透明 | 小尺寸仍需清晰 |
| 图标 | 256x256 | 透明 | 24-48 像素显示仍可识别 |
| UI 面板 | 512x512 或 1024x1024 | 透明 | 适合九宫格 |
| 按钮 | 512x192 或 768x256 | 透明 | 至少 normal/pressed/disabled |
| 背景/地面 | 2048x2048 | 不透明或透明 | 可平铺优先 |
| 特效帧 | 256x256 或 512x512 | 透明 | 单张或序列帧 |

UI PNG 导入建议：

- `Texture Type`: Sprite (2D and UI)
- `Sprite Mode`: Single，图集按需 Multiple
- `Alpha Is Transparency`: 开启
- `Pixels Per Unit`: 100
- `Mesh Type`: Full Rect
- `Filter Mode`: Bilinear
- `Compression`: None 或 High Quality

九宫格面板和按钮：

- 在 Sprite Editor 设置 Border。
- 面板边框建议 12 到 24 像素。
- 按钮边框建议 8 到 16 像素。
- Unity `Image.type` 使用 `Sliced`。
- 设置 Border 前先裁掉多余透明外边距，确保 sprite 逻辑边界和视觉边框一致。

## Unity 接入

当前客户端已有 UGUI、TextMesh Pro、`Client/Assets/Art/UI`、`Client/Assets/Art/Icons`、`DotArenaCJK SDF` 和 `DotArenaTuning.cs` 色彩常量。后续正式 UI 应优先替换 sprite 和 prefab，不把复杂视觉硬编码进脚本。

接入原则：

- 面板使用 `Image` + sliced sprite。
- 按钮使用 `Button` + `Image`，状态优先用 `SpriteState`，颜色只做微调。
- 文本使用 `TextMeshProUGUI`。
- 输入框使用 `TMP_InputField`，背景 sprite 按状态切换。
- 图标使用独立 `Image`，不要并入按钮底图。
- 多处复用组件做成 prefab 或统一创建方法。

建议顺序：

1. 建立 `DotArenaUiTheme` 或等价配置，集中引用 UI sprite、icon、颜色、字体。
2. 替换入口、匹配、结算三个弹窗的面板和按钮。
3. 替换大厅标签、列表行和功能图标。
4. 增加 HUD 排行榜和状态背板。
5. 把排行榜和设置从纯文本详情升级为列表组件。
6. 最后统一动效和音效反馈。

## 生成提示词

通用模板：

```text
2D game asset, clean neon cellular arena style, dark sci-fi arcade palette, soft glow, jelly-like translucent material, cyan and soft gold accents, high readability, centered object, transparent background, no text, no watermark, suitable for Unity sprite
```

玩家皮肤：

```text
2D game asset, circular jelly blob player skin, translucent [color] gel, glossy highlight, subtle inner glow, clean silhouette, transparent background, no text, no watermark, suitable for Unity sprite
```

拾取物：

```text
2D game asset, tiny glowing mass pickup, [color] energy pellet, simple rounded shape, soft bloom, readable at small size, transparent background, no text, no watermark, suitable for Unity sprite
```

背景：

```text
2D game arena background texture, dark blue black cellular grid, subtle microscopic pattern, low contrast, seamless or tileable feel, does not distract from bright player sprites, no text, no watermark
```

UI 面板：

```text
2D Unity UI panel sprite, dark blue black translucent glass rectangle, subtle cyan rim light, soft inner shadow, compact rounded corners, clean center area for text, nine-slice friendly border, transparent background tightly cropped to the visible outer glow and border with minimal transparent padding, no text, no watermark
```

UI 按钮：

```text
2D Unity UI button sprite, [primary/secondary/danger] action button, dark translucent base, subtle neon edge, compact rounded rectangle, [normal/hover/pressed/disabled] state, high readability, transparent background tightly cropped to the visible outer glow and border with minimal transparent padding, no text, no watermark
```

UI 图标：

```text
2D game UI icon, clean rounded line icon, neon cellular arcade style, [subject], cyan and soft gold accents, transparent background, no text, no watermark, readable at 32 pixels, Unity UI sprite
```

负面约束：

```text
no text, no letters, no numbers, no watermark, no logo, no realistic metal, no cyberpunk city, no busy pattern, no large illustration, no opaque background, no excessive bloom
```

## 生产流程

不要一次生成完整资产库。先做小型可运行基准包：

1. 玩家果冻皮肤 3 张。
2. 质量拾取物 2 张。
3. 竞技场背景或地面纹理 1 张。
4. UI 风格基准图 1 张，只作方向确认，不进游戏。
5. 最小 UI 素材包：面板、Primary/Secondary/Danger 按钮状态、输入框、8-10 个核心图标、HUD 背板。
6. 吸收特效和复活波纹原型各 1 个。

UI 接入顺序：

1. 基准图确认整体方向。
2. 生成最小 UI 素材包。
3. 先接入口/模式选择、匹配中、结算。
4. 在 Unity 游戏视图检查 1200x600 和 960x540。
5. 通过后再扩展大厅、排行榜和设置。

## 验收清单

生成或接入资产前检查：

- Unity 实际显示尺寸下主体仍可读。
- 透明边缘干净，无白边、黑边或脏边。
- UI 面板、按钮和输入框的透明外边距足够小，alpha 包围盒贴合肉眼可见边框；不得用大透明 padding 充当布局留白。
- 同一批资产风格一致。
- 文件命名和目录符合本文档。
- 没有文字、水印、签名或明显生成瑕疵。
- 没有引入当前玩法不存在的冲刺、护盾、击退强化等旧概念。
- 玩家球、食物、特效不降低玩法判断效率。
- UI 文字不溢出，按钮状态清楚，图标 32x32 可识别。
- 九宫格拉伸后边框不变形。
- HUD 不遮挡玩家名、质量和游戏中心视野。
- 异步按钮不会重复提交请求。
