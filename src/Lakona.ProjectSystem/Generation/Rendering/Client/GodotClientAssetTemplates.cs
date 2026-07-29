namespace Lakona.ProjectSystem.Generation.Rendering.Client;

internal static class GodotClientAssetTemplates
{
    public const string GameClientUid = "uid://bgameclient001";
    public const string GameSceneScriptUid = "uid://bgamescene0001";
    public const string LoginClientUid = "uid://dud7ml45qrep2";
    public const string ChatClientUid = "uid://b1qinooxr8f6s";
    public const string ChatSessionUid = "uid://vwckwksgbs03";
    public const string LoginSceneUid = "uid://c0rp71v1htp3h";
    public const string ChatSceneUid = "uid://dyo6cbh1f6nkd";

    public static string RenderUid(string uid) => uid;

    public static string RenderGameScene()
    {
        return $$"""
        [gd_scene load_steps=3 format=3]

        [ext_resource type="Script" path="res://Scripts/Game/GameScene.cs" id="1_game"]
        [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2_theme"]

        [node name="Game" type="Node2D"]
        script = ExtResource("1_game")

        [node name="Ui" type="Control" parent="."]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2
        theme = ExtResource("2_theme")

        [node name="Online" type="Label" parent="Ui"]
        layout_mode = 0
        offset_left = 30.0
        offset_top = 24.0
        offset_right = 160.0
        offset_bottom = 58.0
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 19
        text = "ONLINE"

        [node name="LoginPanel" type="Control" parent="Ui"]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 0

        [node name="VBox" type="VBoxContainer" parent="Ui/LoginPanel"]
        layout_mode = 0
        anchor_left = 0.5
        anchor_top = 0.5
        anchor_right = 0.5
        anchor_bottom = 0.5
        offset_left = -410.0
        offset_top = -330.0
        offset_right = 410.0
        offset_bottom = 220.0
        theme_override_constants/separation = 4

        [node name="Title" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 108)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 92
        text = "LAKONA"
        horizontal_alignment = 1
        vertical_alignment = 1

        [node name="Arena" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 88)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 76
        text = "ARENA"
        horizontal_alignment = 1
        vertical_alignment = 1

        [node name="Callsign" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 44)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 18
        text = "—  CHOOSE YOUR CALLSIGN  —"
        horizontal_alignment = 1
        vertical_alignment = 1

        [node name="Action" type="HBoxContainer" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 76)
        layout_mode = 2
        theme_override_constants/separation = 0

        [node name="Name" type="LineEdit" parent="Ui/LoginPanel/VBox/Action"]
        custom_minimum_size = Vector2(560, 76)
        layout_mode = 2
        size_flags_horizontal = 3
        theme_type_variation = &"ArenaInput"
        max_length = 20
        placeholder_text = "YOUR CALLSIGN"

        [node name="Play" type="Button" parent="Ui/LoginPanel/VBox/Action"]
        custom_minimum_size = Vector2(240, 76)
        layout_mode = 2
        theme_type_variation = &"ArenaButton"
        text = "PLAY NOW"

        [node name="Status" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 36)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 15
        text = "Enter a name to join."
        horizontal_alignment = 1
        autowrap_mode = 2

        [node name="Hud" type="PanelContainer" parent="Ui"]
        visible = false
        layout_mode = 0
        anchor_left = 0.0
        anchor_top = 1.0
        anchor_right = 1.0
        anchor_bottom = 1.0
        offset_left = 32.0
        offset_top = -118.0
        offset_right = -32.0
        offset_bottom = -24.0
        grow_horizontal = 2
        grow_vertical = 0
        mouse_filter = 2
        theme_type_variation = &"ArenaHud"

        [node name="HBox" type="HBoxContainer" parent="Ui/Hud"]
        layout_mode = 2
        theme_override_constants/separation = 20

        [node name="Player" type="Label" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(230, 0)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 20
        text = "LAKONA_01"
        vertical_alignment = 1

        [node name="Score" type="Label" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(170, 0)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 18
        text = "SCORE 12,540"
        vertical_alignment = 1

        [node name="HealthBox" type="VBoxContainer" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(380, 0)
        layout_mode = 2
        size_flags_horizontal = 3
        theme_override_constants/separation = 6

        [node name="Health" type="Label" parent="Ui/Hud/HBox/HealthBox"]
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        text = "HEALTH 100 / 100"

        [node name="HealthBar" type="ProgressBar" parent="Ui/Hud/HBox/HealthBox"]
        custom_minimum_size = Vector2(0, 20)
        layout_mode = 2
        theme_type_variation = &"ArenaHealth"
        value = 100.0
        show_percentage = false

        [node name="Hint" type="Label" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(270, 0)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 16
        text = "[ W ] [ A ] [ S ] [ D ]\nMOVE · AUTO FIRE"
        horizontal_alignment = 2
        vertical_alignment = 1
        """;
    }

