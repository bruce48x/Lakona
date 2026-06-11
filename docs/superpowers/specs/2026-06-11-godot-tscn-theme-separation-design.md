# Godot UI 场景-主题-脚本分离设计

## 目标

将 Godot 客户端 UI 生成从「全部用 C# 代码构建」改为「.tscn 定义静态布局 + .tres 定义主题样式 + .cs 只留交互逻辑」，与 Godot 引擎的设计理念对齐，同时与当前 Unity 端已采用的 UXML/USS/脚本分离模式保持一致。

## 当前状态

- **.tscn**：只有 6 行空壳（Control + script 引用），无任何子节点
- **LakonaTheme.cs**：约 45 行静态常量（颜色/字号/间距），通过代码引用
- **LoginScene.cs**：约 200 行，其中 `BuildUi()` 占 110 行，交互逻辑 90 行
- **ChatScene.cs**：约 320 行，其中 `BuildUi()` 占 160 行，交互逻辑 160 行
- 所有 `StyleBoxFlat`、`AddThemeOverride` 均在 `BuildUi()` 中动态创建

## 目标架构

### 文件拆分

```
Client/
├── Login.tscn              (~50 行，完整节点树 + Theme 引用)
├── Chat.tscn               (~70 行，完整节点树 + Theme 引用)
├── Theme/
│   └── LakonaTheme.tres    (~120 行，Theme 资源：类型默认样式 + 变体 + StyleBox)
├── Scripts/
│   ├── Login/
│   │   ├── LoginClient.cs  (不变)
│   │   └── LoginScene.cs   (~120 行，移除 BuildUi，只有 GetNode + 信号 + 交互)
│   └── Chat/
│       ├── ChatClient.cs   (不变)
│       ├── ChatSession.cs  (不变)
│       └── ChatScene.cs    (~220 行，移除 BuildUi，只有 GetNode + 信号 + 交互)
```

### 职责分离

| 层 | 文件 | 负责内容 |
|----|------|---------|
| 场景层 | `.tscn` | 所有控件的静态结构：节点层级、名称、类型、文本、锚点、尺寸、`unique_name_in_owner`、`theme_type_variation` |
| 主题层 | `.tres` | 所有视觉样式：颜色、字体大小、StyleBoxFlat（normal/disabled/hover）、间距常量 |
| 脚本层 | `.cs` | 运行时行为：`GetNode<>("%Name")` 获取引用、信号连接、状态切换、网络逻辑 |

### 不再生成的

- `LakonaTheme.cs` — 颜色/字体/间距常量直接嵌入 `.tres` 生成代码，不再需要单独的 C# 常量类

## Login.tscn 节点树

```
Control (LoginScene)                         theme=LakonaTheme.tres, script=LoginScene.cs
├── ColorRect (Background)                    color=BgBase, anchors=FullRect
├── ColorRect (Scanlines)                     color=black@8%, mouse_filter=Ignore
└── CenterContainer (Center)                  anchors=FullRect
    └── PanelContainer (LoginPanel)           theme_type_variation=LoginPanel, min_size=(360,0)
        └── VBoxContainer (PanelContent)      theme_type_variation=PanelVBox
            ├── Label (Title)                 theme_type_variation=TitleLabel, text="LAKONA"
            ├── Label (NameLabel)             text="NAME:", unique_name_in_owner=false
            ├── LineEdit (NameField)           max_length=20, unique_name_in_owner=true ← %NameField
            ├── Button (ConnectButton)         text="CONNECT", unique_name_in_owner=true ← %ConnectButton
            └── Label (StatusLabel)            text="", theme_type_variation=StatusLabel, unique_name_in_owner=true ← %StatusLabel
```

## Chat.tscn 节点树

