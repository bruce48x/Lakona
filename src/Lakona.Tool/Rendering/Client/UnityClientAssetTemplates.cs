using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class UnityClientAssetTemplates
{
    public const string LoginClientGuid = "1a1f98ba46486884b824d248c98d6e38";
    public const string ChatClientGuid = "fff9f5180f8be804a88038c0f7860779";
    public const string ChatSessionGuid = "c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6";
    public const string RpcGenerationGuid = "d2e51b4bbd591304db8b574127c61d6e";
    public const string LoginUiGuid = "5a1b8c3d2e4f6a7b8c9d0e1f2a3b4c5d";
    public const string ChatUiGuid = "462a8730535800d4a801000623f4450e";
    public const string LoginSceneUxmlGuid = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
    public const string LoginSceneUssGuid = "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7";
    public const string ChatSceneUxmlGuid = "d8e055cb54604094cb41badb6b3866f6";
    public const string ChatSceneUssGuid = "f7e09962267bcef45a558136fb62bb68";
    public const string PanelSettingsGuid = "0c8089bab5856fe4d8f88e6f526fd306";
    public const string RuntimeThemeGuid = "9a59d5efd84abc44da5e32a04db78d26";
    public const string LoginSceneGuid = "7a244091a9bb4d7a9f119d19bc86c012";
    public const string ChatSceneGuid = "3f4a119acc61449cb6f0b9fc01a71d7e";
    public const string ImportGuardGuid = "0fdc9d512cbf4d71a198872e996940f7";

    public static string RenderMonoScriptMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        MonoImporter:
          externalObjects: {}
          serializedVersion: 2
          defaultReferences: []
          executionOrder: 0
          icon: {instanceID: 0}
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
    }

    public static string RenderUxmlMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}
        """;
    }

    public static string RenderUssMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}
          disableValidation: 0
        """;
    }

    public static string RenderTssMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 12388, guid: 0000000000000000e000000000000000, type: 0}
          disableValidation: 0
        """;
    }

    public static string RenderNativeAssetMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        NativeFormatImporter:
          externalObjects: {}
          mainObjectFileID: 11400000
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
    }

    public static string RenderSceneMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        DefaultImporter:
          externalObjects: {}
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
    }

    public static string RenderLoginUxml()
    {
        return """
        <ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
            <Style src="LoginScene.uss" />
            <ui:VisualElement class="login-container">
                <ui:VisualElement class="login-panel">
                    <ui:Label text="LAKONA" class="login-title" />
                    <ui:Label text="NAME:" class="name-label" />
                    <ui:TextField name="name-field" max-length="20" class="name-field" />
                    <ui:Button text="CONNECT" name="connect-button" class="connect-button" />
                    <ui:Label text="" name="status-label" class="status-label" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:UXML>
        """;
    }

    public static string RenderChatUxml()
    {
        return """
        <ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
            <Style src="ChatScene.uss" />
            <ui:VisualElement class="chat-container">
                <ui:VisualElement class="chat-header">
                    <ui:Label text="CHAT ROOM" class="header-title" />
                    <ui:Label text="ONLINE: --" name="online-count" class="header-count" />
                </ui:VisualElement>
                <ui:ScrollView name="message-list" class="message-list" />
                <ui:VisualElement class="chat-footer">
                    <ui:Label text="MESSAGE:" class="message-label" />
                    <ui:TextField name="chat-input" max-length="500" class="chat-input" />
                    <ui:Button text="SEND" name="send-button" class="send-button" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:UXML>
        """;
    }

    public static string RenderLoginUss()
    {
        return """
        .login-container {
            width: 100%;
            height: 100%;
            flex-grow: 1;
            background-color: var(--lakona-bg-base);
            align-items: center;
            justify-content: center;
        }
        .login-panel {
            width: 360px;
            padding: 32px 24px;
            background-color: var(--lakona-bg-panel);
            border-left-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-top-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-top-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
        }
        .login-title {
            font-size: var(--lakona-font-size-title);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent);
            margin-bottom: 20px;
        }
        .name-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent-dim);
            margin-bottom: 4px;
        }
        .name-field {
            margin-bottom: 16px;
        }
        .name-field .unity-text-field__label {
            color: var(--lakona-accent-dim);
            -unity-font: var(--lakona-font);
        }
        .name-field .unity-text-field__input {
            color: var(--lakona-accent);
            -unity-font: var(--lakona-font);
            font-size: var(--lakona-font-size);
            background-color: var(--lakona-bg-input);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        .name-field .unity-text-field__input:focus {
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
        }
        .connect-button {
            width: 100%;
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-bg-base);
            background-color: var(--lakona-accent);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
            margin-bottom: 12px;
        }
        .connect-button:disabled {
            color: var(--lakona-accent-dim);
            background-color: var(--lakona-bg-input);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        .status-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-error);
            white-space: normal;
        }
        """;
    }

    public static string RenderChatUss()
    {
        return """
        .chat-container {
            width: 100%;
            height: 100%;
            flex-grow: 1;
            background-color: var(--lakona-bg-base);
        }
        .chat-header {
            flex-direction: row;
            align-items: center;
            padding: 8px 16px;
            background-color: var(--lakona-bg-panel);
            border-bottom-width: var(--lakona-border-width);
            border-bottom-color: var(--lakona-accent);
        }
        .header-title {
            font-size: var(--lakona-font-size-header);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent);
            flex-grow: 1;
        }
        .header-count {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-warning);
        }
        .message-list {
            flex-grow: 1;
            padding: 8px 16px;
        }
        .chat-message {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-text-body);
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-system {
            font-size: var(--lakona-font-size-system);
            -unity-font: var(--lakona-font);
            color: var(--lakona-text-system);
            -unity-font-style: italic;
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-footer {
            flex-direction: row;
            align-items: center;
            padding: 8px 16px;
            background-color: var(--lakona-bg-panel);
            border-top-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent);
        }
        .message-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent-dim);
            margin-right: 8px;
        }
        .chat-input {
            flex-grow: 1;
            margin-right: 8px;
        }
        .chat-input .unity-text-field__label {
            display: none;
        }
        .chat-input .unity-text-field__input {
            color: var(--lakona-accent);
            -unity-font: var(--lakona-font);
            font-size: var(--lakona-font-size);
            background-color: var(--lakona-bg-input);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        .chat-input .unity-text-field__input:focus {
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
        }
        .send-button {
            width: 96px;
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-bg-base);
            background-color: var(--lakona-accent);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
        }
        .send-button:disabled {
            color: var(--lakona-accent-dim);
            background-color: var(--lakona-bg-input);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        """;
    }

    public static string RenderDefaultRuntimeTheme()
    {
        return """
        @import url("unity-theme://default");
        :root {
            --lakona-bg-base: #0A0F0A;
            --lakona-bg-panel: #0F1A0F;
            --lakona-bg-input: #050A0A;
            --lakona-bg-hover: #152015;
            --lakona-accent: #00FF66;
            --lakona-accent-dim: #00AA44;
            --lakona-accent-glow: #33FF88;
            --lakona-text-primary: #00FF66;
            --lakona-text-body: #88CC99;
            --lakona-text-dim: #448855;
            --lakona-text-system: #66AA77;
            --lakona-warning: #FFFF00;
            --lakona-error: #FF4444;
            --lakona-font: Consolas, "Courier New", monospace;
            --lakona-font-size: 14px;
            --lakona-font-size-title: 22px;
            --lakona-font-size-header: 18px;
            --lakona-font-size-system: 12px;
            --lakona-border-width: 2px;
        }
        """;
    }

    public static string RenderPanelSettingsAsset()
    {
        return $$"""
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!114 &11400000
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 0}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19101, guid: 0000000000000000e000000000000000, type: 0}
          m_Name: LakonaGameChatPanelSettings
          m_EditorClassIdentifier:
          themeUss: {fileID: -4733365628477956816, guid: {{RuntimeThemeGuid}}, type: 3}
          m_TargetTexture: {fileID: 0}
          m_ScaleMode: 1
          m_ReferenceSpritePixelsPerUnit: 100
          m_Scale: 1
          m_ReferenceDpi: 96
          m_FallbackDpi: 96
          m_ReferenceResolution: {x: 1200, y: 800}
          m_ScreenMatchMode: 0
          m_Match: 0
          m_SortingOrder: 0
          m_TargetDisplay: 0
          m_ClearDepthStencil: 1
          m_ClearColor: 0
          m_ColorClearValue: {r: 0, g: 0, b: 0, a: 0}
          textSettings: {fileID: 0}
        """;
    }

    public static string RenderLoginScene(TransportKind transport)
    {
        var defaultPath = transport == TransportKind.WebSocket ? "/ws" : string.Empty;
        return RenderSceneHeader() + $$"""
        --- !u!1 &217437972
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: 217437974}
          - component: {fileID: 217437975}
          - component: {fileID: 217437973}
          m_Layer: 0
          m_Name: Lakona.Game Login UI
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!114 &217437973
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 217437972}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: {{LoginUiGuid}}, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          _serverHost: 127.0.0.1
          _serverPort: 20000
          _serverPath: {{defaultPath}}
        --- !u!114 &217437975
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 217437972}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}
          m_Name:
          m_EditorClassIdentifier:
          m_PanelSettings: {fileID: 11400000, guid: {{PanelSettingsGuid}}, type: 2}
          m_ParentUI: {fileID: 0}
          sourceAsset: {fileID: 9197481963319205126, guid: {{LoginSceneUxmlGuid}}, type: 3}
          m_SortingOrder: 0
        --- !u!4 &217437974
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 217437972}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: 0}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 217437974}
        """;
    }

    public static string RenderChatScene()
    {
        return RenderSceneHeader() + $$"""
        --- !u!1 &317337972
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: 317337975}
          - component: {fileID: 317337974}
          - component: {fileID: 317337973}
          m_Layer: 0
          m_Name: Lakona.Game Chat UI
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!114 &317337973
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 317337972}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: {{ChatUiGuid}}, type: 3}
          m_Name:
          m_EditorClassIdentifier:
        --- !u!114 &317337974
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 317337972}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}
          m_Name:
          m_EditorClassIdentifier:
          m_PanelSettings: {fileID: 11400000, guid: {{PanelSettingsGuid}}, type: 2}
          m_ParentUI: {fileID: 0}
          sourceAsset: {fileID: 9197481963319205126, guid: {{ChatSceneUxmlGuid}}, type: 3}
          m_SortingOrder: 0
        --- !u!4 &317337975
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 317337972}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: 0}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 317337975}
        """;
    }

    public static string RenderEditorBuildSettings()
    {
        return $$"""
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!1045 &1
        EditorBuildSettings:
          m_ObjectHideFlags: 0
          serializedVersion: 2
          m_Scenes:
          - enabled: 1
            path: Assets/Scenes/LoginScene.unity
            guid: {{LoginSceneGuid}}
          - enabled: 1
            path: Assets/Scenes/ChatScene.unity
            guid: {{ChatSceneGuid}}
          m_configObjects: {}
        """;
    }

    private static string RenderSceneHeader()
    {
        return """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!29 &1
        OcclusionCullingSettings:
          m_ObjectHideFlags: 0
          serializedVersion: 2
          m_OcclusionBakeSettings:
            smallestOccluder: 5
            smallestHole: 0.25
            backfaceThreshold: 100
          m_SceneGUID: 00000000000000000000000000000000
          m_OcclusionCullingData: {fileID: 0}
        --- !u!104 &2
        RenderSettings:
          m_ObjectHideFlags: 0
          serializedVersion: 9
          m_Fog: 0
          m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
          m_FogMode: 3
          m_FogDensity: 0.01
          m_LinearFogStart: 0
          m_LinearFogEnd: 300
          m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
          m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
          m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
          m_AmbientIntensity: 1
          m_AmbientMode: 0
          m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
          m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
          m_HaloStrength: 0.5
          m_FlareStrength: 1
          m_FlareFadeSpeed: 3
          m_HaloTexture: {fileID: 0}
          m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
          m_DefaultReflectionMode: 0
          m_DefaultReflectionResolution: 128
          m_ReflectionBounces: 1
          m_ReflectionIntensity: 1
          m_CustomReflection: {fileID: 0}
          m_Sun: {fileID: 0}
          m_UseRadianceAmbientProbe: 0
        --- !u!157 &3
        LightmapSettings:
          m_ObjectHideFlags: 0
          serializedVersion: 12
          m_GIWorkflowMode: 1
          m_GISettings:
            serializedVersion: 2
            m_BounceScale: 1
            m_IndirectOutputScale: 1
            m_AlbedoBoost: 1
            m_EnvironmentLightingMode: 0
            m_EnableBakedLightmaps: 1
            m_EnableRealtimeLightmaps: 0
          m_LightingDataAsset: {fileID: 0}
          m_LightingSettings: {fileID: 0}
        --- !u!196 &4
        NavMeshSettings:
          serializedVersion: 2
          m_ObjectHideFlags: 0
          m_NavMeshData: {fileID: 0}

        """;
    }
}