    public static string RenderTheme()
    {
        return """
        [gd_resource type="Theme" load_steps=15 format=3]

        [sub_resource type="StyleBoxFlat" id="1"]
        bg_color = Color(0.02, 0.039, 0.039, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 0.667, 0.267, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="2"]
        bg_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="3"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 0.667, 0.267, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="4"]
        bg_color = Color(0.2, 1, 0.533, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="5"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 24.0
        content_margin_right = 24.0
        content_margin_top = 32.0
        content_margin_bottom = 32.0

        [sub_resource type="StyleBoxFlat" id="6"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_bottom = 2
        border_width_left = 0
        border_width_right = 0
        border_width_top = 0
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 0.0
        content_margin_right = 0.0
        content_margin_top = 8.0
        content_margin_bottom = 8.0

        [sub_resource type="StyleBoxFlat" id="7"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_top = 2
        border_width_left = 0
        border_width_right = 0
        border_width_bottom = 0
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 0.0
        content_margin_right = 0.0
        content_margin_top = 8.0
        content_margin_bottom = 8.0

        [sub_resource type="StyleBoxFlat" id="8"]
        bg_color = Color(0.039, 0.047, 0.047, 0.97)
        border_width_left = 3
        border_width_top = 3
        border_width_bottom = 3
        border_color = Color(0.745, 0.89, 0.11, 1)
        content_margin_left = 22.0
        content_margin_right = 18.0
        content_margin_top = 16.0
        content_margin_bottom = 16.0

        [sub_resource type="StyleBoxFlat" id="9"]
        bg_color = Color(0.055, 0.063, 0.059, 1)
        border_width_left = 4
        border_width_top = 4
        border_width_bottom = 4
        border_color = Color(0.87, 1, 0.18, 1)
        content_margin_left = 21.0
        content_margin_right = 17.0
        content_margin_top = 15.0
        content_margin_bottom = 15.0

        [sub_resource type="StyleBoxFlat" id="10"]
        bg_color = Color(1, 0.298, 0.251, 1)
        border_width_top = 3
        border_width_right = 3
        border_width_bottom = 3
        border_color = Color(0.957, 0.945, 0.886, 1)
        content_margin_left = 18.0
        content_margin_right = 18.0

        [sub_resource type="StyleBoxFlat" id="11"]
        bg_color = Color(1, 0.396, 0.325, 1)
        border_width_top = 3
        border_width_right = 3
        border_width_bottom = 3
        border_color = Color(1, 1, 1, 1)
        content_margin_left = 18.0
        content_margin_right = 18.0

        [sub_resource type="StyleBoxFlat" id="12"]
        bg_color = Color(0.039, 0.047, 0.047, 0.97)
        border_width_left = 2
        border_width_top = 2
        border_width_right = 2
        border_width_bottom = 2
        border_color = Color(0.745, 0.89, 0.11, 1)
        content_margin_left = 22.0
        content_margin_right = 22.0
        content_margin_top = 14.0
        content_margin_bottom = 14.0

        [sub_resource type="StyleBoxFlat" id="13"]
        bg_color = Color(0.212, 0.224, 0.208, 1)

        [sub_resource type="StyleBoxFlat" id="14"]
        bg_color = Color(0.745, 0.89, 0.11, 1)

        [resource]
        default_font_size = 14

        Button/colors/font_color = Color(0.039, 0.059, 0.039, 1)
        Button/colors/font_disabled_color = Color(0, 0.667, 0.267, 1)
        Button/styles/normal = SubResource("2")
        Button/styles/disabled = SubResource("3")
        Button/styles/hover = SubResource("4")

        LineEdit/colors/font_color = Color(0, 1, 0.4, 1)
        LineEdit/colors/font_placeholder_color = Color(0.267, 0.533, 0.333, 1)
        LineEdit/styles/normal = SubResource("1")

        Label/colors/font_color = Color(0.533, 0.8, 0.6, 1)
        Label/font_sizes/font_size = 14

        RichTextLabel/colors/default_color = Color(0.533, 0.8, 0.6, 1)
        RichTextLabel/font_sizes/normal_font_size = 14

        TitleLabel/type = "Label"
        TitleLabel/colors/font_color = Color(0, 1, 0.4, 1)
        TitleLabel/font_sizes/font_size = 22

        HeaderLabel/type = "Label"
        HeaderLabel/colors/font_color = Color(0, 1, 0.4, 1)
        HeaderLabel/font_sizes/font_size = 18

        NameLabel/type = "Label"
        NameLabel/colors/font_color = Color(0, 0.667, 0.267, 1)
        NameLabel/font_sizes/font_size = 14

        StatusLabel/type = "Label"
        StatusLabel/colors/font_color = Color(1, 0.267, 0.267, 1)
        StatusLabel/font_sizes/font_size = 14

        OnlineCount/type = "Label"
        OnlineCount/colors/font_color = Color(1, 1, 0, 1)
        OnlineCount/font_sizes/font_size = 14

        PanelVBox/type = "VBoxContainer"
        PanelVBox/constants/separation = 12
        ArenaVBox/type = "VBoxContainer"
        ArenaVBox/constants/separation = 0
        HeaderRow/type = "HBoxContainer"
        HeaderRow/constants/separation = 12
        SendRow/type = "HBoxContainer"
        SendRow/constants/separation = 8

        PageMargin/type = "MarginContainer"
        PageMargin/constants/margin_left = 16
        PageMargin/constants/margin_right = 16
        PageMargin/constants/margin_top = 16
        PageMargin/constants/margin_bottom = 16

        LoginPanel/type = "PanelContainer"
        LoginPanel/styles/panel = SubResource("5")
        ArenaHeader/type = "PanelContainer"
        ArenaHeader/styles/panel = SubResource("6")
        ArenaFooter/type = "PanelContainer"
        ArenaFooter/styles/panel = SubResource("7")

        ArenaInput/type = "LineEdit"
        ArenaInput/colors/font_color = Color(0.957, 0.945, 0.886, 1)
        ArenaInput/colors/font_placeholder_color = Color(0.42, 0.43, 0.4, 1)
        ArenaInput/font_sizes/font_size = 24
        ArenaInput/styles/normal = SubResource("8")
        ArenaInput/styles/focus = SubResource("9")

        ArenaButton/type = "Button"
        ArenaButton/colors/font_color = Color(0.957, 0.945, 0.886, 1)
        ArenaButton/colors/font_hover_color = Color(1, 1, 1, 1)
        ArenaButton/colors/font_disabled_color = Color(0.55, 0.55, 0.52, 1)
        ArenaButton/font_sizes/font_size = 25
        ArenaButton/styles/normal = SubResource("10")
        ArenaButton/styles/hover = SubResource("11")
        ArenaButton/styles/pressed = SubResource("11")
        ArenaButton/styles/disabled = SubResource("10")

        ArenaHud/type = "PanelContainer"
        ArenaHud/styles/panel = SubResource("12")

        ArenaHealth/type = "ProgressBar"
        ArenaHealth/styles/background = SubResource("13")
        ArenaHealth/styles/fill = SubResource("14")
        """;
    }

