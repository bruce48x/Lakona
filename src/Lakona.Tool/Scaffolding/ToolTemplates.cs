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
    public static string RenderServerChatRules() => ServerProjectTemplates.RenderServerChatRules();
    public static string RenderHotfixChatServiceImpl() => ServerProjectTemplates.RenderHotfixChatServiceImpl();
    public static string RenderHotfixChatSystem() => ServerProjectTemplates.RenderHotfixChatSystem();
    public static string RenderHotfixPingService() => ServerProjectTemplates.RenderHotfixPingService();
    public static string RenderServerAppAssemblyInfo() => ServerProjectTemplates.RenderServerAppAssemblyInfo();
    public static string RenderServiceBindingConfigurator() => ServerProjectTemplates.RenderServiceBindingConfigurator();

    // Shared contract templates
    public static string RenderSharedRpcContractIds() => SharedContractTemplates.RenderSharedRpcContractIds();
    public static string RenderSharedChatProtocols() => SharedContractTemplates.RenderSharedChatProtocols();
    public static string RenderSharedChatMessages() => SharedContractTemplates.RenderSharedChatMessages();
    public static string RenderSharedChatMessages(NewCommandOptions options) => SharedContractTemplates.RenderSharedChatMessages(options);
    public static string RenderServerChatRuleState() => ServerProjectTemplates.RenderServerChatRuleState();

    // Client chat templates
    public static string RenderClientChatClient() => ChatClientTemplates.RenderClientChatClient();
    public static string RenderGodotChatScene(NewCommandOptions options) => ChatClientTemplates.RenderGodotChatScene(options);
    public static string RenderGodotMainScene() => ChatClientTemplates.RenderGodotMainScene();
    public static string RenderClientChatUI(NewCommandOptions options) => ChatClientTemplates.RenderClientChatUI(options);
    public static string RenderClientChatUxml() => ChatClientTemplates.RenderClientChatUxml();
    public static string RenderClientChatUss() => ChatClientTemplates.RenderClientChatUss();

    // Unity asset templates
    public static string RenderUnityMonoScriptMeta(string guid) => UnityAssetTemplates.RenderUnityMonoScriptMeta(guid);
    public static string RenderUnityUxmlMeta(string guid) => UnityAssetTemplates.RenderUnityUxmlMeta(guid);
    public static string RenderUnityUssMeta(string guid) => UnityAssetTemplates.RenderUnityUssMeta(guid);
    public static string RenderUnityTssMeta(string guid) => UnityAssetTemplates.RenderUnityTssMeta(guid);
    public static string RenderUnityNativeAssetMeta(string guid) => UnityAssetTemplates.RenderUnityNativeAssetMeta(guid);
    public static string RenderUnityDefaultRuntimeTheme() => UnityAssetTemplates.RenderUnityDefaultRuntimeTheme();
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
