# Godot UI Scene-Theme-Script Separation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Godot client UI from code-driven construction to .tscn (static layout) + .tres (theme) + .cs (interaction only), matching Godot best practices and Unity's existing UXML/USS/script separation.

**Architecture:** Three-template approach — `RenderGodotTheme()` generates a Godot Theme resource (.tres) with all colors, StyleBoxes, and type variations; `RenderGodotLoginTscn()` / `RenderGodotChatTscn()` generate complete node trees with `theme_type_variation` references; `RenderGodotLoginScene()` / `RenderGodotChatScene()` drop all `BuildUi()` code and use `GetNode<>("%Name")` for references.

**Tech Stack:** C# raw string templates (`$"""..."""`), Godot 4 .tscn text format, Godot Theme resource (.tres) format

---

### Task 1: Write `RenderGodotTheme()` and tests

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write the failing tests for the new Theme template**

Add these test methods to `ToolTemplateTests.cs`:

```csharp
[Fact]
public void RenderGodotTheme_ContainsExpectedStyleBoxes()
{
    var theme = ToolTemplates.RenderGodotTheme();

    Assert.Contains("[gd_resource type=\"Theme\"", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxInput\"]", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxButtonNormal\"]", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxButtonDisabled\"]", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxLoginPanel\"]", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxChatHeader\"]", theme, StringComparison.Ordinal);
    Assert.Contains("[sub_resource type=\"StyleBoxFlat\" id=\"StyleBoxChatFooter\"]", theme, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotTheme_ContainsDefaultTypeStyles()
{
    var theme = ToolTemplates.RenderGodotTheme();

    Assert.Contains("default_font_size = 14", theme, StringComparison.Ordinal);
    Assert.Contains("Button/colors/font_color", theme, StringComparison.Ordinal);
    Assert.Contains("Button/styles/normal = SubResource(\"StyleBoxButtonNormal\")", theme, StringComparison.Ordinal);
    Assert.Contains("Button/styles/disabled = SubResource(\"StyleBoxButtonDisabled\")", theme, StringComparison.Ordinal);
    Assert.Contains("LineEdit/styles/normal = SubResource(\"StyleBoxInput\")", theme, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotTheme_ContainsTypeVariations()
{
    var theme = ToolTemplates.RenderGodotTheme();

    Assert.Contains("TitleLabel/font_sizes/font_size = 22", theme, StringComparison.Ordinal);
    Assert.Contains("TitleLabel/colors/font_color = Color(0, 1, 0.4, 1)", theme, StringComparison.Ordinal);
    Assert.Contains("HeaderLabel/font_sizes/font_size = 18", theme, StringComparison.Ordinal);
    Assert.Contains("LoginPanel/styles/panel = SubResource(\"StyleBoxLoginPanel\")", theme, StringComparison.Ordinal);
    Assert.Contains("ChatHeader/styles/panel = SubResource(\"StyleBoxChatHeader\")", theme, StringComparison.Ordinal);
    Assert.Contains("ChatFooter/styles/panel = SubResource(\"StyleBoxChatFooter\")", theme, StringComparison.Ordinal);
    Assert.Contains("PanelVBox/constants/separation = 12", theme, StringComparison.Ordinal);
    Assert.Contains("ChatVBox/constants/separation = 0", theme, StringComparison.Ordinal);
    Assert.Contains("PageMargin/constants/margin_left = 16", theme, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotTheme_ContainsAllColorConstants()
{
    var theme = ToolTemplates.RenderGodotTheme();

    Assert.Contains("Color(0.039, 0.059, 0.039, 1)", theme, StringComparison.Ordinal);   // BgBase
    Assert.Contains("Color(0.059, 0.102, 0.059, 1)", theme, StringComparison.Ordinal);   // BgPanel
    Assert.Contains("Color(0.02, 0.039, 0.039, 1)", theme, StringComparison.Ordinal);    // BgInput
    Assert.Contains("Color(0, 1, 0.4, 1)", theme, StringComparison.Ordinal);             // Accent
    Assert.Contains("Color(0, 0.667, 0.267, 1)", theme, StringComparison.Ordinal);      // AccentDim
    Assert.Contains("Color(1, 0.267, 0.267, 1)", theme, StringComparison.Ordinal);      // Error
    Assert.Contains("Color(1, 1, 0, 1)", theme, StringComparison.Ordinal);              // Warning
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotTheme"`
Expected: FAIL with compilation error "'ToolTemplates' does not contain a definition for 'RenderGodotTheme'"

- [ ] **Step 3: Add `RenderGodotTheme()` method to `ChatClientTemplates.cs`**

