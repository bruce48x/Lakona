internal static class ToolTemplates
{
    // Server project templates
    public static string RenderServerSolution() => ServerProjectTemplates.RenderServerSolution();
    public static string RenderServerProgram(NewCommandOptions options) => ServerProjectTemplates.RenderServerProgram(options);
    public static string RenderGeneratedServerApplication(NewCommandOptions options) => ServerProjectTemplates.RenderGeneratedServerApplication(options);
    public static string RenderServerProject(NewCommandOptions options) => ServerProjectTemplates.RenderServerProject(options);
    public static string RenderServerAppSettings(NewCommandOptions options) => ServerProjectTemplates.RenderServerAppSettings(options);
    public static string RenderHotfixProject() => ServerProjectTemplates.RenderHotfixProject();
    public static string RenderServerChatRoomActor() => ServerProjectTemplates.RenderServerChatRoomActor();
    public static string RenderHotfixLoginService() => ServerProjectTemplates.RenderHotfixLoginService();
    public static string RenderHotfixChatService() => ServerProjectTemplates.RenderHotfixChatService();
    public static string RenderServerAppAssemblyInfo() => ServerProjectTemplates.RenderServerAppAssemblyInfo();
    public static string RenderServiceBindingConfigurator() => ServerProjectTemplates.RenderServiceBindingConfigurator();
    public static string RenderServerChatConnectionLifecycle() => ServerProjectTemplates.RenderServerChatConnectionLifecycle();

    // Shared contract templates
    public static string RenderSharedRpcContractIds() => SharedContractTemplates.RenderSharedRpcContractIds();
    public static string RenderSharedLoginProtocols() => SharedContractTemplates.RenderSharedLoginProtocols();
    public static string RenderSharedChatProtocols() => SharedContractTemplates.RenderSharedChatProtocols();
    public static string RenderSharedChatMessages() => SharedContractTemplates.RenderSharedChatMessages();
    public static string RenderSharedChatMessages(NewCommandOptions options) => SharedContractTemplates.RenderSharedChatMessages(options);

    // Client chat templates
    public static string RenderClientLoginClient() => ChatClientTemplates.RenderClientLoginClient();
    public static string RenderClientChatClient() => ChatClientTemplates.RenderClientChatClient();
    public static string RenderChatSession() => ChatClientTemplates.RenderChatSession();
    public static string RenderGodotChatSession() => ChatClientTemplates.RenderGodotChatSession();
    public static string RenderGodotThemeClass() => ChatClientTemplates.RenderGodotThemeClass();
    public static string RenderUnityLoginUI(NewCommandOptions options) => ChatClientTemplates.RenderUnityLoginUI(options);
    public static string RenderUnityLoginUxml() => ChatClientTemplates.RenderUnityLoginUxml();
    public static string RenderUnityLoginUss() => ChatClientTemplates.RenderUnityLoginUss();
    public static string RenderGodotLoginScene(NewCommandOptions options) => ChatClientTemplates.RenderGodotLoginScene(options);
    public static string RenderGodotLoginTscn() => ChatClientTemplates.RenderGodotLoginTscn();
    public static string RenderGodotChatScene() => ChatClientTemplates.RenderGodotChatScene();
    public static string RenderGodotChatTscn() => ChatClientTemplates.RenderGodotChatTscn();
    public static string RenderClientChatUI() => ChatClientTemplates.RenderClientChatUI();
    public static string RenderClientChatUxml() => ChatClientTemplates.RenderClientChatUxml();
    public static string RenderClientChatUss() => ChatClientTemplates.RenderClientChatUss();

    // Unity asset templates
    public static string RenderUnityMonoScriptMeta(string guid) => UnityAssetTemplates.RenderUnityMonoScriptMeta(guid);
    public static string RenderUnityUxmlMeta(string guid) => UnityAssetTemplates.RenderUnityUxmlMeta(guid);
    public static string RenderUnityUssMeta(string guid) => UnityAssetTemplates.RenderUnityUssMeta(guid);
    public static string RenderUnityTssMeta(string guid) => UnityAssetTemplates.RenderUnityTssMeta(guid);
    public static string RenderUnityNativeAssetMeta(string guid) => UnityAssetTemplates.RenderUnityNativeAssetMeta(guid);
    public static string RenderUnityDefaultRuntimeTheme() => UnityAssetTemplates.RenderUnityDefaultRuntimeTheme();
    public static string RenderUnitySceneHeader() => UnityAssetTemplates.RenderUnitySceneHeader();
    public static string RenderUnityPanelSettingsAsset(string defaultRuntimeThemeGuid) => UnityAssetTemplates.RenderUnityPanelSettingsAsset(defaultRuntimeThemeGuid);
    public static string RenderUnityChatSceneObjects(
        long gameObjectId,
        long chatUiComponentId,
        long uiDocumentComponentId,
        long transformId,
        string chatUiScriptGuid,
        string uxmlGuid,
        string panelSettingsGuid,
        string serverPath = "") =>
        UnityAssetTemplates.RenderUnityChatSceneObjects(gameObjectId, chatUiComponentId, uiDocumentComponentId, transformId, chatUiScriptGuid, uxmlGuid, panelSettingsGuid, serverPath);
    public static string RenderUnityNuGetPackageImportGuard() => UnityAssetTemplates.RenderUnityNuGetPackageImportGuard();

    // Operations templates
    public static string RenderServerDockerfile() => OperationsTemplates.RenderServerDockerfile();
    public static string RenderClusterCompose(NewCommandOptions options) => OperationsTemplates.RenderClusterCompose(options);
    public static string RenderClusterEnvExample(NewCommandOptions options) => OperationsTemplates.RenderClusterEnvExample(options);
    public static string RenderClusterOperationsGuide() => OperationsTemplates.RenderClusterOperationsGuide();
}
