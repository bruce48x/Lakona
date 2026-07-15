using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lakona.Hub;

public enum HubLanguage
{
    SimplifiedChinese,
    TraditionalChinese,
    English
}

public sealed record HubLanguageOption(HubLanguage Language, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class HubLocalization : INotifyPropertyChanged
{
    public static readonly HubLanguageOption SimplifiedChinese = new(HubLanguage.SimplifiedChinese, "简体中文");
    public static readonly HubLanguageOption TraditionalChinese = new(HubLanguage.TraditionalChinese, "繁體中文");
    public static readonly HubLanguageOption English = new(HubLanguage.English, "English");

    private HubLanguageOption selectedLanguage;

    public HubLocalization()
        : this(DetectLanguage(CultureInfo.CurrentUICulture))
    {
    }

    public HubLocalization(CultureInfo culture)
        : this(DetectLanguage(culture))
    {
    }

    public HubLocalization(HubLanguage language)
    {
        selectedLanguage = OptionFor(language);
        Text = HubText.For(language);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<HubLanguageOption> LanguageOptions { get; } = [SimplifiedChinese, TraditionalChinese, English];

    public HubLanguageOption SelectedLanguage
    {
        get => selectedLanguage;
        set => SetLanguage(value.Language);
    }

    public HubLanguage Language => selectedLanguage.Language;

    public HubText Text { get; private set; }

    public void SetLanguage(HubLanguage language)
    {
        if (Language == language)
        {
            return;
        }

        selectedLanguage = OptionFor(language);
        Text = HubText.For(language);
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Text));
    }

    public static HubLanguage DetectLanguage(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.Length == 0)
        {
            name = culture.TwoLetterISOLanguageName;
        }

        var normalized = name.Replace('_', '-').ToLowerInvariant();
        if (!normalized.StartsWith("zh", StringComparison.Ordinal))
        {
            return HubLanguage.English;
        }

        if (normalized.Contains("hant", StringComparison.Ordinal) ||
            normalized is "zh-cht" or "zh-tw" or "zh-hk" or "zh-mo")
        {
            return HubLanguage.TraditionalChinese;
        }

        return HubLanguage.SimplifiedChinese;
    }

    private static HubLanguageOption OptionFor(HubLanguage language) => language switch
    {
        HubLanguage.SimplifiedChinese => SimplifiedChinese,
        HubLanguage.TraditionalChinese => TraditionalChinese,
        HubLanguage.English => English,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class HubText
{
    private HubText(HubLanguage language)
    {
        Language = language;
    }

    public HubLanguage Language { get; }

    public static HubText For(HubLanguage language) => new(language);

    public string Projects => L("项目", "專案", "Projects");
    public string Settings => L("设置", "設定", "Settings");
    public string HelpAndFeedback => L("帮助与反馈", "說明與意見反應", "Help & feedback");
    public string Minimize => L("最小化", "最小化", "Minimize");
    public string Maximize => L("最大化", "最大化", "Maximize");
    public string Close => L("关闭", "關閉", "Close");
    public string CreateProject => L("创建项目", "建立專案", "Create project");
    public string ImportExistingProject => L("导入现有项目", "匯入現有專案", "Import existing project");
    public string DotNetReady => L(".NET 10 环境已就绪", ".NET 10 環境已就緒", ".NET 10 is ready");
    public string DetectingTools => L("正在识别本机开发工具…", "正在識別本機開發工具…", "Detecting development tools…");
    public string ToolDetectionFailed => L("本机开发工具识别失败", "本機開發工具識別失敗", "Development tool detection failed");
    public string MyProjects => L("我的项目", "我的專案", "My projects");
    public string ProjectName => L("项目名称", "專案名稱", "Project name");
    public string EngineVersion => L("引擎版本", "引擎版本", "Engine version");
    public string LakonaVersion => L("Lakona 版本", "Lakona 版本", "Lakona version");
    public string LastOpened => L("上次打开", "上次開啟", "Last opened");
    public string OpenServer => L("打开服务端", "開啟伺服器端", "Open server");
    public string OpenClient => L("打开客户端", "開啟用戶端", "Open client");
    public string Open => L("打开", "開啟", "Open");
    public string BackToProjects => L("返回项目列表", "返回專案清單", "Back to projects");
    public string CreateDescription => L("填写项目配置，所有选项都可以直接查看和修改。", "填寫專案設定，所有選項都可直接檢視和修改。", "Configure the project. Every option is visible and editable.");
    public string BasicInformation => L("基本信息", "基本資訊", "Basic information");
    public string ProjectFolderHint => L("将同时作为项目文件夹名称", "也將作為專案資料夾名稱", "This is also used as the project folder name");
    public string OutputLocation => L("保存位置", "儲存位置", "Output location");
    public string Browse => L("浏览…", "瀏覽…", "Browse…");
    public string OutputLocationHint => L("项目会创建在该目录下的新文件夹中", "專案將建立在此目錄下的新資料夾中", "The project is created in a new folder under this location");
    public string ClientType => L("客户端类型", "用戶端類型", "Client type");
    public string ClientTypeHint => L("可选择 Unity、团结引擎、Godot 或 Console", "可選擇 Unity、團結引擎、Godot 或 Console", "Choose Unity, Tuanjie, Godot, or Console");
    public string ClientVersion => L("客户端版本", "用戶端版本", "Client version");
    public string ProjectConfiguration => L("项目配置", "專案設定", "Project configuration");
    public string Transport => L("传输协议", "傳輸協定", "Transport");
    public string DefaultKcp => L("默认 KCP", "預設 KCP", "Default: KCP");
    public string Serializer => L("序列化方式", "序列化方式", "Serializer");
    public string DefaultMemoryPack => L("默认 MemoryPack", "預設 MemoryPack", "Default: MemoryPack");
    public string Persistence => L("持久化", "持久化", "Persistence");
    public string PersistenceHint => L("选择项目需要的数据库支持", "選擇專案需要的資料庫支援", "Choose the database support required by the project");
    public string NuGetForUnitySource => L("NuGetForUnity 来源", "NuGetForUnity 來源", "NuGetForUnity source");
    public string DeploymentProfile => L("部署配置", "部署設定", "Deployment profile");
    public string DeploymentProfileHint => L("可选生成 Docker Compose 配置", "可選擇產生 Docker Compose 設定", "Optionally generate Docker Compose configuration");
    public string ProjectWillBeCreatedAt => L("项目将创建到", "專案將建立於", "Project will be created at");
    public string Cancel => L("取消", "取消", "Cancel");
    public string ContinueCreating => L("继续创建", "繼續建立", "Create project");

    public string LanguageAndRegion => L("语言与区域", "語言與地區", "Language & region");
    public string LanguageDescription => L("切换后立即应用到整个应用，测试时无需修改系统语言。", "切換後會立即套用至整個應用程式，測試時無需修改系統語言。", "Changes apply immediately across the app without changing the system language.");
    public string DisplayLanguage => L("显示语言", "顯示語言", "Display language");
    public string DisplayLanguageHint => L("仅影响 Lakona Hub，不修改操作系统设置。", "僅影響 Lakona Hub，不會修改作業系統設定。", "Affects Lakona Hub only and does not change operating system settings.");
    public string DevelopmentEnvironment => L("开发环境", "開發環境", "Development environment");
    public string DevelopmentEnvironmentDescription => L("查看 .NET SDK 状态，并识别或手动设置受支持的开发工具。", "檢視 .NET SDK 狀態，並識別或手動設定受支援的開發工具。", "Review the .NET SDK and detect or manually configure supported development tools.");
    public string RuntimeStatus => L("运行环境", "執行環境", "Runtime");
    public string RuntimeReadyTitle => L("内置 .NET 10 已就绪", "內建 .NET 10 已就緒", "Bundled .NET 10 is ready");
    public string RuntimeReadyDescription => L("项目操作使用 Hub 自带的 SDK，不依赖系统全局安装。", "專案操作使用 Hub 內建的 SDK，不依賴系統全域安裝。", "Project operations use Hub's bundled SDK and do not require a global installation.");
    public string DetectedTools => L("开发工具", "開發工具", "Development tools");
    public string RefreshDetection => L("重新检测", "重新偵測", "Detect again");
    public string EnvironmentReady => L("环境就绪", "環境就緒", "Ready");
    public string ApplicationUpdates => L("应用更新", "應用程式更新", "App updates");
    public string ApplicationUpdatesDescription => L("从 GitHub Releases 获取经过校验的更新；Linux 使用系统安装包。", "從 GitHub Releases 取得經過驗證的更新；Linux 使用系統安裝套件。", "Get verified updates from GitHub Releases. Linux uses native system packages.");
    public string CheckForUpdates => L("检查更新", "檢查更新", "Check for updates");
    public string DownloadAndInstall => L("下载并安装", "下載並安裝", "Download & install");
    public string UpdateCheckDescription => L("手动检查新版本；Windows 和 macOS 支持增量更新，Linux 交由系统安装器升级。", "手動檢查新版本；Windows 與 macOS 支援差異更新，Linux 交由系統安裝程式升級。", "Check manually for updates. Windows and macOS support deltas; Linux upgrades through the system installer.");
    public string CheckingForUpdates => L("正在检查 GitHub Releases…", "正在檢查 GitHub Releases…", "Checking GitHub Releases…");
    public string NoUpdatesAvailable(string version) => L($"当前已是最新版本（{version}）。", $"目前已是最新版本（{version}）。", $"Lakona Hub is up to date ({version}).");
    public string RestartingForUpdate => L("更新已校验，正在重启 Lakona Hub…", "更新已驗證，正在重新啟動 Lakona Hub…", "The update is verified. Restarting Lakona Hub…");
    public string CurrentHubVersion(string version) => L($"当前版本 {version}", $"目前版本 {version}", $"Current version {version}");
    public string IncrementalUpdateAvailable(string version) => L($"发现版本 {version}，可使用增量更新。", $"發現版本 {version}，可使用差異更新。", $"Version {version} is available as an incremental update.");
    public string FullUpdateAvailable(string version) => L($"发现版本 {version}；没有匹配的增量包，将使用完整更新。", $"發現版本 {version}；沒有相符的差異套件，將使用完整更新。", $"Version {version} is available. A matching delta was not found, so the full update will be used.");
    public string SystemPackageUpdateAvailable(string version) => L($"发现版本 {version}；将下载适用于当前 Linux 发行版的系统安装包。", $"發現版本 {version}；將下載適用於目前 Linux 發行版的系統安裝套件。", $"Version {version} is available as a package for this Linux distribution.");
    public string DownloadingIncrementalUpdate(string version) => L($"正在下载并校验 {version} 增量更新…", $"正在下載並驗證 {version} 差異更新…", $"Downloading and verifying incremental update {version}…");
    public string DownloadingFullUpdate(string version) => L($"正在下载并校验 {version} 完整更新…", $"正在下載並驗證 {version} 完整更新…", $"Downloading and verifying full update {version}…");
    public string DownloadingSystemPackage(string version) => L($"正在下载并校验 {version} Linux 系统安装包…", $"正在下載並驗證 {version} Linux 系統安裝套件…", $"Downloading and verifying Linux system package {version}…");
    public string SystemPackageInstallerOpened => L("安装包已经校验并交给系统安装器；请确认授权，并在安装完成后重启 Hub。", "安裝套件已驗證並交給系統安裝程式；請確認授權，並在安裝完成後重新啟動 Hub。", "The verified package was opened in the system installer. Confirm authorization, then restart Hub after installation.");
    public string UpdateFailed(string message) => L($"更新失败：{message}", $"更新失敗：{message}", $"Update failed: {message}");
    public string PreviousVersionRestored(string message) => L($"更新未能完成，已恢复并重新打开原版本：{message}", $"更新未能完成，已還原並重新開啟原版本：{message}", $"The update could not be completed. The previous version was restored and reopened: {message}");

    public string SelectProjectFolder => L("选择 Lakona 项目目录", "選擇 Lakona 專案目錄", "Select a Lakona project folder");
    public string SelectOutputFolder => L("选择新项目的保存位置", "選擇新專案的儲存位置", "Select a location for the new project");
    public string ClientVersionHint(bool hasVersion) => hasVersion
        ? L("选择客户端使用的编辑器版本", "選擇用戶端使用的編輯器版本", "Choose the editor version used by the client")
        : L("Console 客户端不需要引擎版本", "Console 用戶端不需要引擎版本", "Console clients do not require an engine version");
    public string NuGetForUnityHint(bool usesNuGetForUnity) => usesNuGetForUnity
        ? L("Unity 系客户端的包获取方式", "Unity 系列用戶端的套件取得方式", "Package source for Unity-family clients")
        : L("当前客户端不使用 NuGetForUnity", "目前用戶端不使用 NuGetForUnity", "The selected client does not use NuGetForUnity");
    public string TargetPathMissing => L("请填写项目名称和保存位置", "請填寫專案名稱和儲存位置", "Enter a project name and output location");
    public string InvalidProjectPath => L("项目路径无效", "專案路徑無效", "The project path is invalid");
    public string ProjectNameRequired => L("请输入项目名称", "請輸入專案名稱", "Enter a project name");
    public string InvalidProjectName => L("项目名称包含不能用于文件夹的字符", "專案名稱包含無法用於資料夾的字元", "The project name contains characters that cannot be used in a folder name");
    public string OutputLocationRequired => L("请选择保存位置", "請選擇儲存位置", "Choose an output location");
    public string FullOutputPathRequired => L("请选择完整的保存路径", "請選擇完整的儲存路徑", "Choose a fully qualified output path");
    public string InvalidOutputPath => L("保存路径无效", "儲存路徑無效", "The output path is invalid");
    public string ClientVersionRequired => L("请选择客户端版本", "請選擇用戶端版本", "Choose a client version");
    public string ConfigurationReady => L("配置完整，可以继续创建", "設定完整，可以繼續建立", "Configuration is complete and ready");
    public string NoDatabase => L("不使用数据库", "不使用資料庫", "No database");
    public string NoDeploymentProfile => L("不生成部署配置", "不產生部署設定", "No deployment configuration");
    public string EmbeddedPackages => L("内置包源", "內建套件來源", "Bundled packages");
    public string Tuanjie => L("团结引擎", "團結引擎", "Tuanjie");
    public string TuanjieVersion => L("团结引擎 1.6.7", "團結引擎 1.6.7", "Tuanjie 1.6.7");

    public string UnnamedProject => L("未命名项目", "未命名專案", "Unnamed project");
    public string NotDetected => L("未检测到", "未偵測到", "Not detected");
    public string NoConfiguredPath => L("未设置路径", "未設定路徑", "No path configured");
    public string ConfiguredToolUnavailable => L("已设置的路径不可用", "已設定的路徑無法使用", "Configured path unavailable");
    public string DetectedTool(string? version) => string.IsNullOrWhiteSpace(version)
        ? L("已识别", "已識別", "Detected")
        : L($"已识别 · {version}", $"已識別 · {version}", $"Detected · {version}");
    public string SelectApplicationExecutable(string application) => L(
        $"选择 {application} 可执行文件",
        $"選擇 {application} 執行檔",
        $"Select the {application} executable");
    public string InvalidApplicationExecutable(string application) => L(
        $"所选文件不是可识别的 {application} 可执行文件。",
        $"所選檔案不是可識別的 {application} 執行檔。",
        $"The selected file is not a recognized {application} executable.");
    public string ApplicationExecutableSaved(string application) => L(
        $"已保存 {application} 路径，并立即用于项目操作。",
        $"已儲存 {application} 路徑，並立即用於專案操作。",
        $"The {application} path was saved and is now used for project operations.");
    public string ApplicationExecutableSaveFailed(string message) => L(
        $"无法保存工具路径：{message}",
        $"無法儲存工具路徑：{message}",
        $"Could not save the application path: {message}");
    public string ProjectReady => L("项目结构完整", "專案結構完整", "Project structure is complete");
    public string ProjectNeedsAttention => L("项目结构需要检查", "專案結構需要檢查", "Project structure needs attention");
    public string JustNow => L("刚刚", "剛剛", "Just now");
    public string Unknown => L("未识别", "未識別", "Unknown");
    public string ClientAction(string clientName) => L($"{clientName} 打开", $"使用 {clientName} 開啟", $"Open in {clientName}");
    public string OpenClientAction => L("打开客户端", "開啟用戶端", "Open client");
    public string NoServerIde => L("未检测到 Rider、Visual Studio 或 VS Code", "未偵測到 Rider、Visual Studio 或 VS Code", "Rider, Visual Studio, and VS Code were not detected");
    public string OpenServerWith(string editor) => L($"使用 {editor} 打开服务端", $"使用 {editor} 開啟伺服器端", $"Open the server with {editor}");
    public string NoClientEditor(string client) => L($"未检测到可用于 {client} 的编辑器", $"未偵測到可用於 {client} 的編輯器", $"No editor was detected for {client}");
    public string OpenClientWith(string editor) => L($"使用 {editor} 打开客户端", $"使用 {editor} 開啟用戶端", $"Open the client with {editor}");
    public string EnvironmentNone => L("未识别 Rider、Visual Studio、VS Code、Unity 或 Godot", "未識別 Rider、Visual Studio、VS Code、Unity 或 Godot", "Rider, Visual Studio, VS Code, Unity, and Godot were not detected");
    public string EnvironmentDetected(string names) => L($"已识别 {names}", $"已識別 {names}", $"Detected {names}");
    public string EnvironmentSeparator => L("、", "、", ", ");
    public string ToolDetectionError(string message) => L($"无法识别本机开发工具：{message}", $"無法識別本機開發工具：{message}", $"Could not detect local development tools: {message}");
    public string Imported(string name) => L($"已导入“{name}”。Hub 只读取了项目结构，没有写入任何管理文件。", $"已匯入「{name}」。Hub 僅讀取專案結構，未寫入任何管理檔案。", $"Imported “{name}”. Hub only read the project structure and did not write management files.");
    public string ImportedIncomplete(string name, int count) => L($"“{name}”已加入列表，但项目结构需要检查：{count} 项提示。", $"「{name}」已加入清單，但專案結構需要檢查：{count} 項提示。", $"“{name}” was added, but its structure needs attention: {count} issue(s).");
    public string NotLakonaProject => L("所选目录不是可识别的 Lakona 项目。项目内容没有被修改。", "所選目錄不是可識別的 Lakona 專案。專案內容未被修改。", "The selected folder is not a recognized Lakona project. No project files were changed.");
    public string ProjectNotFound => L("所选项目目录不存在。", "所選專案目錄不存在。", "The selected project folder does not exist.");
    public string ProjectUnrecognized => L("无法识别该项目。", "無法識別此專案。", "The project could not be recognized.");
    public string ProjectSelection(string name, string status, string path) => L($"{name}：{status}。路径：{path}", $"{name}：{status}。路徑：{path}", $"{name}: {status}. Path: {path}");
    public string NoSupportedIde => L("未检测到 Rider、Visual Studio 或 VS Code。请先安装一个受支持的 IDE。", "未偵測到 Rider、Visual Studio 或 VS Code。請先安裝受支援的 IDE。", "Rider, Visual Studio, and VS Code were not detected. Install a supported IDE first.");
    public string OpeningServer(string editor, string project) => L($"正在使用 {editor} 打开“{project}”的服务端。", $"正在使用 {editor} 開啟「{project}」的伺服器端。", $"Opening the server for “{project}” with {editor}.");
    public string OpenServerFailed(string message) => L($"无法打开服务端：{message}", $"無法開啟伺服器端：{message}", $"Could not open the server: {message}");
    public string NoMatchingClientEditor => L("没有检测到与当前项目客户端匹配的 Unity 或 Godot 编辑器。", "未偵測到與目前專案用戶端相符的 Unity 或 Godot 編輯器。", "No Unity or Godot editor matching this project client was detected.");
    public string OpeningClient(string editor, string project) => L($"正在使用 {editor} 打开“{project}”的客户端。", $"正在使用 {editor} 開啟「{project}」的用戶端。", $"Opening the client for “{project}” with {editor}.");
    public string OpenClientFailed(string message) => L($"无法打开客户端：{message}", $"無法開啟用戶端：{message}", $"Could not open the client: {message}");
    public string CreatingProject(string name) => L($"正在创建“{name}”…", $"正在建立「{name}」…", $"Creating “{name}”…");
    public string ProjectCreated(string name) => L($"已创建“{name}”。项目生成逻辑与 lakona-tool 完全共享。", $"已建立「{name}」。專案產生邏輯與 lakona-tool 完全共用。", $"Created “{name}”. Project generation is fully shared with lakona-tool.");
    public string ProjectCreationFailed(string message) => L($"创建项目失败：{message}", $"建立專案失敗：{message}", $"Project creation failed: {message}");
    public string HelpDialogDescription => L("问题反馈和功能建议将在 Lakona 的 GitHub Issues 页面中提交。是否打开该页面？", "問題回報和功能建議將在 Lakona 的 GitHub Issues 頁面中提交。是否開啟此頁面？", "Bug reports and feature requests are submitted on Lakona's GitHub Issues page. Open it now?");
    public string OpenGitHubIssues => L("前往 GitHub Issues", "前往 GitHub Issues", "Open GitHub Issues");
    public string OpenHelpPageFailed(string message) => L($"无法打开 GitHub Issues 页面：{message}", $"無法開啟 GitHub Issues 頁面：{message}", $"Could not open the GitHub Issues page: {message}");

    private string L(string simplifiedChinese, string traditionalChinese, string english) => Language switch
    {
        HubLanguage.SimplifiedChinese => simplifiedChinese,
        HubLanguage.TraditionalChinese => traditionalChinese,
        HubLanguage.English => english,
        _ => throw new ArgumentOutOfRangeException(nameof(Language), Language, null)
    };
}