    public static string RenderLoginScene()
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
        theme_type_variation = &"LoginPanel"
        custom_minimum_size = Vector2(360, 0)

        [node name="PanelContent" type="VBoxContainer" parent="Center/LoginPanel"]
        layout_mode = 0
        theme_type_variation = &"PanelVBox"

        [node name="Title" type="Label" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        theme_type_variation = &"TitleLabel"
        text = "LAKONA"

        [node name="NameLabel" type="Label" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        theme_type_variation = &"NameLabel"
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
        theme_type_variation = &"StatusLabel"
        unique_name_in_owner = true
        """;
    }

    public static string RenderChatScene()
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
        theme_type_variation = &"PageMargin"

        [node name="ChatLayout" type="VBoxContainer" parent="Layout"]
        layout_mode = 0
        theme_type_variation = &"ChatVBox"

        [node name="Header" type="PanelContainer" parent="Layout/ChatLayout"]
        layout_mode = 0
        theme_type_variation = &"ChatHeader"

        [node name="HeaderRow" type="HBoxContainer" parent="Layout/ChatLayout/Header"]
        layout_mode = 0
        theme_type_variation = &"HeaderRow"

        [node name="Title" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
        layout_mode = 0
        theme_type_variation = &"HeaderLabel"
        text = "CHAT ROOM"
        size_flags_horizontal = 3

        [node name="OnlineCount" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
        layout_mode = 0
        theme_type_variation = &"OnlineCount"
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
        theme_type_variation = &"ChatFooter"

        [node name="SendRow" type="HBoxContainer" parent="Layout/ChatLayout/Footer"]
        layout_mode = 0
        theme_type_variation = &"SendRow"

        [node name="MessageLabel" type="Label" parent="Layout/ChatLayout/Footer/SendRow"]
        layout_mode = 0
        theme_type_variation = &"NameLabel"
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
}