Insert after `RenderGodotThemeClass()`:

```csharp
public static string RenderGodotTheme()
{
    return """
    [gd_resource type="Theme" load_steps=8 format=3]

    [sub_resource type="StyleBoxFlat" id="StyleBoxInput"]
    bg_color = Color(0.02, 0.039, 0.039, 1)
    border_width_left = 2
    border_width_right = 2
    border_width_top = 2
    border_width_bottom = 2
    border_color = Color(0, 0.667, 0.267, 1)

    [sub_resource type="StyleBoxFlat" id="StyleBoxButtonNormal"]
    bg_color = Color(0, 1, 0.4, 1)
    content_margin_left = 8.0
    content_margin_right = 8.0
    content_margin_top = 4.0
    content_margin_bottom = 4.0

    [sub_resource type="StyleBoxFlat" id="StyleBoxButtonDisabled"]
    bg_color = Color(0.059, 0.102, 0.059, 1)
    border_color = Color(0, 0.667, 0.267, 1)
    border_width_left = 2
    border_width_right = 2
    border_width_top = 2
    border_width_bottom = 2
    content_margin_left = 8.0
    content_margin_right = 8.0
    content_margin_top = 4.0
    content_margin_bottom = 4.0

    [sub_resource type="StyleBoxFlat" id="StyleBoxLoginPanel"]
    bg_color = Color(0.059, 0.102, 0.059, 1)
    border_color = Color(0, 1, 0.4, 1)
    border_width_left = 2
    border_width_right = 2
    border_width_top = 2
    border_width_bottom = 2
    content_margin_left = 24.0
    content_margin_right = 24.0
    content_margin_top = 32.0
    content_margin_bottom = 32.0

    [sub_resource type="StyleBoxFlat" id="StyleBoxChatHeader"]
    bg_color = Color(0.059, 0.102, 0.059, 1)
    border_color = Color(0, 1, 0.4, 1)
    border_width_bottom = 2
    content_margin_top = 8.0
    content_margin_bottom = 8.0

    [sub_resource type="StyleBoxFlat" id="StyleBoxChatFooter"]
    bg_color = Color(0.059, 0.102, 0.059, 1)
    border_color = Color(0, 1, 0.4, 1)
    border_width_top = 2
    content_margin_top = 8.0
    content_margin_bottom = 8.0

    [resource]
    default_font_size = 14

    Label/colors/font_color = Color(0.533, 0.8, 0.6, 1)
    Label/font_sizes/font_size = 14

    LineEdit/colors/font_color = Color(0, 1, 0.4, 1)
    LineEdit/colors/font_placeholder_color = Color(0.267, 0.533, 0.333, 1)
    LineEdit/styles/normal = SubResource("StyleBoxInput")

    Button/colors/font_color = Color(0.039, 0.059, 0.039, 1)
    Button/colors/font_disabled_color = Color(0, 0.667, 0.267, 1)
    Button/styles/normal = SubResource("StyleBoxButtonNormal")
    Button/styles/disabled = SubResource("StyleBoxButtonDisabled")

    RichTextLabel/colors/default_color = Color(0.533, 0.8, 0.6, 1)
    RichTextLabel/font_sizes/normal_font_size = 14

    TitleLabel/font_sizes/font_size = 22
    TitleLabel/colors/font_color = Color(0, 1, 0.4, 1)

    HeaderLabel/font_sizes/font_size = 18
    HeaderLabel/colors/font_color = Color(0, 1, 0.4, 1)

    NameLabel/colors/font_color = Color(0, 0.667, 0.267, 1)

    StatusLabel/colors/font_color = Color(1, 0.267, 0.267, 1)

    OnlineCount/colors/font_color = Color(1, 1, 0, 1)

    PanelVBox/constants/separation = 12
    ChatVBox/constants/separation = 0
    HeaderRow/constants/separation = 12
    SendRow/constants/separation = 8
    PageMargin/constants/margin_left = 16
    PageMargin/constants/margin_top = 16
    PageMargin/constants/margin_right = 16
    PageMargin/constants/margin_bottom = 16

    LoginPanel/styles/panel = SubResource("StyleBoxLoginPanel")
    ChatHeader/styles/panel = SubResource("StyleBoxChatHeader")
    ChatFooter/styles/panel = SubResource("StyleBoxChatFooter")
    """;
}
```

