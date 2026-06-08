internal static class UnityAssetTemplates
{
    public static string RenderUnityMonoScriptMeta(string guid)
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

    public static string RenderUnityUxmlMeta(string guid)
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

    public static string RenderUnityUssMeta(string guid)
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

    public static string RenderUnityTssMeta(string guid)
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

    public static string RenderUnityNativeAssetMeta(string guid)
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

    public static string RenderUnityDefaultRuntimeTheme()
    {
        return """
        @import url("unity-theme://default");
        """;
    }

    public static string RenderUnityPanelSettingsAsset(string defaultRuntimeThemeGuid)
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
          themeUss: {fileID: -4733365628477956816, guid: {{defaultRuntimeThemeGuid}}, type: 3}
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
          m_DynamicAtlasSettings:
            m_MinAtlasSize: 64
            m_MaxAtlasSize: 4096
            m_MaxSubTextureSize: 64
            m_ActiveFilters: 31
          m_AtlasBlitShader: {fileID: 9101, guid: 0000000000000000f000000000000000, type: 0}
          m_RuntimeShader: {fileID: 9100, guid: 0000000000000000f000000000000000, type: 0}
          m_RuntimeWorldShader: {fileID: 9102, guid: 0000000000000000f000000000000000, type: 0}
          textSettings: {fileID: 0}
        """;
    }

    public static string RenderUnityChatSceneObjects(
        long gameObjectId,
        long chatUiComponentId,
        long uiDocumentComponentId,
        long transformId,
        string chatUiScriptGuid,
        string uxmlGuid,
        string panelSettingsGuid,
        string serverPath = "")
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
          - component: {fileID: {{uiDocumentComponentId}}}
          - component: {fileID: {{chatUiComponentId}}}
          m_Layer: 0
          m_Name: Lakona.Game Chat UI
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!114 &{{chatUiComponentId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: {{chatUiScriptGuid}}, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          _serverHost: 127.0.0.1
          _serverPort: 20000
          _serverPath: {{serverPath}}
        --- !u!114 &{{uiDocumentComponentId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}
          m_Name:
          m_EditorClassIdentifier:
          m_PanelSettings: {fileID: 11400000, guid: {{panelSettingsGuid}}, type: 2}
          m_ParentUI: {fileID: 0}
          sourceAsset: {fileID: 9197481963319205126, guid: {{uxmlGuid}}, type: 3}
          m_SortingOrder: 0
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

    public static string RenderUnityNuGetPackageImportGuard()
    {
        return """
        #if UNITY_EDITOR
        using System;
        using UnityEditor;

        [InitializeOnLoad]
        internal sealed class LakonaGameNuGetPackageImportGuard : AssetPostprocessor
        {
            static LakonaGameNuGetPackageImportGuard()
            {
                EditorApplication.delayCall += DisableExistingAnalyzerPlugins;
            }

            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                foreach (var assetPath in importedAssets)
                {
                    DisableAnalyzerPlugin(assetPath);
                }

                foreach (var assetPath in movedAssets)
                {
                    DisableAnalyzerPlugin(assetPath);
                }
            }

            private static void DisableExistingAnalyzerPlugins()
            {
                var pluginGuids = AssetDatabase.FindAssets("t:PluginImporter", new[] { "Assets/Packages" });
                foreach (var guid in pluginGuids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    DisableAnalyzerPlugin(assetPath);
                }
            }

            private static void DisableAnalyzerPlugin(string assetPath)
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.IndexOf("Assets/Packages/", StringComparison.OrdinalIgnoreCase) < 0 ||
                    normalizedPath.IndexOf("/analyzers/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    return;
                }

                if (!importer.GetCompatibleWithAnyPlatform() && !importer.GetCompatibleWithEditor())
                {
                    return;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SaveAndReimport();
            }
        }
        #endif
        """;
    }
}
