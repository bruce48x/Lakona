namespace Lakona.ProjectSystem.Generation.Rendering.Client;

internal static class GodotClientAssetTemplates
{
    public const string GameClientUid = "uid://bgameclient001";
    public const string GameSceneScriptUid = "uid://bgamescene0001";

    public static string RenderUid(string uid) => uid;

    public static string RenderGameScene()
    {
        return $$"""
        [gd_scene load_steps=3 format=3]

        [ext_resource type="Script" path="res://Scripts/Game/GameScene.cs" id="1_game"]
        [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2_theme"]

        [node name="Game" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2
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
        offset_top = -240.0
        offset_right = 410.0
        offset_bottom = 260.0
        theme_override_constants/separation = 4
        alignment = 1

        [node name="Title" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 90)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 82
        text = "LAKONA"
        horizontal_alignment = 1
        vertical_alignment = 1

        [node name="Arena" type="Label" parent="Ui/LoginPanel/VBox"]
        custom_minimum_size = Vector2(0, 76)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 70
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
        custom_minimum_size = Vector2(700, 76)
        layout_mode = 2
        size_flags_horizontal = 4
        theme_override_constants/separation = 0

        [node name="Name" type="LineEdit" parent="Ui/LoginPanel/VBox/Action"]
        custom_minimum_size = Vector2(460, 76)
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
        clip_text = true

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
        custom_minimum_size = Vector2(180, 0)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.745, 0.89, 0.11, 1)
        theme_override_font_sizes/font_size = 20
        text = "LAKONA_01"
        clip_text = true
        vertical_alignment = 1

        [node name="Score" type="Label" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(110, 0)
        layout_mode = 2
        theme_override_colors/font_color = Color(0.957, 0.945, 0.886, 1)
        theme_override_font_sizes/font_size = 18
        text = "SCORE 12,540"
        vertical_alignment = 1

        [node name="HealthBox" type="VBoxContainer" parent="Ui/Hud/HBox"]
        custom_minimum_size = Vector2(0, 0)
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
        custom_minimum_size = Vector2(170, 0)
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
        [gd_resource type="Theme" load_steps=8 format=3]

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

        Label/colors/font_color = Color(0.957, 0.945, 0.886, 1)
        Label/font_sizes/font_size = 14

        ArenaInput/base_type = "LineEdit"
        ArenaInput/colors/font_color = Color(0.957, 0.945, 0.886, 1)
        ArenaInput/colors/font_placeholder_color = Color(0.42, 0.43, 0.4, 1)
        ArenaInput/font_sizes/font_size = 24
        ArenaInput/styles/normal = SubResource("8")
        ArenaInput/styles/focus = SubResource("9")

        ArenaButton/base_type = "Button"
        ArenaButton/colors/font_color = Color(0.957, 0.945, 0.886, 1)
        ArenaButton/colors/font_hover_color = Color(1, 1, 1, 1)
        ArenaButton/colors/font_disabled_color = Color(0.55, 0.55, 0.52, 1)
        ArenaButton/font_sizes/font_size = 25
        ArenaButton/styles/normal = SubResource("10")
        ArenaButton/styles/hover = SubResource("11")
        ArenaButton/styles/pressed = SubResource("11")
        ArenaButton/styles/disabled = SubResource("10")

        ArenaHud/base_type = "PanelContainer"
        ArenaHud/styles/panel = SubResource("12")

        ArenaHealth/base_type = "ProgressBar"
        ArenaHealth/styles/background = SubResource("13")
        ArenaHealth/styles/fill = SubResource("14")
        """;
    }

}
