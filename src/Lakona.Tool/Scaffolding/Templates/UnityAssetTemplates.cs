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

    public static string RenderUnitySceneHeader()
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
          m_LightmapEditorSettings:
            serializedVersion: 12
            m_Resolution: 2
            m_BakeResolution: 40
            m_AtlasSize: 1024
            m_AO: 0
            m_AOMaxDistance: 1
            m_CompAOExponent: 1
            m_CompAOExponentDirect: 0
            m_ExtractAmbientOcclusion: 0
            m_Padding: 2
            m_LightmapParameters: {fileID: 0}
            m_LightmapsBakeMode: 1
            m_TextureCompression: 1
            m_FinalGather: 0
            m_FinalGatherFiltering: 1
            m_FinalGatherRayCount: 256
            m_ReflectionCompression: 2
            m_MixedBakeMode: 2
            m_BakeBackend: 1
            m_PVRSampling: 1
            m_PVRDirectSampleCount: 32
            m_PVRSampleCount: 512
            m_PVRBounces: 2
            m_PVREnvironmentSampleCount: 256
            m_PVREnvironmentReferencePointCount: 2048
            m_PVRFilteringMode: 1
            m_PVRDenoiserTypeDirect: 1
            m_PVRDenoiserTypeIndirect: 1
            m_PVRDenoiserTypeAO: 1
            m_PVRFilterTypeDirect: 0
            m_PVRFilterTypeIndirect: 0
            m_PVRFilterTypeAO: 0
            m_PVREnvironmentMIS: 1
            m_PVRCulling: 1
            m_PVRFilteringGaussRadiusDirect: 1
            m_PVRFilteringGaussRadiusIndirect: 5
            m_PVRFilteringGaussRadiusAO: 2
            m_PVRFilteringAtrousPositionSigmaDirect: 0.5
            m_PVRFilteringAtrousPositionSigmaIndirect: 2
            m_PVRFilteringAtrousPositionSigmaAO: 1
            m_ExportTrainingData: 0
            m_TrainingDataDestination: TrainingData
            m_LightProbeSampleCountMultiplier: 4
          m_LightingDataAsset: {fileID: 0}
          m_LightingSettings: {fileID: 0}
        --- !u!196 &4
        NavMeshSettings:
          serializedVersion: 2
          m_ObjectHideFlags: 0
          m_BuildSettings:
            serializedVersion: 3
            agentTypeID: 0
            agentRadius: 0.5
            agentHeight: 2
            agentSlope: 45
            agentClimb: 0.4
            ledgeDropHeight: 0
            maxJumpAcrossDistance: 0
            minRegionArea: 2
            manualCellSize: 0
            cellSize: 0.16666667
            manualTileSize: 0
            tileSize: 256
            buildHeightMesh: 0
            maxJobWorkers: 0
            preserveTilesOutsideBounds: 0
            debug:
              m_Flags: 0
          m_NavMeshData: {fileID: 0}

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