```
Control (ChatScene)                           theme=LakonaTheme.tres, script=ChatScene.cs
├── ColorRect (Background)
├── ColorRect (Scanlines)
└── MarginContainer (Layout)                  theme_type_variation=PageMargin
    └── VBoxContainer (ChatLayout)            theme_type_variation=ChatVBox
        ├── PanelContainer (Header)           theme_type_variation=ChatHeader
        │   └── HBoxContainer (HeaderRow)     theme_type_variation=HeaderRow
        │       ├── Label (Title)             theme_type_variation=HeaderLabel, text="CHAT ROOM", expand_h
        │       └── Label (OnlineCount)       theme_type_variation=OnlineCount, text="ONLINE: --"
        ├── RichTextLabel (MessageLog)         bbcode=false, scroll_following=true, expand_v
        └── PanelContainer (Footer)           theme_type_variation=ChatFooter
            └── HBoxContainer (SendRow)       theme_type_variation=SendRow
                ├── Label (MessageLabel)      text="MESSAGE:"
                ├── LineEdit (MessageField)    max_length=500, expand_h
                └── Button (SendButton)        text="SEND"
```

标记 `unique_name_in_owner=true` 的节点（需在 .cs 中引用）：
- LoginScene：`%NameField`、`%ConnectButton`、`%StatusLabel`
- ChatScene：`%MessageField`、`%SendButton`、`%MessageLog`、`%OnlineCount`

## LakonaTheme.tres 内容

### 颜色常量（从原 LakonaTheme.cs 迁移）

```
BgBase     = Color(0.039, 0.059, 0.039)   #0A0F0A
BgPanel    = Color(0.059, 0.102, 0.059)   #0F1A0F
BgInput    = Color(0.020, 0.039, 0.039)   #050A0A
Accent     = Color(0, 1, 0.4)             #00FF66
AccentDim  = Color(0, 0.667, 0.267)       #00AA44
TextPrimary= Color(0, 1, 0.4)             #00FF66
TextBody   = Color(0.533, 0.8, 0.6)       #88CC99
TextDim    = Color(0.267, 0.533, 0.333)   #448855
TextSystem = Color(0.4, 0.667, 0.467)     #66AA77
Warning    = Color(1, 1, 0)               #FFFF00
Error      = Color(1, 0.267, 0.267)       #FF4444
```

### 类型默认样式

| 类型 | 属性 | 值 |
|------|------|-----|
| default | `font_size` | 14 |
| Label | `font_color` | TextBody |
| Label | `font_size` | 14 |
| LineEdit | `font_color` | Accent |
| LineEdit | `font_placeholder_color` | TextDim |
| LineEdit | `styles/normal` | StyleBoxInput |
| Button | `font_color` | BgBase |
| Button | `font_disabled_color` | AccentDim |
| Button | `styles/normal` | StyleBoxButtonNormal |
| Button | `styles/disabled` | StyleBoxButtonDisabled |
| RichTextLabel | `default_color` | TextBody |
| RichTextLabel | `normal_font_size` | 14 |

### 类型变体 (theme_type_variation)

| 变体名 | 基类型 | 属性 |
|--------|--------|------|
| TitleLabel | Label | font_size=22, font_color=Accent |
| HeaderLabel | Label | font_size=18, font_color=Accent |
| SmallLabel | Label | font_size=12, font_color=TextSystem |
| NameLabel | Label | font_size=14, font_color=AccentDim |
| StatusLabel | Label | font_size=14, font_color=Error |
| OnlineCount | Label | font_size=14, font_color=Warning |
| PanelVBox | VBoxContainer | separation=12 |
| ChatVBox | VBoxContainer | separation=0 |
| HeaderRow | HBoxContainer | separation=12 |
| SendRow | HBoxContainer | separation=8 |
| PageMargin | MarginContainer | margin=16 (四边) |
| LoginPanel | PanelContainer | styles/panel=StyleBoxLoginPanel |
| ChatHeader | PanelContainer | styles/panel=StyleBoxChatHeader |
| ChatFooter | PanelContainer | styles/panel=StyleBoxChatFooter |

### StyleBox 子资源

| ID | 属性 | 用途 |
|----|------|------|
| StyleBoxInput | bg=InputBg, border=2px AccentDim | LineEdit 输入框 |
| StyleBoxButtonNormal | bg=Accent, content_margin=(8,8,4,4) | Button 正常态 |
| StyleBoxButtonDisabled | bg=PanelBg, border=2px AccentDim, content_margin=(8,8,4,4) | Button 禁用态 |
| StyleBoxLoginPanel | bg=PanelBg, border=2px Accent, content_margin=(24,24,32,32) | 登录面板 |
| StyleBoxChatHeader | bg=PanelBg, border_bottom=2px Accent, content_margin=(0,0,8,8) | 聊天头部 |
| StyleBoxChatFooter | bg=PanelBg, border_top=2px Accent, content_margin=(0,0,8,8) | 聊天底部 |

