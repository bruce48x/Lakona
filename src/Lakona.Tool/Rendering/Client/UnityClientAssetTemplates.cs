using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class UnityClientAssetTemplates
{
    public const string GameClientGuid = "11a1f98ba46486884b824d248c98d6e3";
    public const string GameControllerGuid = "25a1b8c3d2e4f6a7b8c9d0e1f2a3b4cd";
    public const string GameUxmlGuid = "3a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d";
    public const string GameUssGuid = "4b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e";
    public const string InputActionsGuid = "6c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f";
    public const string GameSceneGuid = "57a244091a9bb4d7a9f119d19bc86c01";
    public const string LoginClientGuid = "1a1f98ba46486884b824d248c98d6e38";
    public const string ChatClientGuid = "fff9f5180f8be804a88038c0f7860779";
    public const string ChatSessionGuid = "c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6";
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
    public const string DefaultSceneLoaderGuid = "6d3dc0f7c4f3410e9e6ed70b8e18e8a8";

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

    public static string RenderInputActions()
    {
        return """
        {
          "name": "LakonaInputActions",
          "maps": [
            {
              "name": "Player",
              "id": "c66f74f2-f270-4d2c-8174-f856c173308b",
              "actions": [
                {
                  "name": "Move",
                  "type": "Value",
                  "id": "8f451bed-340a-4841-941d-2de211d8ff35",
                  "expectedControlType": "Vector2",
                  "processors": "",
                  "interactions": "",
                  "initialStateCheck": true
                }
              ],
              "bindings": [
                {
                  "name": "",
                  "id": "5713188c-80b0-40df-822d-bc1b54b81b82",
                  "path": "<Gamepad>/leftStick",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": false
                },
                {
                  "name": "WASD",
                  "id": "235307da-ae68-4898-acb2-4fc6de69a9ce",
                  "path": "Dpad",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": true,
                  "isPartOfComposite": false
                },
                {
                  "name": "up",
                  "id": "a5612595-a421-4c9d-b089-e6e6cdce49f7",
                  "path": "<Keyboard>/w",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "down",
                  "id": "2df48f76-c1bf-438f-8c66-c5fd0e2dc1f1",
                  "path": "<Keyboard>/s",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "left",
                  "id": "eac8a9a0-6e79-4e35-8a21-71833dd908b3",
                  "path": "<Keyboard>/a",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "right",
                  "id": "538bc295-e566-4304-b11a-915bdac5e639",
                  "path": "<Keyboard>/d",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "Arrows",
                  "id": "9084b649-3817-4235-9489-41c5759d2aac",
                  "path": "Dpad",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": true,
                  "isPartOfComposite": false
                },
                {
                  "name": "up",
                  "id": "6f1f990e-100a-4100-af98-085784ae0a52",
                  "path": "<Keyboard>/upArrow",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "down",
                  "id": "3b42d60f-2417-430d-915a-6fb0dad026df",
                  "path": "<Keyboard>/downArrow",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "left",
                  "id": "a0a33689-f3f5-4c7a-8124-b1f4891623f3",
                  "path": "<Keyboard>/leftArrow",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                },
                {
                  "name": "right",
                  "id": "9b3dd172-1d60-4096-b91c-47e3b38e9510",
                  "path": "<Keyboard>/rightArrow",
                  "interactions": "",
                  "processors": "",
                  "groups": "",
                  "action": "Move",
                  "isComposite": false,
                  "isPartOfComposite": true
                }
              ]
            }
          ],
          "controlSchemes": []
        }
        """;
    }

    public static string RenderInputActionsMeta()
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{InputActionsGuid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 11500000, guid: 8404be70184654265930450def6a9037, type: 3}
          generateWrapperCode: 0
          wrapperCodePath:
          wrapperClassName:
          wrapperCodeNamespace:
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

    public static string RenderGameUxml()
    {
        return """
        <ui:UXML xmlns:ui="UnityEngine.UIElements">
          <Style src="Game.uss" />
          <ui:VisualElement class="root">
            <ui:VisualElement name="arena-view" class="arena-view" />
            <ui:VisualElement class="online-pill">
              <ui:VisualElement class="online-dot" />
              <ui:Label text="ONLINE" class="online-label" />
            </ui:VisualElement>
            <ui:VisualElement name="login-panel" class="login-panel">
              <ui:VisualElement class="title-block">
                <ui:Label text="LAKONA" class="title title-primary" />
                <ui:Label text="ARENA" class="title title-accent" />
              </ui:VisualElement>
              <ui:VisualElement class="callsign-heading">
                <ui:VisualElement class="heading-rule" />
                <ui:Label text="CHOOSE YOUR CALLSIGN" class="callsign-label" />
                <ui:VisualElement class="heading-rule" />
              </ui:VisualElement>
              <ui:VisualElement class="login-action">
                <ui:TextField name="name-field" label="CALLSIGN" max-length="20" class="name-field" />
                <ui:Button name="connect-button" text="PLAY NOW" class="connect-button" />
              </ui:VisualElement>
              <ui:Label name="status-label" text="Enter a name to join." class="status" />
            </ui:VisualElement>
            <ui:VisualElement name="hud" class="hud">
              <ui:VisualElement class="player-badge"><ui:VisualElement class="player-core" /></ui:VisualElement>
              <ui:VisualElement class="identity-group">
                <ui:Label name="player-label" text="LAKONA_01" class="player-name" />
                <ui:Label name="score-label" text="SCORE 12,540" class="score-label" />
              </ui:VisualElement>
              <ui:VisualElement class="hud-divider" />
              <ui:VisualElement class="health-group">
                <ui:Label name="health-label" text="HEALTH 100 / 100" class="metric-label" />
                <ui:VisualElement class="health-track"><ui:VisualElement name="health-fill" class="health-fill" /></ui:VisualElement>
              </ui:VisualElement>
              <ui:VisualElement class="hud-divider" />
              <ui:VisualElement class="controls-group">
                <ui:VisualElement class="key-row">
                  <ui:Label text="W" class="key-chip" /><ui:Label text="A" class="key-chip" /><ui:Label text="S" class="key-chip" /><ui:Label text="D" class="key-chip" />
                </ui:VisualElement>
                <ui:Label text="MOVE · AUTO FIRE" class="hint" />
              </ui:VisualElement>
            </ui:VisualElement>
          </ui:VisualElement>
        </ui:UXML>
        """;
    }

    public static string RenderGameUss()
    {
        return """
        .root {
            flex-grow: 1;
            background-color: rgba(13, 15, 15, 0);
            color: rgb(244, 241, 226);
        }

        .arena-view {
            position: absolute;
            left: 0;
            top: 0;
            right: 0;
            bottom: 0;
        }

        .login-panel {
            position: absolute;
            width: 820px;
            height: 500px;
            left: 50%;
            top: 50%;
            translate: -410px -240px;
            align-items: center;
            justify-content: center;
        }

        .online-pill {
            position: absolute;
            left: 30px;
            top: 26px;
            height: 34px;
            flex-direction: row;
            align-items: center;
            border-bottom-width: 2px;
            border-bottom-color: rgb(190, 226, 28);
        }
        .online-dot { width: 14px; height: 14px; margin-right: 10px; border-radius: 7px; background-color: rgb(190, 226, 28); }
        .online-label { font-size: 18px; -unity-font-style: bold; color: rgb(190, 226, 28); letter-spacing: 2px; }
        .title-block { align-items: center; margin-bottom: 24px; }
        .title { -unity-font-style: bold; -unity-text-align: middle-center; letter-spacing: 8px; }
        .title-primary { height: 90px; font-size: 82px; color: rgb(244, 241, 226); }
        .title-accent { height: 76px; margin-top: -16px; font-size: 70px; color: rgb(190, 226, 28); }
        .callsign-heading { width: 620px; margin-bottom: 12px; flex-direction: row; align-items: center; }
        .heading-rule { height: 2px; flex-grow: 1; background-color: rgb(190, 226, 28); opacity: 0.8; }
        .callsign-label { margin-left: 14px; margin-right: 14px; color: rgb(244, 241, 226); -unity-font-style: bold; letter-spacing: 2px; }
        .login-action { width: 700px; height: 76px; flex-direction: row; }
        .name-field { width: 460px; height: 76px; color: rgb(244, 241, 226); }
        .name-field > .unity-text-field__label { display: none; }
        .name-field .unity-text-field__input {
            height: 76px;
            padding-left: 22px;
            font-size: 24px;
            -unity-font-style: bold;
            color: rgb(244, 241, 226);
            background-color: rgba(10, 12, 12, 0.96);
            border-left-width: 3px;
            border-top-width: 3px;
            border-bottom-width: 3px;
            border-right-width: 0;
            border-left-color: rgb(190, 226, 28);
            border-top-color: rgb(190, 226, 28);
            border-bottom-color: rgb(190, 226, 28);
        }
        .connect-button {
            width: 240px;
            height: 76px;
            font-size: 25px;
            color: rgb(244, 241, 226);
            background-color: rgb(255, 76, 64);
            border-left-width: 0;
            border-right-width: 3px;
            border-top-width: 3px;
            border-bottom-width: 3px;
            border-right-color: rgb(244, 241, 226);
            border-top-color: rgb(244, 241, 226);
            border-bottom-color: rgb(244, 241, 226);
            -unity-font-style: bold;
            letter-spacing: 2px;
        }
        .connect-button:hover { background-color: rgb(255, 101, 83); }
        .status { margin-top: 14px; min-height: 24px; color: rgb(244, 241, 226); letter-spacing: 1px; }

        .compact .login-panel { height: 430px; translate: -410px -205px; }
        .compact .title-block { margin-bottom: 12px; }
        .compact .title-primary { height: 70px; font-size: 62px; }
        .compact .title-accent { height: 58px; margin-top: -12px; font-size: 52px; }
        .compact .callsign-heading { margin-bottom: 8px; }
        .compact .login-action { height: 66px; }
        .compact .name-field { height: 66px; }
        .compact .name-field .unity-text-field__input { height: 66px; }
        .compact .connect-button { height: 66px; }
        .compact .status { margin-top: 8px; }

        .hud {
            display: none;
            position: absolute;
            left: 32px;
            right: 32px;
            bottom: 24px;
            height: 94px;
            padding-left: 22px;
            padding-right: 22px;
            flex-direction: row;
            align-items: center;
            background-color: rgba(10, 12, 12, 0.96);
            border-left-width: 2px;
            border-right-width: 2px;
            border-top-width: 2px;
            border-bottom-width: 2px;
            border-left-color: rgb(190, 226, 28);
            border-right-color: rgb(190, 226, 28);
            border-top-color: rgb(190, 226, 28);
            border-bottom-color: rgb(190, 226, 28);
        }

        .player-badge { width: 58px; height: 58px; margin-right: 16px; align-items: center; justify-content: center; border-radius: 29px; border-left-width: 3px; border-right-width: 3px; border-top-width: 3px; border-bottom-width: 3px; border-left-color: rgb(190, 226, 28); border-right-color: rgb(190, 226, 28); border-top-color: rgb(190, 226, 28); border-bottom-color: rgb(190, 226, 28); }
        .player-core { width: 28px; height: 28px; border-radius: 14px; background-color: rgb(190, 226, 28); }
        .identity-group { width: 210px; }
        .player-name { font-size: 19px; color: rgb(190, 226, 28); -unity-font-style: bold; letter-spacing: 1px; }
        .score-label { margin-top: 3px; font-size: 17px; color: rgb(244, 241, 226); -unity-font-style: bold; }
        .hud-divider { width: 1px; height: 58px; margin-left: 18px; margin-right: 24px; background-color: rgb(88, 91, 84); }
        .health-group { flex-grow: 1; max-width: 430px; }
        .metric-label { margin-bottom: 8px; color: rgb(244, 241, 226); -unity-font-style: bold; letter-spacing: 1px; }
        .health-track { height: 20px; background-color: rgb(54, 57, 53); }
        .health-fill { width: 100%; height: 20px; background-color: rgb(190, 226, 28); }
        .controls-group { width: 260px; align-items: center; }
        .key-row { flex-direction: row; }
        .key-chip { width: 34px; height: 30px; margin-left: 3px; margin-right: 3px; -unity-text-align: middle-center; color: rgb(244, 241, 226); border-left-width: 1px; border-right-width: 1px; border-top-width: 1px; border-bottom-width: 1px; border-left-color: rgb(244, 241, 226); border-right-color: rgb(244, 241, 226); border-top-color: rgb(244, 241, 226); border-bottom-color: rgb(244, 241, 226); border-radius: 4px; -unity-font-style: bold; }
        .hint { margin-top: 5px; color: rgb(190, 226, 28); -unity-font-style: bold; letter-spacing: 1px; }
        """;
    }

    public static string RenderGameScene(TransportKind transport)
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
          m_Name: Lakona Arena Game
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
          m_Script: {fileID: 11500000, guid: {{GameControllerGuid}}, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          _serverHost: 127.0.0.1
          _serverPort: 20000
          _serverPath: {{defaultPath}}
          _inputActions: {fileID: -944628639613478452, guid: {{InputActionsGuid}}, type: 3}
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
          sourceAsset: {fileID: 9197481963319205126, guid: {{GameUxmlGuid}}, type: 3}
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
        {{RenderMainCamera(217438972, 217438973, 217438974, 217438975)}}
        {{RenderInputSystemEventSystem(217439972, 217439973, 217439974, 217439975)}}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 217437974}
          - {fileID: 217438973}
          - {fileID: 217439975}
        """;
    }

    public static string RenderGameEditorBuildSettings()
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
            path: Assets/Scenes/Game.unity
            guid: {{GameSceneGuid}}
          m_configObjects: {}
        """;
    }

    public static string RenderPlayerSettings(string productName)
    {
        var yamlProductName = productName.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!129 &1
        PlayerSettings:
          m_ObjectHideFlags: 0
          serializedVersion: 26
          companyName: Lakona
          productName: '{{yamlProductName}}'
          defaultScreenWidth: 800
          defaultScreenHeight: 600
          defaultIsNativeResolution: 0
          runInBackground: 1
          forceSingleInstance: 0
          useFlipModelSwapchain: 1
          resizableWindow: 1
          visibleInBackground: 1
          allowFullscreenSwitch: 1
          fullscreenMode: 3
          activeInputHandler: 1
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
                <ui:ScrollView name="message-list" class="message-list">
                    <ui:Label text="Open this scene from LoginScene after connecting." name="chat-empty-state" class="chat-empty-state" />
                </ui:ScrollView>
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
            background-color: #0A0F0A;
            align-items: center;
            justify-content: center;
        }
        .login-panel {
            width: 360px;
            padding: 32px 24px;
            background-color: #0F1A0F;
            border-left-width: 2px;
            border-right-width: 2px;
            border-top-width: 2px;
            border-bottom-width: 2px;
            border-left-color: #00FF66;
            border-right-color: #00FF66;
            border-top-color: #00FF66;
            border-bottom-color: #00FF66;
        }
        .login-title {
            font-size: 22px;
            color: #00FF66;
            margin-bottom: 20px;
        }
        .name-label {
            font-size: 14px;
            color: #00AA44;
            margin-bottom: 4px;
        }
        .name-field {
            margin-bottom: 16px;
        }
        .name-field .unity-text-field__label {
            color: #00AA44;
        }
        .name-field .unity-text-field__input {
            color: #00FF66;
            font-size: 14px;
            background-color: #050A0A;
            border-top-width: 2px;
            border-right-width: 2px;
            border-bottom-width: 2px;
            border-left-width: 2px;
            border-top-color: #00AA44;
            border-right-color: #00AA44;
            border-bottom-color: #00AA44;
            border-left-color: #00AA44;
        }
        .name-field .unity-text-field__input:focus {
            border-top-color: #00FF66;
            border-right-color: #00FF66;
            border-bottom-color: #00FF66;
            border-left-color: #00FF66;
        }
        .connect-button {
            width: 100%;
            font-size: 14px;
            color: #0A0F0A;
            background-color: #00FF66;
            border-top-width: 2px;
            border-right-width: 2px;
            border-bottom-width: 2px;
            border-left-width: 2px;
            border-top-color: #00FF66;
            border-right-color: #00FF66;
            border-bottom-color: #00FF66;
            border-left-color: #00FF66;
            margin-bottom: 12px;
        }
        .connect-button:disabled {
            color: #00AA44;
            background-color: #050A0A;
            border-top-color: #00AA44;
            border-right-color: #00AA44;
            border-bottom-color: #00AA44;
            border-left-color: #00AA44;
        }
        .status-label {
            font-size: 14px;
            color: #FF4444;
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
            background-color: #0A0F0A;
        }
        .chat-header {
            flex-direction: row;
            align-items: center;
            flex-shrink: 0;
            padding: 8px 16px;
            background-color: #0F1A0F;
            border-bottom-width: 2px;
            border-bottom-color: #00FF66;
        }
        .header-title {
            font-size: 18px;
            color: #00FF66;
            flex-grow: 1;
        }
        .header-count {
            font-size: 14px;
            color: #FFFF00;
        }
        .message-list {
            flex-grow: 1;
            min-height: 160px;
            padding: 8px 16px;
        }
        .chat-empty-state {
            font-size: 14px;
            color: #66AA77;
            white-space: normal;
        }
        .chat-message {
            font-size: 14px;
            color: #88CC99;
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-system {
            font-size: 12px;
            color: #66AA77;
            -unity-font-style: italic;
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-footer {
            flex-direction: row;
            align-items: center;
            flex-shrink: 0;
            padding: 8px 16px;
            background-color: #0F1A0F;
            border-top-width: 2px;
            border-top-color: #00FF66;
        }
        .message-label {
            font-size: 14px;
            color: #00AA44;
            margin-right: 8px;
            flex-shrink: 0;
        }
        .chat-input {
            flex-grow: 1;
            flex-shrink: 1;
            min-width: 0;
            margin-right: 8px;
        }
        .chat-input .unity-text-field__label {
            display: none;
        }
        .chat-input .unity-text-field__input {
            color: #00FF66;
            font-size: 14px;
            background-color: #050A0A;
            border-top-width: 2px;
            border-right-width: 2px;
            border-bottom-width: 2px;
            border-left-width: 2px;
            border-top-color: #00AA44;
            border-right-color: #00AA44;
            border-bottom-color: #00AA44;
            border-left-color: #00AA44;
        }
        .chat-input .unity-text-field__input:focus {
            border-top-color: #00FF66;
            border-right-color: #00FF66;
            border-bottom-color: #00FF66;
            border-left-color: #00FF66;
        }
        .send-button {
            width: 96px;
            min-width: 96px;
            flex-shrink: 0;
            font-size: 14px;
            color: #0A0F0A;
            background-color: #00FF66;
            border-top-width: 2px;
            border-right-width: 2px;
            border-bottom-width: 2px;
            border-left-width: 2px;
            border-top-color: #00FF66;
            border-right-color: #00FF66;
            border-bottom-color: #00FF66;
            border-left-color: #00FF66;
        }
        .send-button:disabled {
            color: #00AA44;
            background-color: #050A0A;
            border-top-color: #00AA44;
            border-right-color: #00AA44;
            border-bottom-color: #00AA44;
            border-left-color: #00AA44;
        }
        """;
    }

    public static string RenderDefaultRuntimeTheme()
    {
        return """
        @import url("unity-theme://default");
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
          m_Name: LakonaGamePanelSettings
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
        {{RenderMainCamera(217438972, 217438973, 217438974, 217438975)}}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 217437974}
          - {fileID: 217438973}
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
        {{RenderMainCamera(317338972, 317338973, 317338974, 317338975)}}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 317337975}
          - {fileID: 317338973}
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

    private static string RenderInputSystemEventSystem(int gameObjectId, int inputModuleId, int eventSystemId, int transformId)
    {
        return $$"""
        --- !u!1 &{{gameObjectId}}
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: {{transformId}}}
          - component: {fileID: {{inputModuleId}}}
          - component: {fileID: {{eventSystemId}}}
          m_Layer: 0
          m_Name: EventSystem
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!114 &{{inputModuleId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: 01614664b831546d2ae94a42149d80ac, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          m_MoveRepeatDelay: 0.5
          m_MoveRepeatRate: 0.1
          m_ActionsAsset: {fileID: 0}
          m_PointAction: {fileID: 0}
          m_MoveAction: {fileID: 0}
          m_SubmitAction: {fileID: 0}
          m_CancelAction: {fileID: 0}
          m_LeftClickAction: {fileID: 0}
          m_MiddleClickAction: {fileID: 0}
          m_RightClickAction: {fileID: 0}
          m_ScrollWheelAction: {fileID: 0}
          m_TrackedDevicePositionAction: {fileID: 0}
          m_TrackedDeviceOrientationAction: {fileID: 0}
          m_DeselectOnBackgroundClick: 1
          m_PointerBehavior: 0
          m_CursorLockBehavior: 0
        --- !u!114 &{{eventSystemId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: 76c392e42b5098c458856cdf6ecaaaa1, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          m_FirstSelected: {fileID: 0}
          m_sendNavigationEvents: 1
          m_DragThreshold: 10
        --- !u!4 &{{transformId}}
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: 0}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        """;
    }

    private static string RenderMainCamera(int gameObjectId, int transformId, int cameraId, int audioListenerId)
    {
        return $$"""
        --- !u!1 &{{gameObjectId}}
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: {{transformId}}}
          - component: {fileID: {{cameraId}}}
          - component: {fileID: {{audioListenerId}}}
          m_Layer: 0
          m_Name: Main Camera
          m_TagString: MainCamera
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!4 &{{transformId}}
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 1, z: -10}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        --- !u!20 &{{cameraId}}
        Camera:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          serializedVersion: 2
          m_ClearFlags: 1
          m_BackGroundColor: {r: 0.039215688, g: 0.05882353, b: 0.039215688, a: 0}
          m_projectionMatrixMode: 1
          m_GateFitMode: 2
          m_FOVAxisMode: 0
          m_Iso: 200
          m_ShutterSpeed: 0.005
          m_Aperture: 16
          m_FocusDistance: 10
          m_FocalLength: 50
          m_BladeCount: 5
          m_Curvature: {x: 2, y: 11}
          m_BarrelClipping: 0.25
          m_Anamorphism: 0
          m_SensorSize: {x: 36, y: 24}
          m_LensShift: {x: 0, y: 0}
          m_NormalizedViewPortRect:
            serializedVersion: 2
            x: 0
            y: 0
            width: 1
            height: 1
          near clip plane: 0.3
          far clip plane: 1000
          field of view: 60
          orthographic: 0
          orthographic size: 5
          m_Depth: -1
          m_CullingMask:
            serializedVersion: 2
            m_Bits: 4294967295
          m_RenderingPath: -1
          m_TargetTexture: {fileID: 0}
          m_TargetDisplay: 0
          m_TargetEye: 3
          m_HDR: 1
          m_AllowMSAA: 1
          m_AllowDynamicResolution: 0
          m_ForceIntoRT: 0
          m_OcclusionCulling: 1
          m_StereoConvergence: 10
          m_StereoSeparation: 0.022
        --- !u!81 &{{audioListenerId}}
        AudioListener:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
        """;
    }
}