Note: `load_steps` counts the resource itself plus sub-resources. 1 (resource) + 7 (sub_resources) = 8.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotTheme"`
Expected: all 4 new tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "feat: add RenderGodotTheme() generating .tres with StyleBoxes and type variations"
```

---

### Task 2: Rewrite `RenderGodotLoginTscn()` with full node tree

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void RenderGodotLoginTscn_ContainsFullNodeTree()
{
    var tscn = ToolTemplates.RenderGodotLoginTscn();

    Assert.Contains("[gd_scene load_steps=3 format=3]", tscn, StringComparison.Ordinal);
    Assert.Contains("[ext_resource type=\"Script\" path=\"res://Scripts/Login/LoginScene.cs\" id=\"1\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[ext_resource type=\"Theme\" path=\"res://Theme/LakonaTheme.tres\" id=\"2\"]", tscn, StringComparison.Ordinal);

    // Root node
    Assert.Contains("[node name=\"LoginScene\" type=\"Control\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("theme = ExtResource(\"2\")", tscn, StringComparison.Ordinal);

    // Child nodes (existence)
    Assert.Contains("[node name=\"Background\" type=\"ColorRect\" parent=\".\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"Scanlines\" type=\"ColorRect\" parent=\".\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"Center\" type=\"CenterContainer\" parent=\".\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"LoginPanel\" type=\"PanelContainer\" parent=\"Center\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"PanelContent\" type=\"VBoxContainer\" parent=\"Center/LoginPanel\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"Title\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"NameLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"NameField\" type=\"LineEdit\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"ConnectButton\" type=\"Button\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"StatusLabel\" type=\"Label\" parent=\"Center/LoginPanel/PanelContent\"]", tscn, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotLoginTscn_InteractiveNodesHaveUniqueNames()
{
    var tscn = ToolTemplates.RenderGodotLoginTscn();

    Assert.Contains("unique_name_in_owner = true", tscn, StringComparison.Ordinal);
    // NameField, ConnectButton, StatusLabel should have unique_name_in_owner
    Assert.Contains("[node name=\"NameField\"", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"ConnectButton\"", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"StatusLabel\"", tscn, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotLoginTscn_NodesUseThemeTypeVariations()
{
    var tscn = ToolTemplates.RenderGodotLoginTscn();

    Assert.Contains("theme_type_variation = \"LoginPanel\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"PanelVBox\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"TitleLabel\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"NameLabel\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"StatusLabel\"", tscn, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotLoginTscn"`
Expected: FAIL (existing assertion changes needed, new assertions fail)

- [ ] **Step 3: Rewrite `RenderGodotLoginTscn()`**

Replace the existing 12-line method:

```csharp
public static string RenderGodotLoginTscn()
{
    return """
    [gd_scene load_steps=3 format=3]

    [ext_resource type="Script" path="res://Scripts/Login/LoginScene.cs" id="1"]
    [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2"]

    [node name="LoginScene" type="Control"]
    layout_mode = 3
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    theme = ExtResource("2")
    script = ExtResource("1")

    [node name="Background" type="ColorRect" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    color = Color(0.039, 0.059, 0.039, 1)

    [node name="Scanlines" type="ColorRect" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    color = Color(0, 0, 0, 0.08)
    mouse_filter = 2

    [node name="Center" type="CenterContainer" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2

    [node name="LoginPanel" type="PanelContainer" parent="Center"]
    layout_mode = 0
    theme_type_variation = "LoginPanel"
    custom_minimum_size = Vector2(360, 0)

    [node name="PanelContent" type="VBoxContainer" parent="Center/LoginPanel"]
    layout_mode = 0
    theme_type_variation = "PanelVBox"

    [node name="Title" type="Label" parent="Center/LoginPanel/PanelContent"]
    layout_mode = 0
    theme_type_variation = "TitleLabel"
    text = "LAKONA"

    [node name="NameLabel" type="Label" parent="Center/LoginPanel/PanelContent"]
    layout_mode = 0
    theme_type_variation = "NameLabel"
    text = "NAME:"

    [node name="NameField" type="LineEdit" parent="Center/LoginPanel/PanelContent"]
    layout_mode = 0
    max_length = 20
    custom_minimum_size = Vector2(0, 36)
    unique_name_in_owner = true

    [node name="ConnectButton" type="Button" parent="Center/LoginPanel/PanelContent"]
    layout_mode = 0
    text = "CONNECT"
    custom_minimum_size = Vector2(0, 36)
    unique_name_in_owner = true

    [node name="StatusLabel" type="Label" parent="Center/LoginPanel/PanelContent"]
    layout_mode = 0
    theme_type_variation = "StatusLabel"
    unique_name_in_owner = true
    """;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotLoginTscn"`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "feat: rewrite RenderGodotLoginTscn with full node tree and theme references"
