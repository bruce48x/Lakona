namespace Lakona.Tool.Rendering.Client;

internal static class GodotClientAssetTemplates
{
    public const string LoginClientUid = "uid://dud7ml45qrep2";
    public const string ChatClientUid = "uid://b1qinooxr8f6s";
    public const string ChatSessionUid = "uid://vwckwksgbs03";
    public const string LoginSceneUid = "uid://c0rp71v1htp3h";
    public const string ChatSceneUid = "uid://dyo6cbh1f6nkd";

    public static string RenderUid(string uid) => uid;

    public static string RenderTheme()
    {
        return """
        [gd_resource type="Theme" load_steps=8 format=3]

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
        ChatVBox/type = "VBoxContainer"
        ChatVBox/constants/separation = 0
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
        ChatHeader/type = "PanelContainer"
        ChatHeader/styles/panel = SubResource("6")
        ChatFooter/type = "PanelContainer"
        ChatFooter/styles/panel = SubResource("7")
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
