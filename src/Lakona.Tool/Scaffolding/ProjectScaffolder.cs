internal sealed class ProjectScaffolder
{
    private const string UnityChatUiScriptGuid = "462a8730535800d4a801000623f4450e";
    private const string UnityChatSceneUxmlGuid = "d8e055cb54604094cb41badb6b3866f6";
    private const string UnityChatSceneUssGuid = "f7e09962267bcef45a558136fb62bb68";
    private const string UnityChatPanelSettingsGuid = "0c8089bab5856fe4d8f88e6f526fd306";
    private const string UnityDefaultRuntimeThemeGuid = "9a59d5efd84abc44da5e32a04db78d26";
    private const string UnityLoginUiScriptGuid = "5a1b8c3d2e4f6a7b8c9d0e1f2a3b4c5d";
    private const string UnityLoginSceneUxmlGuid = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
    private const string UnityLoginSceneUssGuid = "b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7";

    public async Task AugmentProjectWithLakonaGameAsync(string projectRoot, NewCommandOptions options)
    {
        EnsureStarterServerProjectDirectory(projectRoot);
        await WriteClientPackageReferenceAsync(projectRoot, options).ConfigureAwait(false);
        await WriteClientChatFilesAsync(projectRoot, options).ConfigureAwait(false);
        await WriteSharedHotfixBoundaryFilesAsync(projectRoot, options).ConfigureAwait(false);
        await WriteServerSolutionAsync(projectRoot).ConfigureAwait(false);
        await WriteServerProgramAsync(projectRoot, options).ConfigureAwait(false);
        await WriteGeneratedServerApplicationAsync(projectRoot, options).ConfigureAwait(false);
        await WriteServerProjectAsync(projectRoot, options).ConfigureAwait(false);
        await WriteHotfixProjectAsync(projectRoot).ConfigureAwait(false);
        await WriteHotfixBoundaryFilesAsync(projectRoot).ConfigureAwait(false);
        await WriteServerAppSettingsAsync(projectRoot, options).ConfigureAwait(false);
        await WriteServerConfiguratorsAsync(projectRoot, options).ConfigureAwait(false);
        await WriteServerChatFilesAsync(projectRoot).ConfigureAwait(false);
        await WriteOperationsScaffoldingAsync(projectRoot, options).ConfigureAwait(false);
    }

    private static Task WriteClientPackageReferenceAsync(string projectRoot, NewCommandOptions options)
    {
        return ProjectConventions.IsGodot(options.ClientEngine)
            ? WriteGodotClientPackageReferenceAsync(projectRoot)
            : WriteUnityClientPackageReferenceAsync(projectRoot);
    }

    private static async Task WriteGodotClientPackageReferenceAsync(string projectRoot)
    {
        var clientDirectory = Path.Combine(projectRoot, "Client");
        if (!Directory.Exists(clientDirectory))
        {
            return;
        }

        var projectFiles = Directory.EnumerateFiles(clientDirectory, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
        if (projectFiles.Length == 0)
        {
            return;
        }

        if (projectFiles.Length > 1)
        {
            throw new InvalidOperationException($"Multiple client project files were found in: {clientDirectory}");
        }

        var path = projectFiles[0];
        var document = System.Xml.Linq.XDocument.Load(path);
        var project = document.Root ?? throw new InvalidOperationException($"Invalid project file: {path}");

        ProjectXmlMutator.EnsurePackageReference(project, "Lakona.Game.Client", ToolPackageVersions.LakonaGameClient);

        await ToolFileWriter.WriteTextAsync(path, document.ToString()).ConfigureAwait(false);
    }

    private static Task WriteSharedHotfixBoundaryFilesAsync(string projectRoot, NewCommandOptions options)
    {
        return Task.WhenAll(
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Shared", "Contracts", "RpcContractIds.cs"),
                ToolTemplates.RenderSharedRpcContractIds()),
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatProtocols.cs"),
                ToolTemplates.RenderSharedChatProtocols()),
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatMessages.cs"),
                ToolTemplates.RenderSharedChatMessages(options)));
    }

    private static async Task WriteUnityClientPackageReferenceAsync(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "Client", "Assets", "packages.config");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);

        System.Xml.Linq.XDocument document;
        if (File.Exists(path))
        {
            document = System.Xml.Linq.XDocument.Load(path);
        }
        else
        {
            document = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XDeclaration("1.0", "utf-8", null),
                new System.Xml.Linq.XElement("packages"));
        }

        var packages = document.Root ?? throw new InvalidOperationException($"Invalid packages.config file: {path}");
        if (!string.Equals(packages.Name.LocalName, "packages", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid packages.config root element in: {path}");
        }

        ProjectXmlMutator.EnsureNuGetForUnityPackage(packages, "Lakona.Game.Client", ToolPackageVersions.LakonaGameClient);
        ProjectXmlMutator.EnsureNuGetForUnityPackage(packages, "Lakona.Game.Abstractions", ToolPackageVersions.LakonaGameAbstractions);

        await ToolFileWriter.WriteTextAsync(path, document.ToString()).ConfigureAwait(false);
        await WriteUnityNuGetPackageImportGuardAsync(projectRoot).ConfigureAwait(false);
    }

    private static Task WriteUnityNuGetPackageImportGuardAsync(string projectRoot)
    {
        return WriteIfMissingAsync(
            Path.Combine(projectRoot, "Client", "Assets", "Editor", "LakonaGameNuGetPackageImportGuard.cs"),
            ToolTemplates.RenderUnityNuGetPackageImportGuard());
    }

    private static async Task WriteClientChatFilesAsync(string projectRoot, NewCommandOptions options)
    {
        if (ProjectConventions.IsGodot(options.ClientEngine))
        {
            await Task.WhenAll(
                WriteIfMissingAsync(
                    Path.Combine(projectRoot, "Client", "Scripts", "Chat", "ChatClient.cs"),
                    ToolTemplates.RenderClientChatClient()),
                WriteIfMissingAsync(
                    Path.Combine(projectRoot, "Client", "Scripts", "Chat", "ChatSession.cs"),
                    ToolTemplates.RenderGodotChatSession()),
                WriteIfMissingAsync(
                    Path.Combine(projectRoot, "Client", "Scripts", "Login", "LoginScene.cs"),
                    ToolTemplates.RenderGodotLoginScene(options)),
                WriteIfMissingAsync(
                    Path.Combine(projectRoot, "Client", "Scripts", "Chat", "ChatScene.cs"),
                    ToolTemplates.RenderGodotChatScene()),
                WriteAsync(
                    Path.Combine(projectRoot, "Client", "Login.tscn"),
                    ToolTemplates.RenderGodotLoginTscn()),
                WriteAsync(
                    Path.Combine(projectRoot, "Client", "Chat.tscn"),
                    ToolTemplates.RenderGodotChatTscn())).ConfigureAwait(false);

            await PatchGodotProjectForAutoloadAsync(projectRoot).ConfigureAwait(false);
            await PatchGodotMainSceneAsync(projectRoot).ConfigureAwait(false);
            return;
        }

        var loginUiPath = Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Login", "LoginUI.cs");
        var chatSessionPath = Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatSession.cs");
        var chatUiPath = Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatUI.cs");
        var loginUxmlPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "LoginScene.uxml");
        var loginUssPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "LoginScene.uss");
        var chatUxmlPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "ChatScene.uxml");
        var chatUssPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "ChatScene.uss");
        var panelSettingsPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "LakonaGameChatPanelSettings.asset");
        var runtimeThemePath = Path.Combine(
            projectRoot,
            "Client",
            "Assets",
            "UI Toolkit",
            "UnityThemes",
            "UnityDefaultRuntimeTheme.tss");

        await Task.WhenAll(
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatClient.cs"),
                ToolTemplates.RenderClientChatClient()),
            WriteIfMissingAsync(
                chatSessionPath,
                ToolTemplates.RenderChatSession()),
            WriteIfMissingAsync(
                loginUiPath,
                ToolTemplates.RenderUnityLoginUI(options)),
            WriteIfMissingAsync(
                chatUiPath,
                ToolTemplates.RenderClientChatUI()),
            WriteIfMissingAsync(
                loginUxmlPath,
                ToolTemplates.RenderUnityLoginUxml()),
            WriteIfMissingAsync(
                loginUssPath,
                ToolTemplates.RenderUnityLoginUss()),
            WriteIfMissingAsync(
                chatUxmlPath,
                ToolTemplates.RenderClientChatUxml()),
            WriteIfMissingAsync(
                chatUssPath,
                ToolTemplates.RenderClientChatUss()),
            WriteIfMissingAsync(
                loginUiPath + ".meta",
                ToolTemplates.RenderUnityMonoScriptMeta(UnityLoginUiScriptGuid)),
            WriteIfMissingAsync(
                chatSessionPath + ".meta",
                ToolTemplates.RenderUnityMonoScriptMeta("c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6")),
            WriteIfMissingAsync(
                chatUiPath + ".meta",
                ToolTemplates.RenderUnityMonoScriptMeta(UnityChatUiScriptGuid)),
            WriteIfMissingAsync(
                loginUxmlPath + ".meta",
                ToolTemplates.RenderUnityUxmlMeta(UnityLoginSceneUxmlGuid)),
            WriteIfMissingAsync(
                loginUssPath + ".meta",
                ToolTemplates.RenderUnityUssMeta(UnityLoginSceneUssGuid)),
            WriteIfMissingAsync(
                chatUxmlPath + ".meta",
                ToolTemplates.RenderUnityUxmlMeta(UnityChatSceneUxmlGuid)),
            WriteIfMissingAsync(
                chatUssPath + ".meta",
                ToolTemplates.RenderUnityUssMeta(UnityChatSceneUssGuid)),
            WriteIfMissingAsync(
                panelSettingsPath,
                ToolTemplates.RenderUnityPanelSettingsAsset(UnityDefaultRuntimeThemeGuid)),
            WriteIfMissingAsync(
                panelSettingsPath + ".meta",
                ToolTemplates.RenderUnityNativeAssetMeta(UnityChatPanelSettingsGuid)),
            WriteIfMissingAsync(
                runtimeThemePath,
                ToolTemplates.RenderUnityDefaultRuntimeTheme()),
            WriteIfMissingAsync(
                runtimeThemePath + ".meta",
                ToolTemplates.RenderUnityTssMeta(UnityDefaultRuntimeThemeGuid))).ConfigureAwait(false);

        await InstallUnityLoginSceneAsync(projectRoot, loginUiPath, loginUxmlPath, panelSettingsPath, options).ConfigureAwait(false);
        await InstallUnityChatSceneAsync(projectRoot, chatUiPath, chatUxmlPath, panelSettingsPath, options).ConfigureAwait(false);
        await WriteUnityEditorBuildSettingsIfNeededAsync(projectRoot).ConfigureAwait(false);
    }

    private static async Task PatchGodotProjectForAutoloadAsync(string projectRoot)
    {
        var projectPath = Path.Combine(projectRoot, "Client", "project.godot");
        if (!File.Exists(projectPath))
        {
            return;
        }

        var project = await File.ReadAllTextAsync(projectPath).ConfigureAwait(false);

        const string autoloadEntry = "ChatSession=\"*res://Scripts/Chat/ChatSession.cs\"";
        if (project.Contains(autoloadEntry, StringComparison.Ordinal))
        {
            return;
        }

        if (!project.Contains("[autoload]", StringComparison.Ordinal))
        {
            project += Environment.NewLine + "[autoload]" + Environment.NewLine + autoloadEntry + Environment.NewLine;
        }
        else
        {
            var autoloadIndex = project.IndexOf("[autoload]", StringComparison.Ordinal);
            var nextSectionIndex = project.IndexOf('[', autoloadIndex + 1);
            var insertIndex = nextSectionIndex >= 0 ? nextSectionIndex : project.Length;
            project = project.Insert(insertIndex, autoloadEntry + Environment.NewLine);
        }

        await File.WriteAllTextAsync(projectPath, project).ConfigureAwait(false);
    }

    private static async Task PatchGodotMainSceneAsync(string projectRoot)
    {
        var projectPath = Path.Combine(projectRoot, "Client", "project.godot");
        if (!File.Exists(projectPath))
        {
            return;
        }

        var project = await File.ReadAllTextAsync(projectPath).ConfigureAwait(false);
        var patched = System.Text.RegularExpressions.Regex.Replace(
            project,
            "(?m)^run/main_scene=.*$",
            "run/main_scene=\"res://Login.tscn\"");

        if (!patched.Contains("[application]", StringComparison.Ordinal))
        {
            patched += Environment.NewLine + "[application]" + Environment.NewLine + "run/main_scene=\"res://Login.tscn\"" + Environment.NewLine;
        }
        else if (!patched.Contains("run/main_scene=", StringComparison.Ordinal))
        {
            patched = patched.Replace("[application]", "[application]" + Environment.NewLine + "run/main_scene=\"res://Login.tscn\"", StringComparison.Ordinal);
        }

        if (!string.Equals(project, patched, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(projectPath, patched).ConfigureAwait(false);
        }
    }

    private static async Task InstallUnityLoginSceneAsync(
        string projectRoot,
        string loginUiPath,
        string uxmlPath,
        string panelSettingsPath,
        NewCommandOptions options)
    {
        var scenePath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "LoginScene.unity");
        if (File.Exists(scenePath))
        {
            return;
        }

        var loginUiGuid = await ReadUnityMetaGuidAsync(loginUiPath + ".meta", UnityLoginUiScriptGuid).ConfigureAwait(false);
        var uxmlGuid = await ReadUnityMetaGuidAsync(uxmlPath + ".meta", UnityLoginSceneUxmlGuid).ConfigureAwait(false);
        var panelSettingsGuid = await ReadUnityMetaGuidAsync(
            panelSettingsPath + ".meta",
            UnityChatPanelSettingsGuid).ConfigureAwait(false);

        var defaultPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";

        var sceneContent = ToolTemplates.RenderUnitySceneHeader() + $$"""
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
          m_Script: {fileID: 11500000, guid: {{loginUiGuid}}, type: 3}
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
          m_PanelSettings: {fileID: 11400000, guid: {{panelSettingsGuid}}, type: 2}
          m_ParentUI: {fileID: 0}
          sourceAsset: {fileID: 9197481963319205126, guid: {{uxmlGuid}}, type: 3}
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
        --- !u!1 &256380733
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: 256380735}
          - component: {fileID: 256380734}
          m_Layer: 0
          m_Name: Main Camera
          m_TagString: MainCamera
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!20 &256380734
        Camera:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 256380733}
          m_Enabled: 1
          serializedVersion: 2
          m_ClearFlags: 1
          m_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}
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
          m_Depth: 0
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
        --- !u!4 &256380735
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 256380733}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: -10}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        --- !u!1 &375611045
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: 375611047}
          - component: {fileID: 375611046}
          m_Layer: 0
          m_Name: Directional Light
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!108 &375611046
        Light:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 375611045}
          m_Enabled: 1
          serializedVersion: 11
          m_Type: 1
          m_Color: {r: 1, g: 0.95686275, b: 0.8392157, a: 1}
          m_Intensity: 1
          m_Range: 10
          m_SpotAngle: 30
          m_InnerSpotAngle: 21.80208
          m_CookieSize: 10
          m_Shadows:
            m_Type: 2
            m_Resolution: -1
            m_CustomResolution: -1
            m_Strength: 1
            m_Bias: 0.05
            m_NormalBias: 0.4
            m_NearPlane: 0.2
            m_CullingMatrixOverride:
              e00: 1
              e01: 0
              e02: 0
              e03: 0
              e10: 0
              e11: 1
              e12: 0
              e13: 0
              e20: 0
              e21: 0
              e22: 1
              e23: 0
              e30: 0
              e31: 0
              e32: 0
              e33: 1
            m_UseCullingMatrixOverride: 0
          m_Cookie: {fileID: 0}
          m_DrawHalo: 0
          m_Flare: {fileID: 0}
          m_RenderMode: 0
          m_CullingMask:
            serializedVersion: 2
            m_Bits: 4294967295
          m_RenderingLayerMask: 1
          m_Lightmapping: 4
          m_LightShadowCasterMode: 0
          m_AreaSize: {x: 1, y: 1}
          m_BounceIntensity: 1
          m_ColorTemperature: 6570
          m_UseColorTemperature: 0
          m_BoundingSphereOverride: {x: 0, y: 0, z: 0, w: 0}
          m_UseBoundingSphereOverride: 0
          m_UseViewFrustumForShadowCasterCull: 1
          m_ShadowRadius: 0
          m_ShadowAngle: 0
        --- !u!4 &375611047
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 375611045}
          serializedVersion: 2
          m_LocalRotation: {x: 0.40821788, y: -0.23456968, z: 0.10938163, w: 0.8754261}
          m_LocalPosition: {x: 0, y: 3, z: 0}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 50, y: -30, z: 0}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: 217437974}
          - {fileID: 256380735}
          - {fileID: 375611047}
        """;

        Directory.CreateDirectory(Path.GetDirectoryName(scenePath) ?? projectRoot);
        await File.WriteAllTextAsync(scenePath, sceneContent).ConfigureAwait(false);

        var sceneMeta = $$"""
        fileFormatVersion: 2
        guid: {{Guid.NewGuid().ToString("N")}}
        DefaultImporter:
          externalObjects: {}
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
        await File.WriteAllTextAsync(scenePath + ".meta", sceneMeta).ConfigureAwait(false);
    }

    private static async Task InstallUnityChatSceneAsync(
        string projectRoot,
        string chatUiPath,
        string uxmlPath,
        string panelSettingsPath,
        NewCommandOptions options)
    {
        var scenePath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "ChatScene.unity");
        if (File.Exists(scenePath))
        {
            return;
        }

        var chatUiGuid = await ReadUnityMetaGuidAsync(chatUiPath + ".meta", UnityChatUiScriptGuid).ConfigureAwait(false);
        var uxmlGuid = await ReadUnityMetaGuidAsync(uxmlPath + ".meta", UnityChatSceneUxmlGuid).ConfigureAwait(false);
        var panelSettingsGuid = await ReadUnityMetaGuidAsync(
            panelSettingsPath + ".meta",
            UnityChatPanelSettingsGuid).ConfigureAwait(false);

        var gameObjectId = 317337972L;
        var chatUiComponentId = gameObjectId + 1;
        var uiDocumentComponentId = chatUiComponentId + 1;
        var transformId = uiDocumentComponentId + 1;

        var chatSceneObjects = ToolTemplates.RenderUnityChatSceneObjects(
            gameObjectId,
            chatUiComponentId,
            uiDocumentComponentId,
            transformId,
            chatUiGuid,
            uxmlGuid,
            panelSettingsGuid);

        var sceneContent = ToolTemplates.RenderUnitySceneHeader() + chatSceneObjects + $$"""

        --- !u!1 &256380733
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: 256380735}
          - component: {fileID: 256380734}
          m_Layer: 0
          m_Name: Main Camera
          m_TagString: MainCamera
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!20 &256380734
        Camera:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 256380733}
          m_Enabled: 1
          serializedVersion: 2
          m_ClearFlags: 1
          m_BackGroundColor: {r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}
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
          m_Depth: 0
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
        --- !u!4 &256380735
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 256380733}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: -10}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        --- !u!1660057539 &9223372036854775807
        SceneRoots:
          m_ObjectHideFlags: 0
          m_Roots:
          - {fileID: {{transformId}}}
          - {fileID: 256380735}
        """;

        Directory.CreateDirectory(Path.GetDirectoryName(scenePath) ?? projectRoot);
        await File.WriteAllTextAsync(scenePath, sceneContent).ConfigureAwait(false);

        var sceneMeta = $$"""
        fileFormatVersion: 2
        guid: {{Guid.NewGuid().ToString("N")}}
        DefaultImporter:
          externalObjects: {}
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
        await File.WriteAllTextAsync(scenePath + ".meta", sceneMeta).ConfigureAwait(false);
    }

    private static async Task WriteUnityEditorBuildSettingsIfNeededAsync(string projectRoot)
    {
        var settingsPath = Path.Combine(projectRoot, "Client", "ProjectSettings", "EditorBuildSettings.asset");
        var loginMetaPath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "LoginScene.unity.meta");
        var chatMetaPath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "ChatScene.unity.meta");

        if (!File.Exists(loginMetaPath) || !File.Exists(chatMetaPath))
        {
            return;
        }

        var loginGuid = await ReadUnityMetaGuidAsync(loginMetaPath, string.Empty).ConfigureAwait(false);
        var chatGuid = await ReadUnityMetaGuidAsync(chatMetaPath, string.Empty).ConfigureAwait(false);

        if (string.IsNullOrEmpty(loginGuid) || string.IsNullOrEmpty(chatGuid))
        {
            return;
        }

        var loginEntry = $"  - enabled: 1\n    path: Assets/Scenes/LoginScene.unity\n    guid: {loginGuid}";
        var chatEntry = $"  - enabled: 1\n    path: Assets/Scenes/ChatScene.unity\n    guid: {chatGuid}";
        var scenesBlock = $"  m_Scenes:\n{loginEntry}\n{chatEntry}";

        if (File.Exists(settingsPath))
        {
            var existing = await File.ReadAllTextAsync(settingsPath).ConfigureAwait(false);

            if (existing.Contains("Assets/Scenes/LoginScene.unity", StringComparison.Ordinal)
                && existing.Contains("Assets/Scenes/ChatScene.unity", StringComparison.Ordinal))
            {
                return;
            }

            if (existing.Contains("m_Scenes: []", StringComparison.Ordinal))
            {
                existing = existing.Replace("m_Scenes: []", scenesBlock, StringComparison.Ordinal);
            }
            else
            {
                // Append entries to existing scenes list, before m_configObjects.
                var marker = "\n  m_configObjects:";
                var insertIndex = existing.IndexOf(marker, StringComparison.Ordinal);
                if (insertIndex >= 0)
                {
                    var toAppend = "";
                    if (!existing.Contains("Assets/Scenes/LoginScene.unity", StringComparison.Ordinal))
                    {
                        toAppend += "\n" + loginEntry;
                    }

                    if (!existing.Contains("Assets/Scenes/ChatScene.unity", StringComparison.Ordinal))
                    {
                        toAppend += "\n" + chatEntry;
                    }

                    if (toAppend.Length > 0)
                    {
                        existing = existing.Insert(insertIndex, toAppend);
                    }
                }
            }

            await File.WriteAllTextAsync(settingsPath, existing).ConfigureAwait(false);
            return;
        }

        var content = $$"""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!1045 &1
            EditorBuildSettings:
              m_ObjectHideFlags: 0
              serializedVersion: 2
            {{scenesBlock}}
              m_configObjects: {}
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? projectRoot);
        await File.WriteAllTextAsync(settingsPath, content).ConfigureAwait(false);
    }

    private static async Task<string> ReadUnityMetaGuidAsync(string path, string fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
        foreach (var line in lines)
        {
            const string prefix = "guid: ";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return fallback;
    }

    private static long NextAvailableFileId(string scene, long preferred)
    {
        var current = preferred;
        while (System.Text.RegularExpressions.Regex.IsMatch(scene, $@"&{current}\b"))
        {
            current++;
        }

        return current;
    }

    private static string AddUnitySceneRoot(string scene, long transformId)
    {
        var rootLine = $"  - {{fileID: {transformId}}}";
        if (scene.Contains(rootLine, StringComparison.Ordinal))
        {
            return scene;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            scene,
            @"(?m)^  m_Roots:\r?\n(?<roots>(?:  - \{fileID: \d+\}\r?\n)*)");
        if (!match.Success)
        {
            return scene;
        }

        var replacement = match.Value + rootLine + Environment.NewLine;
        return scene.Remove(match.Index, match.Length).Insert(match.Index, replacement);
    }

    private static Task WriteServerChatFilesAsync(string projectRoot)
    {
        return Task.WhenAll(
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Server", "App", "Properties", "AssemblyInfo.cs"),
                ToolTemplates.RenderServerAppAssemblyInfo()),
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Server", "App", "Chat", "ChatRoomActor.cs"),
                ToolTemplates.RenderServerChatRoomActor()));
    }

    private static Task WriteServerSolutionAsync(string projectRoot)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "Server.slnx"), ToolTemplates.RenderServerSolution());
    }

    private static void EnsureStarterServerProjectDirectory(string projectRoot)
    {
        var starterServerDirectory = Path.Combine(projectRoot, ToNativePath(ProjectConventions.StarterServerProjectPath));

        Directory.CreateDirectory(starterServerDirectory);
    }

    private static Task WriteServerProgramAsync(string projectRoot, NewCommandOptions options)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "App", "Program.cs"), ToolTemplates.RenderServerProgram(options));
    }

    private static Task WriteGeneratedServerApplicationAsync(string projectRoot, NewCommandOptions options)
    {
        var content = ToolTemplates.RenderGeneratedServerApplication(options);
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.CompletedTask;
        }

        return WriteAsync(
            Path.Combine(projectRoot, "Server", "App", "Hosting", "Advanced", "LakonaGameGeneratedApplication.cs"),
            content);
    }

    private static async Task WriteServerProjectAsync(string projectRoot, NewCommandOptions options)
    {
        var path = Path.Combine(projectRoot, "Server", "App", "Server.App.csproj");
        if (!File.Exists(path))
        {
            await WriteAsync(path, ToolTemplates.RenderServerProject(options)).ConfigureAwait(false);
            return;
        }

        var document = System.Xml.Linq.XDocument.Load(path);
        var project = document.Root ?? throw new InvalidOperationException($"Invalid project file: {path}");

        ProjectXmlMutator.SetProperty(project, "OutputType", "Exe");
        ProjectXmlMutator.SetProperty(project, "TargetFramework", "net10.0");
        ProjectXmlMutator.RemoveProperty(project, "TargetFrameworks");
        ProjectXmlMutator.SetProperty(project, "ImplicitUsings", "enable");
        ProjectXmlMutator.SetProperty(project, "Nullable", "enable");
        ProjectXmlMutator.SetProperty(project, "RootNamespace", "Server");
        ProjectXmlMutator.SetProperty(project, "BuildInParallel", "false");
        ProjectXmlMutator.SetProperty(project, "RestoreBuildInParallel", "false");
        ProjectXmlMutator.SetProperty(project, "LakonaRpcGenerateServer", "true");
        ProjectXmlMutator.SetProperty(project, "LakonaRpcServerGeneratedNamespace", ProjectConventions.StarterServerGeneratedNamespace);

        ProjectXmlMutator.EnsureProjectReference(project, @"..\..\Shared\Shared.csproj", "net10.0");
        ProjectXmlMutator.EnsureProjectReferenceWithoutOutput(project, @"..\Hotfix\Server.Hotfix.csproj");
        foreach (var reference in GameDependencyPlanner.CreateServerPlan(options).PackageReferences)
        {
            var attributes = new List<(string Name, string Value)>();
            if (reference.PrivateAssets is not null)
            {
                attributes.Add(("PrivateAssets", reference.PrivateAssets));
            }

            if (reference.OutputItemType is not null)
            {
                attributes.Add(("OutputItemType", reference.OutputItemType));
            }

            if (reference.IncludeAssets is not null)
            {
                attributes.Add(("IncludeAssets", reference.IncludeAssets));
            }

            ProjectXmlMutator.EnsurePackageReference(project, reference.Id, reference.Version, attributes.ToArray());
        }
        ProjectXmlMutator.EnsureNoneUpdate(project, "appsettings.json", "PreserveNewest");
        EnsureHotfixCopyTarget(project);

        await ToolFileWriter.WriteTextAsync(path, document.ToString()).ConfigureAwait(false);
    }

    private static Task WriteServerAppSettingsAsync(string projectRoot, NewCommandOptions options)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "App", "appsettings.json"), ToolTemplates.RenderServerAppSettings(options));
    }

    private static Task WriteHotfixProjectAsync(string projectRoot)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "Hotfix", "Server.Hotfix.csproj"), ToolTemplates.RenderHotfixProject());
    }

    private static Task WriteHotfixBoundaryFilesAsync(string projectRoot)
    {
        return WriteIfMissingAsync(
            Path.Combine(projectRoot, "Server", "Hotfix", "Chat", "ChatServiceImpl.cs"),
            ToolTemplates.RenderHotfixChatService());
    }

    private static Task WriteServerConfiguratorsAsync(string projectRoot, NewCommandOptions options)
    {
        return WriteAsync(
            Path.Combine(projectRoot, "Server", "App", "Hosting", "ServiceBindingConfigurator.cs"),
            ToolTemplates.RenderServiceBindingConfigurator());
    }

    private static Task WriteOperationsScaffoldingAsync(string projectRoot, NewCommandOptions options)
    {
        if (!ProjectConventions.UsesComposeDeployProfile(options.DeployProfile))
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(
            WriteAsync(Path.Combine(projectRoot, "Server", "Dockerfile"), ToolTemplates.RenderServerDockerfile()),
            WriteAsync(Path.Combine(projectRoot, "docker-compose.cluster.yml"), ToolTemplates.RenderClusterCompose(options)),
            WriteAsync(Path.Combine(projectRoot, ".env.cluster.example"), ToolTemplates.RenderClusterEnvExample(options)),
            WriteAsync(Path.Combine(projectRoot, "ops", "CLUSTER_OPERATIONS.md"), ToolTemplates.RenderClusterOperationsGuide()));
    }

    private static Task WriteAsync(string path, string content)
    {
        return ToolFileWriter.WriteTextAsync(path, content);
    }

    private static Task WriteIfMissingAsync(string path, string content)
    {
        return ToolFileWriter.WriteTextIfMissingAsync(path, content);
    }

    private static string ToNativePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private static void EnsureHotfixCopyTarget(System.Xml.Linq.XElement project)
    {
        const string targetName = "CopyHotfixOutput";
        foreach (var target in project
            .Elements("Target")
            .Where(element => string.Equals(element.Attribute("Name")?.Value, targetName, StringComparison.Ordinal))
            .ToArray())
        {
            target.Remove();
        }

        project.Add(
            new System.Xml.Linq.XElement(
                "Target",
                new System.Xml.Linq.XAttribute("Name", targetName),
                new System.Xml.Linq.XAttribute("AfterTargets", "Build"),
                new System.Xml.Linq.XElement(
                    "Copy",
                    new System.Xml.Linq.XAttribute("SourceFiles", @"$(ProjectDir)..\Hotfix\bin\$(Configuration)\$(TargetFramework)\Server.Hotfix.dll"),
                    new System.Xml.Linq.XAttribute("DestinationFolder", @"$(OutDir)hotfix\"),
                    new System.Xml.Linq.XAttribute("Condition", @"Exists('$(ProjectDir)..\Hotfix\bin\$(Configuration)\$(TargetFramework)\Server.Hotfix.dll')"))));
    }

}