## LoginScene.cs 变化

### 删除
- `BuildUi()` 方法（约 110 行）
- 所有 `new StyleBoxFlat` 创建
- 所有 `AddThemeStyleboxOverride` / `AddThemeColorOverride` / `AddThemeFontSizeOverride` / `AddThemeConstantOverride` 调用
- 所有 `new Control()` + `AddChild()` 构建

### 替换
- `_Ready()` 开头添加 `GetNode<>("%...")` 获取引用
- 信号连接代码保留

### 保留
- `OnConnectPressed()` 及网络逻辑
- `SetStatus()` / `SetBusy()` 及状态管理
- `NormalizePath()` / `ConfigureTransportSecurity()` / `CreateRpcClientOptions()`
- `[Export]` 字段（_serverHost / _serverPort / _serverPath）

### _Ready() 示例

```csharp
public override void _Ready()
{
    _nameField = GetNode<LineEdit>("%NameField");
    _connectButton = GetNode<Button>("%ConnectButton");
    _statusLabel = GetNode<Label>("%StatusLabel");

    _nameField.TextSubmitted += _ => OnConnectPressed();
    _connectButton.Pressed += OnConnectPressed;

    SetBusy(false);
}
```

## ChatScene.cs 变化

与 LoginScene 同理：删除 `BuildUi()`（约 160 行），替换为 `GetNode<>()` 引用获取。

## 代码生成层变更

### ChatClientTemplates.cs

| 方法 | 变化 |
|------|------|
| `RenderGodotLoginTscn()` | 重写：从 6 行空壳变为 ~50 行完整节点树 |
| `RenderGodotChatTscn()` | 重写：从 6 行空壳变为 ~70 行完整节点树 |
| `RenderGodotTheme()` | 新增：~120 行 .tres 字符串 |
| `RenderGodotLoginScene(options)` | 修改：移除 `BuildUi()`，`_Ready()` 改为 GetNode + 信号连接 |
| `RenderGodotChatScene()` | 修改：同上 |
| `RenderGodotThemeClass()` | **删除** |

### ProjectScaffolder.cs

- `WriteClientChatFilesAsync()` Godot 分支：
  - 删除 `WriteIfMissingAsync(..., "LakonaTheme.cs", ...)`
  - 新增 `WriteAsync(..., "LakonaTheme.tres", RenderGodotTheme())`
  - `.tscn` 保持不变：`WriteAsync`（始终覆盖）
  - `.cs` 保持不变：`WriteIfMissingAsync`（用户可修改）

### 文件写入语义

| 文件 | 写入策略 | 原因 |
|------|---------|------|
| `Login.tscn` | `WriteAsync`（始终覆盖） | 静态结构应由 Tool 管理 |
| `Chat.tscn` | `WriteAsync`（始终覆盖） | 同上 |
| `LakonaTheme.tres` | `WriteAsync`（始终覆盖） | 主题应由 Tool 管理 |
| `LoginScene.cs` | `WriteIfMissingAsync` | 用户可能修改交互逻辑 |
| `ChatScene.cs` | `WriteIfMissingAsync` | 同上 |

## 设计决策记录

1. **不使用 .gd 脚本** — 保持 Godot C#，与现有架构一致
2. **节点引用用 `%` 唯一名称** — 比绝对路径短，节点移动时不影响代码
3. **LakonaTheme.cs 删除** — 颜色/尺寸常量直接嵌入 .tres 生成方法
4. **`_serverHost` 等保持 `[Export]` 在 .cs** — 运行时配置，不进 .tscn
5. **兼容性不考虑** — 项目处于早期阶段，不做旧项目迁移
6. **Button disabled 状态自动切换** — Theme 定义 `styles/disabled`，Godot 自动换样式，`SetBusy()` 中不再手动创建 StyleBox