```

---

### Task 3: Rewrite `RenderGodotChatTscn()` with full node tree

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void RenderGodotChatTscn_ContainsFullNodeTree()
{
    var tscn = ToolTemplates.RenderGodotChatTscn();

    Assert.Contains("[gd_scene load_steps=3 format=3]", tscn, StringComparison.Ordinal);
    Assert.Contains("[ext_resource type=\"Script\" path=\"res://Scripts/Chat/ChatScene.cs\" id=\"1\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[ext_resource type=\"Theme\" path=\"res://Theme/LakonaTheme.tres\" id=\"2\"]", tscn, StringComparison.Ordinal);

    Assert.Contains("[node name=\"ChatScene\" type=\"Control\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("theme = ExtResource(\"2\")", tscn, StringComparison.Ordinal);

    // Header section
    Assert.Contains("[node name=\"Header\" type=\"PanelContainer\" parent=\"Layout/ChatLayout\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"HeaderRow\" type=\"HBoxContainer\" parent=\"Layout/ChatLayout/Header\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"Title\" type=\"Label\" parent=\"Layout/ChatLayout/Header/HeaderRow\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"OnlineCount\" type=\"Label\" parent=\"Layout/ChatLayout/Header/HeaderRow\"]", tscn, StringComparison.Ordinal);

    // MessageLog
    Assert.Contains("[node name=\"MessageLog\" type=\"RichTextLabel\" parent=\"Layout/ChatLayout\"]", tscn, StringComparison.Ordinal);

    // Footer section
    Assert.Contains("[node name=\"Footer\" type=\"PanelContainer\" parent=\"Layout/ChatLayout\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"SendRow\" type=\"HBoxContainer\" parent=\"Layout/ChatLayout/Footer\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"MessageField\" type=\"LineEdit\" parent=\"Layout/ChatLayout/Footer/SendRow\"]", tscn, StringComparison.Ordinal);
    Assert.Contains("[node name=\"SendButton\" type=\"Button\" parent=\"Layout/ChatLayout/Footer/SendRow\"]", tscn, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotChatTscn_InteractiveNodesHaveUniqueNames()
{
    var tscn = ToolTemplates.RenderGodotChatTscn();

    Assert.Contains("MessageField", tscn, StringComparison.Ordinal);
    Assert.Contains("SendButton", tscn, StringComparison.Ordinal);
    Assert.Contains("MessageLog", tscn, StringComparison.Ordinal);
    Assert.Contains("OnlineCount", tscn, StringComparison.Ordinal);
    Assert.Contains("unique_name_in_owner = true", tscn, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotChatTscn_NodesUseThemeTypeVariations()
{
    var tscn = ToolTemplates.RenderGodotChatTscn();

    Assert.Contains("theme_type_variation = \"ChatHeader\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"ChatFooter\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"HeaderRow\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"SendRow\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"PageMargin\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"ChatVBox\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"HeaderLabel\"", tscn, StringComparison.Ordinal);
    Assert.Contains("theme_type_variation = \"OnlineCount\"", tscn, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotChatTscn"`
Expected: FAIL

- [ ] **Step 3: Rewrite `RenderGodotChatTscn()`**

Replace the existing method:

