# Godot Client UI: Scene-Theme-Script Separation

## Principle

Godot client UI is generated as three layers, matching Godot's intended
workflow and the Unity client's existing UXML/USS/script separation:

| Layer | File | Responsibility |
|-------|------|---------------|
| Scene | `.tscn` | Static node hierarchy, anchors, names, `theme_type_variation` references |
| Theme | `.tres` | All visual styling: colors, StyleBoxFlat, type defaults, type variations |
| Script | `.cs` | Runtime interaction: `GetNode<>("%Name")`, signal connections, business logic |

## Why

Before 2026-06, Godot UI was generated entirely in C# `BuildUi()` methods (~270
lines of `new Control()` / `AddChild()` / `AddThemeOverride()` per scene). This
prevented visual preview in the Godot editor, mixed layout and logic concerns,
and was inconsistent with the Unity path (which already used `.uxml` + `.uss` +
`.cs`).

## Generated files

```
Client/
├── Login.tscn              Full node tree with unique_name_in_owner for interactive nodes
├── Chat.tscn               Full node tree (Header/MessageLog/Footer layout)
├── Theme/
│   └── LakonaTheme.tres    Theme resource: 7 StyleBoxFlat sub-resources, 13 type variations
├── Scripts/
│   ├── Login/
│   │   ├── LoginClient.cs  (unchanged)
│   │   └── LoginScene.cs   GetNode references, no BuildUi
│   └── Chat/
│       ├── ChatClient.cs   (unchanged)
│       ├── ChatSession.cs  (unchanged)
│       └── ChatScene.cs    GetNode references, no BuildUi
```

## LakonaTheme.tres type variations

Each variation declares its base type (`*/type`), enabling Godot to resolve
inheritance chains:

| Variation | Base Type | Purpose |
|-----------|-----------|---------|
| TitleLabel | Label | 22px Accent, login title |
| HeaderLabel | Label | 18px Accent, chat header |
| NameLabel | Label | 14px AccentDim, field labels |
| StatusLabel | Label | 14px Error, status messages |
| OnlineCount | Label | 14px Warning, online count |
| PanelVBox | VBoxContainer | separation=12, login panel |
| ChatVBox | VBoxContainer | separation=0, chat layout |
| HeaderRow | HBoxContainer | separation=12, header row |
| SendRow | HBoxContainer | separation=8, send row |
| PageMargin | MarginContainer | 16px page margins |
| LoginPanel | PanelContainer | 4-side accent border, 32/24px content |
| ChatHeader | PanelContainer | accent bottom-border only |
| ChatFooter | PanelContainer | accent top-border only |

## Code generation

Templates live in `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`:

- `RenderGodotTheme()` — generates `.tres` content
- `RenderGodotLoginTscn()` — generates `Login.tscn`
- `RenderGodotChatTscn()` — generates `Chat.tscn`
- `RenderGodotLoginScene(options)` — generates `LoginScene.cs` (no `BuildUi`)
- `RenderGodotChatScene()` — generates `ChatScene.cs` (no `BuildUi`)

All use C# raw string literals (`"""..."""`). The `.tscn` and `.tres` files
use `WriteAsync` (always overwrite on scaffold); `.cs` scripts use
`WriteIfMissingAsync` (preserve user edits).

## Design decisions

1. **Node references use `%` unique names.** `GetNode<LineEdit>("%NameField")`
   is shorter than absolute paths and survives tree restructuring.
2. **`LakonaTheme.cs` removed.** Color/size constants are embedded in the
   `.tres` generator, eliminating duplicate maintenance.
3. **`_serverHost`/`_serverPort` stay as `[Export]` in `.cs`.** Runtime
   configuration does not belong in `.tscn`.
4. **Button disabled styling is automatic.** The `.tres` defines
   `Button/styles/disabled`; Godot switches styles when `Disabled = true`.
   `SetBusy()` no longer creates `StyleBoxFlat` at runtime.
5. **Base type declarations are required.** Every type variation has a
   `*/type` entry (e.g., `TitleLabel/type = "Label"`) so Godot can resolve
   style inheritance from the base control type.
