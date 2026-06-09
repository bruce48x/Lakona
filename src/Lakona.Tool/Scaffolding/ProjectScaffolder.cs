internal sealed class ProjectScaffolder
{
    private const string UnityChatUiScriptGuid = "462a8730535800d4a801000623f4450e";
    private const string UnityChatSceneUxmlGuid = "d8e055cb54604094cb41badb6b3866f6";
    private const string UnityChatSceneUssGuid = "f7e09962267bcef45a558136fb62bb68";
    private const string UnityChatPanelSettingsGuid = "0c8089bab5856fe4d8f88e6f526fd306";
    private const string UnityDefaultRuntimeThemeGuid = "9a59d5efd84abc44da5e32a04db78d26";

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
                    Path.Combine(projectRoot, "Client", "Scripts", "Chat", "ChatScene.cs"),
                    ToolTemplates.RenderGodotChatScene(options)),
                WriteAsync(
                    Path.Combine(projectRoot, "Client", "Main.tscn"),
                    ToolTemplates.RenderGodotMainScene())).ConfigureAwait(false);

            await PatchGodotMainSceneAsync(projectRoot).ConfigureAwait(false);
            return;
        }

        var chatUiPath = Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatUI.cs");
        var uxmlPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "ChatScene.uxml");
        var ussPath = Path.Combine(projectRoot, "Client", "Assets", "UI", "ChatScene.uss");
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
                chatUiPath,
                ToolTemplates.RenderClientChatUI(options)),
            WriteIfMissingAsync(
                uxmlPath,
                ToolTemplates.RenderClientChatUxml()),
            WriteIfMissingAsync(
                ussPath,
                ToolTemplates.RenderClientChatUss()),
            WriteIfMissingAsync(
                chatUiPath + ".meta",
                ToolTemplates.RenderUnityMonoScriptMeta(UnityChatUiScriptGuid)),
            WriteIfMissingAsync(
                uxmlPath + ".meta",
                ToolTemplates.RenderUnityUxmlMeta(UnityChatSceneUxmlGuid)),
            WriteIfMissingAsync(
                ussPath + ".meta",
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

        await InstallUnityChatSceneAsync(projectRoot, chatUiPath, uxmlPath, panelSettingsPath, options).ConfigureAwait(false);
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
            "run/main_scene=\"res://Main.tscn\"");

        if (!patched.Contains("[application]", StringComparison.Ordinal))
        {
            patched += Environment.NewLine + "[application]" + Environment.NewLine + "run/main_scene=\"res://Main.tscn\"" + Environment.NewLine;
        }
        else if (!patched.Contains("run/main_scene=", StringComparison.Ordinal))
        {
            patched = patched.Replace("[application]", "[application]" + Environment.NewLine + "run/main_scene=\"res://Main.tscn\"", StringComparison.Ordinal);
        }

        if (!string.Equals(project, patched, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(projectPath, patched).ConfigureAwait(false);
        }
    }

    private static async Task InstallUnityChatSceneAsync(
        string projectRoot,
        string chatUiPath,
        string uxmlPath,
        string panelSettingsPath,
        NewCommandOptions options)
    {
        var scenePath = Path.Combine(projectRoot, "Client", "Assets", "Scenes", "ConnectionTest.unity");
        if (!File.Exists(scenePath))
        {
            return;
        }

        var chatUiGuid = await ReadUnityMetaGuidAsync(chatUiPath + ".meta", UnityChatUiScriptGuid).ConfigureAwait(false);
        var uxmlGuid = await ReadUnityMetaGuidAsync(uxmlPath + ".meta", UnityChatSceneUxmlGuid).ConfigureAwait(false);
        var panelSettingsGuid = await ReadUnityMetaGuidAsync(
            panelSettingsPath + ".meta",
            UnityChatPanelSettingsGuid).ConfigureAwait(false);

        var scene = await File.ReadAllTextAsync(scenePath).ConfigureAwait(false);
        var defaultPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var panelSettingsReference =
            $"m_PanelSettings: {{fileID: 11400000, guid: {panelSettingsGuid}, type: 2}}";

        if (scene.Contains("m_Name: Lakona.Game Chat UI", StringComparison.Ordinal))
        {
            var patchedExisting = scene.Replace("m_PanelSettings: {fileID: 0}", panelSettingsReference, StringComparison.Ordinal);
            patchedExisting = System.Text.RegularExpressions.Regex.Replace(
                patchedExisting,
                @"(?m)^  _serverPath:.*$",
                $"  _serverPath: {defaultPath}");
            if (!string.Equals(patchedExisting, scene, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(scenePath, patchedExisting).ConfigureAwait(false);
            }

            return;
        }

        var gameObjectId = NextAvailableFileId(scene, 217337972);
        var chatUiComponentId = NextAvailableFileId(scene, gameObjectId + 1);
        var uiDocumentComponentId = NextAvailableFileId(scene, chatUiComponentId + 1);
        var transformId = NextAvailableFileId(scene, uiDocumentComponentId + 1);
        var chatSceneObjects = ToolTemplates.RenderUnityChatSceneObjects(
            gameObjectId,
            chatUiComponentId,
            uiDocumentComponentId,
            transformId,
            chatUiGuid,
            uxmlGuid,
            panelSettingsGuid,
            defaultPath);

        var sceneRootsMarker = "--- !u!1660057539 &9223372036854775807";
        var sceneRootsIndex = scene.LastIndexOf(sceneRootsMarker, StringComparison.Ordinal);
        var patched = sceneRootsIndex >= 0
            ? scene.Insert(sceneRootsIndex, chatSceneObjects + Environment.NewLine)
            : scene + Environment.NewLine + chatSceneObjects + Environment.NewLine;
        patched = AddUnitySceneRoot(patched, transformId);

        await File.WriteAllTextAsync(scenePath, patched).ConfigureAwait(false);
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
            Path.Combine(projectRoot, "Server", "Server", "Hosting", "Advanced", "LakonaGameGeneratedApplication.cs"),
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

            ProjectXmlMutator.EnsurePackageReference(project, reference.Id, reference.Version, attributes.ToArray());
        }
        ProjectXmlMutator.EnsureNoneUpdate(project, "appsettings.json", "PreserveNewest");
        EnsureHotfixCopyTarget(project);

        await ToolFileWriter.WriteTextAsync(path, document.ToString()).ConfigureAwait(false);
    }

    private static Task WriteServerAppSettingsAsync(string projectRoot, NewCommandOptions options)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "Server", "appsettings.json"), ToolTemplates.RenderServerAppSettings(options));
    }

    private static Task WriteHotfixProjectAsync(string projectRoot)
    {
        return WriteAsync(Path.Combine(projectRoot, "Server", "Hotfix", "Server.Hotfix.csproj"), ToolTemplates.RenderHotfixProject());
    }

    private static Task WriteHotfixBoundaryFilesAsync(string projectRoot)
    {
        return Task.WhenAll(
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Server", "Hotfix", "Chat", "ChatServiceImpl.cs"),
                ToolTemplates.RenderHotfixChatService()),
            WriteIfMissingAsync(
                Path.Combine(projectRoot, "Server", "Hotfix", "Services", "PingService.cs"),
                ToolTemplates.RenderHotfixPingService()));
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