```csharp
public static string RenderGodotChatTscn()
{
    return """
    [gd_scene load_steps=3 format=3]

    [ext_resource type="Script" path="res://Scripts/Chat/ChatScene.cs" id="1"]
    [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2"]

    [node name="ChatScene" type="Control"]
    layout_mode = 3
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    theme = ExtResource("2")
    script = ExtResource("1")

    [node name="Background" type="ColorRect" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    color = Color(0.039, 0.059, 0.039, 1)

    [node name="Scanlines" type="ColorRect" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    color = Color(0, 0, 0, 0.08)
    mouse_filter = 2

    [node name="Layout" type="MarginContainer" parent="."]
    layout_mode = 1
    anchors_preset = 15
    anchor_right = 1.0
    anchor_bottom = 1.0
    grow_horizontal = 2
    grow_vertical = 2
    theme_type_variation = "PageMargin"

    [node name="ChatLayout" type="VBoxContainer" parent="Layout"]
    layout_mode = 0
    theme_type_variation = "ChatVBox"

    [node name="Header" type="PanelContainer" parent="Layout/ChatLayout"]
    layout_mode = 0
    theme_type_variation = "ChatHeader"

    [node name="HeaderRow" type="HBoxContainer" parent="Layout/ChatLayout/Header"]
    layout_mode = 0
    theme_type_variation = "HeaderRow"

    [node name="Title" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
    layout_mode = 0
    theme_type_variation = "HeaderLabel"
    text = "CHAT ROOM"
    size_flags_horizontal = 3

    [node name="OnlineCount" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
    layout_mode = 0
    theme_type_variation = "OnlineCount"
    text = "ONLINE: --"
    unique_name_in_owner = true

    [node name="MessageLog" type="RichTextLabel" parent="Layout/ChatLayout"]
    layout_mode = 0
    bbcode_enabled = false
    scroll_following = true
    size_flags_vertical = 3
    unique_name_in_owner = true

    [node name="Footer" type="PanelContainer" parent="Layout/ChatLayout"]
    layout_mode = 0
    theme_type_variation = "ChatFooter"

    [node name="SendRow" type="HBoxContainer" parent="Layout/ChatLayout/Footer"]
    layout_mode = 0
    theme_type_variation = "SendRow"

    [node name="MessageLabel" type="Label" parent="Layout/ChatLayout/Footer/SendRow"]
    layout_mode = 0
    theme_type_variation = "NameLabel"
    text = "MESSAGE:"

    [node name="MessageField" type="LineEdit" parent="Layout/ChatLayout/Footer/SendRow"]
    layout_mode = 0
    max_length = 500
    custom_minimum_size = Vector2(0, 36)
    size_flags_horizontal = 3
    unique_name_in_owner = true

    [node name="SendButton" type="Button" parent="Layout/ChatLayout/Footer/SendRow"]
    layout_mode = 0
    text = "SEND"
    custom_minimum_size = Vector2(96, 36)
    unique_name_in_owner = true
    """;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotChatTscn"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "feat: rewrite RenderGodotChatTscn with full node tree and theme references"
```

---

### Task 4: Refactor `RenderGodotLoginScene()` — remove BuildUi(), add GetNode references

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write tests for the refactored LoginScene**

```csharp
[Fact]
public void RenderGodotLoginScene_DoesNotContainBuildUi()
{
    var source = ToolTemplates.RenderGodotLoginScene(new NewCommandOptions(
        Name: "MyGame",
        OutputPath: null,
        ClientEngine: "godot",
        Transport: "kcp",
        NetworkProfile: "cluster",
        Serializer: "memorypack",
        Persistence: "none",
        NuGetForUnitySource: "embedded",
        DeployProfile: "none"));

    Assert.DoesNotContain("private void BuildUi()", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new ColorRect", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new CenterContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new PanelContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new VBoxContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new StyleBoxFlat", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeStyleboxOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeColorOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeFontSizeOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeConstantOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("SetAnchorsPreset", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddChild", source, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotLoginScene_UsesGetNodeWithUniqueNames()
{
    var source = ToolTemplates.RenderGodotLoginScene(new NewCommandOptions(
        Name: "MyGame",
        OutputPath: null,
        ClientEngine: "godot",
        Transport: "kcp",
        NetworkProfile: "cluster",
        Serializer: "memorypack",
        Persistence: "none",
        NuGetForUnitySource: "embedded",
        DeployProfile: "none"));

    Assert.Contains("GetNode<LineEdit>(\"%NameField\")", source, StringComparison.Ordinal);
    Assert.Contains("GetNode<Button>(\"%ConnectButton\")", source, StringComparison.Ordinal);
    Assert.Contains("GetNode<Label>(\"%StatusLabel\")", source, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotLoginScene_UsesSelectedTransportAndSerializer()
{
    var source = ToolTemplates.RenderGodotLoginScene(new NewCommandOptions(
        Name: "MyGame",
        OutputPath: null,
        ClientEngine: "godot",
        Transport: "websocket",
        NetworkProfile: "cluster",
        Serializer: "json",
        Persistence: "none",
        NuGetForUnitySource: "embedded",
        DeployProfile: "none"));

    Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", source, StringComparison.Ordinal);
    Assert.Contains("using Lakona.Rpc.Serializer.Json;", source, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotLoginScene_DoesNotUseThemeNamespaceInUsings()
{
    var source = ToolTemplates.RenderGodotLoginScene(new NewCommandOptions(
        Name: "MyGame",
        OutputPath: null,
        ClientEngine: "godot",
        Transport: "tcp",
        NetworkProfile: "cluster",
        Serializer: "json",
        Persistence: "none",
        NuGetForUnitySource: "embedded",
        DeployProfile: "none"));

    Assert.DoesNotContain("using Client.Theme;", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotLoginScene"`
Expected: FAIL (BuildUi still exists, using Client.Theme still present)

- [ ] **Step 3: Rewrite `RenderGodotLoginScene()`**

Replace the existing method. The new `_Ready()` and remove `BuildUi()`:

Change the usings block — remove `using Client.Theme;`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Shared.Contracts.Chat;
using Client.Chat;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
{{serializerUsing}}
{{transportUsing}}
```

Replace the `_Ready()` and remove `BuildUi()`:

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

The rest of the method (OnConnectPressed, SetStatus, SetBusy, CreateRpcClientOptions, NormalizePath, ConfigureTransportSecurity, _ExitTree) stays identical.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotLoginScene"`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "refactor: remove BuildUi from RenderGodotLoginScene, use GetNode with unique names"
```

---

### Task 5: Refactor `RenderGodotChatScene()` — remove BuildUi(), add GetNode references

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write tests for the refactored ChatScene**

```csharp
[Fact]
public void RenderGodotChatScene_DoesNotContainBuildUi()
{
    var source = ToolTemplates.RenderGodotChatScene();

    Assert.DoesNotContain("private void BuildUi()", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new ColorRect", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new PanelContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new VBoxContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new HBoxContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new MarginContainer", source, StringComparison.Ordinal);
    Assert.DoesNotContain("new StyleBoxFlat", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeStyleboxOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeColorOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeFontSizeOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddThemeConstantOverride", source, StringComparison.Ordinal);
    Assert.DoesNotContain("SetAnchorsPreset", source, StringComparison.Ordinal);
    Assert.DoesNotContain("AddChild", source, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotChatScene_UsesGetNodeWithUniqueNames()
{
    var source = ToolTemplates.RenderGodotChatScene();

    Assert.Contains("GetNode<LineEdit>(\"%MessageField\")", source, StringComparison.Ordinal);
    Assert.Contains("GetNode<Button>(\"%SendButton\")", source, StringComparison.Ordinal);
    Assert.Contains("GetNode<RichTextLabel>(\"%MessageLog\")", source, StringComparison.Ordinal);
    Assert.Contains("GetNode<Label>(\"%OnlineCount\")", source, StringComparison.Ordinal);
}

[Fact]
public void RenderGodotChatScene_DoesNotUseThemeNamespaceInUsings()
{
    var source = ToolTemplates.RenderGodotChatScene();

    Assert.DoesNotContain("using Client.Theme;", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotChatScene"`
Expected: FAIL

- [ ] **Step 3: Rewrite `RenderGodotChatScene()`**

Remove `using Client.Theme;` from usings.

Replace `_Ready()` and remove `BuildUi()`:

```csharp
public override void _Ready()
{
    _messageField = GetNode<LineEdit>("%MessageField");
    _sendButton = GetNode<Button>("%SendButton");
    _messageLog = GetNode<RichTextLabel>("%MessageLog");
    _onlineCount = GetNode<Label>("%OnlineCount");

    _messageField.TextSubmitted += _ => OnSendPressed();
    _sendButton.Pressed += OnSendPressed;

    var session = GetNode<ChatSession>("/root/ChatSession");
    // ... rest of startup logic unchanged
}
```

The rest (OnSendPressed, AppendMessageText, AppendSystemMessage, AppendLine, SetOnlineCount, SetSendBusy, BindChatAsync, _ExitTree) stays identical.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "RenderGodotChatScene"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "refactor: remove BuildUi from RenderGodotChatScene, use GetNode with unique names"
```

---

### Task 6: Delete `RenderGodotThemeClass()` and update references

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- Modify: `src/Lakona.Tool/Scaffolding/ProjectScaffolder.cs`
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Update ProjectScaffolder to write .tres instead of .cs theme**

In `WriteClientChatFilesAsync()` Godot branch, replace:

```csharp
WriteIfMissingAsync(
    Path.Combine(projectRoot, "Client", "Scripts", "Theme", "LakonaTheme.cs"),
    ToolTemplates.RenderGodotThemeClass()),
```

with:

```csharp
WriteAsync(
    Path.Combine(projectRoot, "Client", "Theme", "LakonaTheme.tres"),
    ToolTemplates.RenderGodotTheme()),
```

- [ ] **Step 2: Delete `RenderGodotThemeClass()` method**

Remove the entire `RenderGodotThemeClass()` method (lines 157-205) from `ChatClientTemplates.cs`.

- [ ] **Step 3: Run existing tests to see what breaks**

Run: `dotnet test tests/Lakona.Tool.Tests/`
Expected: compilation errors in tests referencing `RenderGodotThemeClass()`

- [ ] **Step 4: Update tests that reference `RenderGodotThemeClass()`**

The `ToolScaffoldNewTests` uses `AugmentProjectWithLakonaGameAsync` which internally calls `WriteClientChatFilesAsync`. Let it run and check if any test directly references the method.

Search for and update/remove any test assertions that check for `LakonaTheme.cs` file existence. The `Theme` directory path changes from `Scripts/Theme/LakonaTheme.cs` to `Theme/LakonaTheme.tres`.

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/Lakona.Tool.Tests/`
Expected: all tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs src/Lakona.Tool/Scaffolding/ProjectScaffolder.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "refactor: replace RenderGodotThemeClass with RenderGodotTheme, write .tres instead of .cs"
```

---

### Task 7: Update tests that reference old UI patterns

**Files:**
- Modify: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Update `RenderChatTemplatesUseSharedContractsChatNamespace`**

The test at line 696 references `ToolTemplates.RenderGodotChatScene()`. After our changes, this method exists but has different content. The assertion `Assert.Contains("using Shared.Contracts.Chat;", godotScene, StringComparison.Ordinal)` is still valid. Verify.

- [ ] **Step 2: Update `RenderGodotChatScene_ImportsLoginNamespace`**

Line 737: This test checks `using Client.Login;` and `private LoginClient?`. These are still present. Verify.

- [ ] **Step 3: Update `RenderClientChatTemplates_DoNotUseSessionConnectionId`**

Line 746: Creates `godotScene` from `RenderGodotChatScene()` and `godotSession` from `RenderGodotChatSession()`. Both still exist. Verify.

- [ ] **Step 4: Update `RenderGodotLoginScene_DoesNotUseSessionConnectionId`**

Line 762: Tests `RenderGodotLoginScene(options)`. The session.LoginClient/reply setup is still there. Verify.

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/Lakona.Tool.Tests/`
Expected: all tests PASS

- [ ] **Step 6: Commit if changes needed, else note verification**

```bash
git add tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "test: verify existing tests pass after Godot UI refactoring"
```

---

### Task 8: Verify end-to-end scaffold generates valid files

**Files:**
- Create: (temporary) an E2E-style validation in `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Write integration-style test**

```csharp
[Fact]
public async Task GodotScaffold_GeneratesThemeAndFullTscnFiles()
{
    var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var serverDirectory = Path.Combine(projectRoot, "Server", "App");
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(projectRoot, "Shared"));
        await File.WriteAllTextAsync(
            Path.Combine(serverDirectory, "Server.App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken);

        await new ProjectScaffolder().AugmentProjectWithLakonaGameAsync(
            projectRoot,
            new NewCommandOptions(
                Name: "MyGame",
                OutputPath: null,
                ClientEngine: "godot",
                Transport: "kcp",
                NetworkProfile: "cluster",
                Serializer: "memorypack",
                Persistence: "none",
                NuGetForUnitySource: "embedded",
                DeployProfile: "none"));

        // Verify .tres exists
        var tresPath = Path.Combine(projectRoot, "Client", "Theme", "LakonaTheme.tres");
        Assert.True(File.Exists(tresPath), "LakonaTheme.tres should be created");
        var tresContent = await File.ReadAllTextAsync(tresPath, TestContext.Current.CancellationToken);
        Assert.Contains("[gd_resource type=\"Theme\"", tresContent, StringComparison.Ordinal);

        // Verify .tscn files contain full node trees
        var loginTscnPath = Path.Combine(projectRoot, "Client", "Login.tscn");
        Assert.True(File.Exists(loginTscnPath));
        var loginTscn = await File.ReadAllTextAsync(loginTscnPath, TestContext.Current.CancellationToken);
        Assert.Contains("unique_name_in_owner = true", loginTscn, StringComparison.Ordinal);
        Assert.Contains("theme = ExtResource(\"2\")", loginTscn, StringComparison.Ordinal);

        var chatTscnPath = Path.Combine(projectRoot, "Client", "Chat.tscn");
        Assert.True(File.Exists(chatTscnPath));
        var chatTscn = await File.ReadAllTextAsync(chatTscnPath, TestContext.Current.CancellationToken);
        Assert.Contains("unique_name_in_owner = true", chatTscn, StringComparison.Ordinal);
        Assert.Contains("theme = ExtResource(\"2\")", chatTscn, StringComparison.Ordinal);

        // Verify LakonaTheme.cs is NOT generated
        var themeCsPath = Path.Combine(projectRoot, "Client", "Scripts", "Theme", "LakonaTheme.cs");
        Assert.False(File.Exists(themeCsPath), "LakonaTheme.cs should not be generated");

        // Verify LoginScene.cs does NOT contain BuildUi
        var loginCsPath = Path.Combine(projectRoot, "Client", "Scripts", "Login", "LoginScene.cs");
        var loginCs = await File.ReadAllTextAsync(loginCsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("BuildUi", loginCs, StringComparison.Ordinal);
        Assert.Contains("GetNode<LineEdit>(\"%NameField\")", loginCs, StringComparison.Ordinal);

        // Verify ChatScene.cs does NOT contain BuildUi
        var chatCsPath = Path.Combine(projectRoot, "Client", "Scripts", "Chat", "ChatScene.cs");
        var chatCs = await File.ReadAllTextAsync(chatCsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("BuildUi", chatCs, StringComparison.Ordinal);
        Assert.Contains("GetNode<LineEdit>(\"%MessageField\")", chatCs, StringComparison.Ordinal);
    }
    finally
    {
        if (Directory.Exists(projectRoot))
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the integration test**

Run: `dotnet test tests/Lakona.Tool.Tests/ --filter "GodotScaffold_GeneratesThemeAndFullTscnFiles"`
Expected: PASS

- [ ] **Step 3: Run full test suite**

Run: `dotnet test tests/Lakona.Tool.Tests/`
Expected: all tests PASS

- [ ] **Step 4: Commit**

```bash
git add tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "test: add E2E integration test for Godot scaffold theme+tscn output"
```

## 2026-06-11 Review Handoff Issues

These issues were found during a review of the 2026-06-11 commits. Address them
before considering the branch complete.

### Issue 1: Godot Theme type variations are missing base type declarations

Severity: Warning

Current generated scenes use custom `theme_type_variation` values such as:

- `LoginPanel`
- `PanelVBox`
- `TitleLabel`
- `NameLabel`
- `StatusLabel`
- `PageMargin`
- `ChatVBox`
- `ChatHeader`
- `HeaderRow`
- `HeaderLabel`
- `OnlineCount`
- `ChatFooter`
- `SendRow`

Affected source:

- `src/Lakona.Tool/Scaffolding/Templates/ChatClientTemplates.cs`
- `RenderGodotTheme()`
- `RenderGodotLoginTscn()`
- `RenderGodotChatTscn()`

Problem:

`RenderGodotTheme()` writes entries like `TitleLabel/colors/font_color` and
`LoginPanel/styles/panel`, while the `.tscn` files apply those names through
`theme_type_variation = TitleLabel` and similar lines. Godot theme type
variations should be associated with their base control type. Without base type
declarations, Godot may not resolve the variation as a `Label`,
`PanelContainer`, `VBoxContainer`, `HBoxContainer`, or `MarginContainer`
variation.

Expected fix direction:

- Add base type declarations to `RenderGodotTheme()` for every custom
  variation.
- Map each variation to the actual control type used in the generated scene:
  - `LoginPanel`, `ChatHeader`, `ChatFooter` -> `PanelContainer`
  - `PanelVBox`, `ChatVBox` -> `VBoxContainer`
  - `HeaderRow`, `SendRow` -> `HBoxContainer`
  - `TitleLabel`, `HeaderLabel`, `NameLabel`, `StatusLabel`, `OnlineCount` -> `Label`
  - `PageMargin` -> `MarginContainer`
- Add tests that assert the generated `.tres` contains these base type
  declarations, not only color/style entries.

Suggested verification:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-build
git diff --check 454cb4b89d0f811201e2e9a5f0080b8eedc349ce..HEAD
```

If the local Godot executable is stable, also generate a temporary Godot project
and verify that Godot can import or open the generated `Client/` project without
resource parse or theme warnings.

### Issue 2: Temporary docs/superpowers files must not remain in the finished branch

Severity: Warning

Affected files:

- `docs/superpowers/plans/2026-06-11-godot-tscn-theme-separation-plan.md`
- `docs/superpowers/specs/2026-06-11-godot-tscn-theme-separation-design.md`

Problem:

`CONTRIBUTING.md` says `docs/superpowers/**` is a temporary working directory.
Before finishing the development branch, move any durable design material into
permanent documentation under `docs/**`, then delete the entire
`docs/superpowers` directory.

Expected fix direction:

- Preserve any durable decisions in the appropriate permanent doc, likely under
  `docs/rpc/starter/**` or another existing contributor-facing doc.
- Delete `docs/superpowers/**` before the final commit/PR.
- Do this after the implementation issue above is resolved, so this handoff
  note is not removed before the next worker has used it.

Review notes:

- `dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-build`
  passed locally: 175/175.
- `git diff --check 454cb4b89d0f811201e2e9a5f0080b8eedc349ce..HEAD` passed.
- A temporary Godot project was generated under `.tmp`, but Godot 4.6.1 Mono
  crashed in this environment during headless startup, so real Godot import/load
  validation was not completed.
