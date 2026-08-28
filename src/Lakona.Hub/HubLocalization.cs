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
        HubCrashReporter.SetActivity($"Changing language from {Language} to {language}");
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
    public string Restore => L("还原", "還原", "Restore");
    public string Close => L("关闭", "關閉", "Close");
    public string CreateProject => L("创建项目", "建立專案", "Create project");
    public string ImportExistingProject => L("导入现有项目", "匯入現有專案", "Import existing project");
    public string DotNetReady => L(".NET 10 环境已就绪", ".NET 10 環境已就緒", ".NET 10 is ready");
    public string DetectingTools => L("正在识别本机开发工具…", "正在識別本機開發工具…", "Detecting development tools…");
    public string ToolDetectionFailed => L("本机开发工具识别失败", "本機開發工具識別失敗", "Development tool detection failed");
    public string MyProjects => L("我的项目", "我的專案", "My projects");
    public string SearchProjects => L("搜索项目…", "搜尋專案…", "Search projects…");
    public string SortByName => L("按名称", "依名稱", "Name");
    public string SortByEngine => L("按引擎", "依引擎", "Engine");
    public string SortByLakona => L("按 Lakona", "依 Lakona", "Lakona");
    public string SortByLastOpened => L("最近打开", "最近開啟", "Recent");
    public string NoMatchingProjects => L("没有匹配的项目", "沒有符合的專案", "No matching projects");
    public string ProjectName => L("项目名称", "專案名稱", "Project name");
    public string LakonaVersion => L("Lakona 版本", "Lakona 版本", "Lakona version");
    public string LastOpened => L("上次打开", "上次開啟", "Last opened");
    public string Server => L("服务端", "伺服器端", "Server");
    public string Client => L("客户端", "用戶端", "Client");
    public string Package => L("打包", "打包", "Package");
    public string PackageProject(string project) => L($"打包“{project}”", $"打包「{project}」", $"Package “{project}”");
    public string PackageType => L("包类型", "套件類型", "Package type");
    public string PackageOutputLocation => L("产物保存位置", "產物儲存位置", "Artifact output location");
    public string TargetRuntime => L("目标运行时", "目標執行環境", "Target runtime");
    public string BuildConfiguration => L("构建配置", "建置設定", "Build configuration");
    public string BuildTag => L("兼容版本", "相容版本", "BuildTag");
    public string ServerPackage => L("完整服务端包", "完整伺服器套件", "Deployable server package");
    public string HotfixPackage => L("热更包", "熱更新套件", "Hotfix package");
    public string StartPackaging => L("开始打包", "開始打包", "Build package");
    public string CancelPackaging => L("取消打包", "取消打包", "Cancel packaging");
    public string OpenArtifactFolder => L("打开产物目录", "開啟產物目錄", "Open artifact folder");
    public string OpenPackagingLogFolder => L("打开日志目录", "開啟日誌目錄", "Open log folder");
    public string PackageReady => L("配置完成，可以开始打包。", "設定完成，可以開始打包。", "Ready to build a package.");
    public string PackageSdkRequired => L("打包需要可用的 .NET 10 SDK。", "打包需要可用的 .NET 10 SDK。", "Packaging requires an available .NET 10 SDK.");
    public string PackageAlreadyRunning => L("已有打包任务正在运行。", "已有打包工作正在執行。", "A packaging operation is already running.");
    public string PackagingStarted => L("正在准备打包…", "正在準備打包…", "Preparing package…");
    public string PackageValidating => L("正在验证项目和打包参数…", "正在驗證專案與打包參數…", "Validating project and package inputs…");
    public string PackageBuilding => L("正在构建并生成压缩包…", "正在建置並產生壓縮檔…", "Building and creating the package archive…");
    public string PackageSucceeded => L("打包成功。", "打包成功。", "Package created successfully.");
    public string PackageCanceled => L("打包已取消。", "打包已取消。", "Packaging was canceled.");
    public string PackageFailed(string message) => L($"打包失败：{message}", $"打包失敗：{message}", $"Packaging failed: {message}");
    public string OpenClient => L("打开客户端", "開啟用戶端", "Open client");
    public string OpenProjectFolder => L("打开所在文件夹", "開啟所在資料夾", "Open containing folder");
    public string Open => L("打开", "開啟", "Open");
    public string MoreActions => L("更多操作", "更多操作", "More actions");
    public string More => L("更多", "更多", "More");
    public string RemoveFromList => L("从列表中移除", "從清單中移除", "Remove from list");
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
    public string DefaultWebSocket => L("默认 WebSocket", "預設 WebSocket", "Default: WebSocket");
    public string Serializer => L("序列化方式", "序列化方式", "Serializer");
    public string DefaultMemoryPack => L("默认 MemoryPack", "預設 MemoryPack", "Default: MemoryPack");
    public string TransportHint(string id) => id switch
    {
        "tcp" => L("可靠的长连接 TCP 传输", "可靠的長連線 TCP 傳輸", "Reliable persistent TCP transport"),
        "kcp" => L("面向低延迟场景的 UDP 传输", "適用低延遲場景的 UDP 傳輸", "UDP transport for low-latency scenarios"),
        _ => L("兼容浏览器与代理的 WebSocket 传输", "相容瀏覽器與 Proxy 的 WebSocket 傳輸", "WebSocket transport compatible with browsers and proxies")
    };
    public string SerializerHint(string id) => id == "json"
        ? L("便于阅读和调试的文本格式", "便於閱讀和除錯的文字格式", "Human-readable text format for debugging")
        : L("紧凑且高性能的二进制格式", "精簡且高效能的二進位格式", "Compact high-performance binary format");
    public string NuGetForUnitySource => L("NuGetForUnity 来源", "NuGetForUnity 來源", "NuGetForUnity source");
    public string MembershipProvider => L("集群成员存储", "叢集成員儲存", "Cluster membership");
    public string InMemory => L("内存（本地开发）", "記憶體（本機開發）", "Memory (local development)");
    public string MembershipProviderHint(string id) => id switch
    {
        "memory" => L("适合本地单节点，不需要数据库或 Redis", "適合本機單節點，不需要資料庫或 Redis", "For a local single node; no database or Redis is required"),
        "postgres" => L("生成 PostgreSQL 成员表配置；启动前需要执行随包提供的 SQL", "產生 PostgreSQL 成員表設定；啟動前需要執行套件提供的 SQL", "Generates PostgreSQL membership configuration; apply the packaged SQL before startup"),
        "redis" => L("生成 Redis 成员表配置；生产环境应启用持久化并禁止逐出键", "產生 Redis 成員表設定；生產環境應啟用持久化並禁止逐出鍵", "Generates Redis membership configuration; production Redis should persist data and never evict keys"),
        _ => L("生成 MySQL 成员表配置；启动前需要执行随包提供的 SQL", "產生 MySQL 成員表設定；啟動前需要執行套件提供的 SQL", "Generates MySQL membership configuration; apply the packaged SQL before startup")
    };
    public string ProjectWillBeCreatedAt => L("项目将创建到", "專案將建立於", "Project will be created at");
    public string Cancel => L("取消", "取消", "Cancel");
    public string ContinueCreating => L("继续创建", "繼續建立", "Create project");

    public string LanguageAndRegion => L("语言与区域", "語言與地區", "Language & region");
    public string DisplayLanguage => L("显示语言", "顯示語言", "Display language");
    public string DevelopmentEnvironment => L("开发环境", "開發環境", "Development environment");
    public string DevelopmentEnvironmentDescription => L("查看 .NET SDK 状态，并识别或手动设置受支持的开发工具。", "檢視 .NET SDK 狀態，並識別或手動設定受支援的開發工具。", "Review the .NET SDK and detect or manually configure supported development tools.");
    public string RuntimeStatus => L("运行环境", "執行環境", "Runtime");
    public string DetectingDotNetSdk => L("正在检测 .NET SDK", "正在偵測 .NET SDK", "Detecting .NET SDK");
    public string DetectingDotNetSdkDescription => L("正在检查 Hub 私有 SDK 和系统 SDK。", "正在檢查 Hub 私有 SDK 和系統 SDK。", "Checking Hub-managed and system SDK installations.");
    public string ManagedDotNetSdkReady => L("Hub 私有 .NET SDK 已就绪", "Hub 私有 .NET SDK 已就緒", "Hub-managed .NET SDK is ready");
    public string ManagedDotNetSdkDescription => L("项目操作使用 Hub 独立管理的 SDK，不修改系统 PATH。", "專案操作使用 Hub 獨立管理的 SDK，不修改系統 PATH。", "Project operations use the SDK managed privately by Hub without changing PATH.");
    public string SystemDotNetSdkReady => L("系统 .NET SDK 已就绪", "系統 .NET SDK 已就緒", "System .NET SDK is ready");
    public string SystemDotNetSdkDescription => L("检测到兼容的系统 .NET 10 SDK，无需额外下载。", "偵測到相容的系統 .NET 10 SDK，無需額外下載。", "A compatible system .NET 10 SDK was found; no download is required.");
    public string DotNetSdkMissing => L("需要安装 .NET 10 SDK", "需要安裝 .NET 10 SDK", ".NET 10 SDK is required");
    public string DotNetSdkMissingDescription => L("安装前 Hub 会请求确认，然后下载到用户目录。", "安裝前 Hub 會要求確認，然後下載到使用者目錄。", "Hub asks for confirmation before downloading the SDK into your user data directory.");
    public string InstallDotNetSdk => L("安装 SDK", "安裝 SDK", "Install SDK");
    public string DotNetSdkRequiredTitle => L("需要下载 .NET 10 SDK", "需要下載 .NET 10 SDK", "Download .NET 10 SDK");
    public string DotNetSdkRequiredDescription => L("未检测到兼容的 .NET 10 SDK。是否由 Lakona Hub 下载并安装私有 SDK？取消后仍可使用 Hub，但需要 SDK 的项目操作将不可用。", "未偵測到相容的 .NET 10 SDK。是否由 Lakona Hub 下載並安裝私有 SDK？取消後仍可使用 Hub，但需要 SDK 的專案操作將無法使用。", "No compatible .NET 10 SDK was found. Allow Lakona Hub to download and install a private SDK? You can cancel and continue using Hub, but project operations that require the SDK will be unavailable.");
    public string DotNetSdkDownloadSource => L("来源：Microsoft 官方 .NET 发布服务", "來源：Microsoft 官方 .NET 發佈服務", "Source: official Microsoft .NET release service");
    public string DotNetSdkInstallLocation => L("位置：Lakona Hub 用户数据目录（不修改系统 PATH）", "位置：Lakona Hub 使用者資料目錄（不修改系統 PATH）", "Location: Lakona Hub user data (system PATH is unchanged)");
    public string DownloadAndInstallSdk => L("下载并安装", "下載並安裝", "Download and install");
    public string ResolvingDotNetSdkDownload => L("正在获取官方 SDK 下载信息…", "正在取得官方 SDK 下載資訊…", "Resolving the official SDK download…");
    public string VerifyingDotNetSdk => L("正在验证 SDK 完整性…", "正在驗證 SDK 完整性…", "Verifying SDK integrity…");
    public string ExtractingDotNetSdk => L("正在安装 SDK…", "正在安裝 SDK…", "Installing SDK…");
    public string ValidatingDotNetSdk => L("正在验证 SDK 版本…", "正在驗證 SDK 版本…", "Validating SDK version…");
    public string DotNetSdkInstallComplete => L("SDK 安装完成。", "SDK 安裝完成。", "SDK installation complete.");
    public string UnknownSize => L("未知大小", "未知大小", "unknown size");
    public string DotNetSdkInstalled(string version) => L($".NET SDK {version} 已安装。", $".NET SDK {version} 已安裝。", $".NET SDK {version} was installed.");
    public string DotNetSdkInstallFailed(string message) => L($"SDK 安装失败：{message}", $"SDK 安裝失敗：{message}", $"SDK installation failed: {message}");
    public string DotNetSdkDetectionFailed(string message) => L($"无法检测 .NET SDK：{message}", $"無法偵測 .NET SDK：{message}", $"Could not detect the .NET SDK: {message}");
    public string ServerEditor => L("服务端 IDE", "伺服器端 IDE", "Server IDE");
    public string ServerEditorDescription => L("所有项目统一使用这个 IDE 打开服务端。", "所有專案統一使用這個 IDE 開啟伺服器端。", "Use this IDE to open the server for every project.");
    public string DetectedTools => L("开发工具", "開發工具", "Development tools");
    public string RefreshDetection => L("重新检测", "重新偵測", "Detect again");
    public string AddApplication => L("手动添加", "手動新增", "Add manually");
    public string Remove => L("移除", "移除", "Remove");
    public string EnvironmentReady => L("环境就绪", "環境就緒", "Ready");
    public string EnvironmentChecking => L("正在检查", "正在檢查", "Checking");
    public string EnvironmentNeedsSetup => L("需要设置", "需要設定", "Setup required");
    public string ApplicationUpdates => L("应用更新", "應用程式更新", "App updates");
    public string ApplicationUpdatesDescription => L("从 GitHub Releases 获取经过校验的更新；Linux 使用系统安装包。", "從 GitHub Releases 取得經過驗證的更新；Linux 使用系統安裝套件。", "Get verified updates from GitHub Releases. Linux uses native system packages.");
    public string CheckForUpdates => L("检查更新", "檢查更新", "Check for updates");
    public string DownloadAndInstall => L("下载并安装", "下載並安裝", "Download & install");
    public string Update => L("更新", "更新", "Update");
    public string UpdateCheckDescription => L("Hub 会在启动和返回窗口时自动检查新版本，但一小时内最多检查一次；下载并校验后交由系统安装器升级。", "Hub 會在啟動和返回視窗時自動檢查新版本，但一小時內最多檢查一次；下載並驗證後交由系統安裝程式升級。", "Hub automatically checks on startup and when you return, at most once per hour. Verified updates open in the system installer.");
    public string CheckingForUpdates => L("正在检查 GitHub Releases…", "正在檢查 GitHub Releases…", "Checking GitHub Releases…");
    public string NoUpdatesAvailable(string version) => L($"当前已是最新版本（{version}）。", $"目前已是最新版本（{version}）。", $"Lakona Hub is up to date ({version}).");
    public string CurrentHubVersion(string version) => L($"当前版本 {version}", $"目前版本 {version}", $"Current version {version}");
    public string SystemPackageUpdateAvailable(string version) => L($"发现版本 {version}；将下载适用于当前系统的安装包。", $"發現版本 {version}；將下載適用於目前系統的安裝套件。", $"Version {version} is available as an installer for this system.");
    public string DownloadingSystemPackage(string version) => L($"正在下载并校验 {version} 系统安装包…", $"正在下載並驗證 {version} 系統安裝套件…", $"Downloading and verifying system installer {version}…");
    public string DownloadProgress(double percentage, string received, string total) => L(
        $"{percentage:0}% · {received} / {total}",
        $"{percentage:0}% · {received} / {total}",
        $"{percentage:0}% · {received} / {total}");
    public string VerifyingSystemPackage => L("正在校验安装包…", "正在驗證安裝套件…", "Verifying installer…");
    public string OpeningSystemPackageInstaller => L("正在打开系统安装器…", "正在開啟系統安裝程式…", "Opening system installer…");
    public string InstallingSystemPackage => L("请在系统授权窗口中确认，正在安装更新…", "請在系統授權視窗中確認，正在安裝更新…", "Confirm the system authorization request. Installing update…");
    public string SystemPackageInstalled => L("更新安装成功，正在启动新版本。", "更新安裝成功，正在啟動新版本。", "The update was installed successfully. Starting the new version.");
    public string SystemPackageInstallerOpened => L("安装包已经校验并交给系统安装器；请确认授权，并在安装完成后重启 Hub。", "安裝套件已驗證並交給系統安裝程式；請確認授權，並在安裝完成後重新啟動 Hub。", "The verified package was opened in the system installer. Confirm authorization, then restart Hub after installation.");
    public string UpdateFailed(string message) => L($"更新失败：{message}", $"更新失敗：{message}", $"Update failed: {message}");

    public string SelectProjectFolder => L("选择 Lakona 项目目录", "選擇 Lakona 專案目錄", "Select a Lakona project folder");
    public string SelectOutputFolder => L("选择新项目的保存位置", "選擇新專案的儲存位置", "Select a location for the new project");
    public string SelectPackageOutputFolder => L("选择打包产物的保存位置", "選擇打包產物的儲存位置", "Select a location for package artifacts");
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
    public string ManuallyAddedTool(string? version) => string.IsNullOrWhiteSpace(version)
        ? L("手动添加", "手動新增", "Added manually")
        : L($"手动添加 · {version}", $"手動新增 · {version}", $"Added manually · {version}");
    public string SelectToolExecutable => L("选择要添加的工具", "選擇要新增的工具", "Select a tool executable");
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
    public string ApplicationAlreadyAdded(string application) => L(
        $"{application} 已在开发工具列表中。",
        $"{application} 已在開發工具清單中。",
        $"{application} is already in the development tools list.");
    public string ApplicationRemoved(string application) => L(
        $"已从开发工具列表移除 {application}。",
        $"已從開發工具清單移除 {application}。",
        $"Removed {application} from the development tools list.");
    public string ApplicationExecutableSaveFailed(string message) => L(
        $"无法保存工具路径：{message}",
        $"無法儲存工具路徑：{message}",
        $"Could not save the application path: {message}");
    public string UserSettingsSaveFailed(string message) => L(
        $"无法保存 Hub 设置：{message}",
        $"無法儲存 Hub 設定：{message}",
        $"Could not save Hub settings: {message}");
    public string ProjectReady => L("项目结构完整", "專案結構完整", "Project structure is complete");
    public string ProjectNeedsAttention => L("项目结构需要检查", "專案結構需要檢查", "Project structure needs attention");
    public string JustNow => L("刚刚", "剛剛", "Just now");
    public string NeverOpened => L("尚未打开", "尚未開啟", "Never opened");
    public string MinutesAgo(int minutes) => L($"{minutes} 分钟前", $"{minutes} 分鐘前", $"{minutes} minute{(minutes == 1 ? string.Empty : "s")} ago");
    public string HoursAgo(int hours) => L($"{hours} 小时前", $"{hours} 小時前", $"{hours} hour{(hours == 1 ? string.Empty : "s")} ago");
    public string DaysAgo(int days) => L($"{days} 天前", $"{days} 天前", $"{days} day{(days == 1 ? string.Empty : "s")} ago");
    public string Unknown => L("未识别", "未識別", "Unknown");
    public string NoServerIde => L("未识别可用的服务端 IDE", "未識別可用的伺服器端 IDE", "No server IDE was detected");
    public string OpenServerWith(string editor) => L($"使用 {editor} 打开服务端", $"使用 {editor} 開啟伺服器端", $"Open the server with {editor}");
    public string NoClientEditor(string client) => L($"未检测到可用于 {client} 的编辑器", $"未偵測到可用於 {client} 的編輯器", $"No editor was detected for {client}");
    public string OpenClientWith(string editor) => L($"使用 {editor} 打开客户端", $"使用 {editor} 開啟用戶端", $"Open the client with {editor}");
    public string EnvironmentNone => L("未识别任何开发工具", "未識別任何開發工具", "No development tools were detected");
    public string EnvironmentDetected(string names) => L($"已识别 {names}", $"已識別 {names}", $"Detected {names}");
    public string EnvironmentSeparator => L("、", "、", ", ");
    public string ToolDetectionError(string message) => L($"无法识别本机开发工具：{message}", $"無法識別本機開發工具：{message}", $"Could not detect local development tools: {message}");
    public string Imported(string name) => L($"已导入“{name}”。Hub 只读取了项目结构，没有写入任何管理文件。", $"已匯入「{name}」。Hub 僅讀取專案結構，未寫入任何管理檔案。", $"Imported “{name}”. Hub only read the project structure and did not write management files.");
    public string ImportedIncomplete(string name, int count) => L($"“{name}”已加入列表，但项目结构需要检查：{count} 项提示。", $"「{name}」已加入清單，但專案結構需要檢查：{count} 項提示。", $"“{name}” was added, but its structure needs attention: {count} issue(s).");
    public string NotLakonaProject => L("所选目录不是可识别的 Lakona 项目。项目内容没有被修改。", "所選目錄不是可識別的 Lakona 專案。專案內容未被修改。", "The selected folder is not a recognized Lakona project. No project files were changed.");
    public string ProjectNotFound => L("所选项目目录不存在。", "所選專案目錄不存在。", "The selected project folder does not exist.");
    public string ProjectUnrecognized => L("无法识别该项目。", "無法識別此專案。", "The project could not be recognized.");
    public string ProjectSelection(string name, string status, string path) => L($"{name}：{status}。路径：{path}", $"{name}：{status}。路徑：{path}", $"{name}: {status}. Path: {path}");
    public string ProjectRemoved(string name) => L($"已从 Hub 列表中移除“{name}”。项目文件未被修改。", $"已從 Hub 清單中移除「{name}」。專案檔案未被修改。", $"Removed “{name}” from the Hub list. Project files were not changed.");
    public string OpeningProjectFolder(string name) => L($"正在打开“{name}”所在的文件夹。", $"正在開啟「{name}」所在的資料夾。", $"Opening the folder containing “{name}”.");
    public string OpenProjectFolderFailed(string message) => L($"无法打开所在文件夹：{message}", $"無法開啟所在資料夾：{message}", $"Could not open the containing folder: {message}");
    public string NoSupportedIde => L("未识别可用的服务端 IDE。请先安装或手动添加一个 IDE。", "未識別可用的伺服器端 IDE。請先安裝或手動新增一個 IDE。", "No server IDE was detected. Install or manually add an IDE first.");
    public string OpeningServer(string editor, string project) => L($"正在使用 {editor} 打开“{project}”的服务端。", $"正在使用 {editor} 開啟「{project}」的伺服器端。", $"Opening the server for “{project}” with {editor}.");
    public string OpenServerFailed(string message) => L($"无法打开服务端：{message}", $"無法開啟伺服器端：{message}", $"Could not open the server: {message}");
    public string NoMatchingClientEditor => L("没有检测到与当前项目客户端匹配的 Unity、团结引擎或 Godot 编辑器。", "未偵測到與目前專案用戶端相符的 Unity、團結引擎或 Godot 編輯器。", "No Unity, Tuanjie, or Godot editor matching this project client was detected.");
    public string OpeningClient(string editor, string project) => L($"正在使用 {editor} 打开“{project}”的客户端。", $"正在使用 {editor} 開啟「{project}」的用戶端。", $"Opening the client for “{project}” with {editor}.");
    public string OpenClientFailed(string message) => L($"无法打开客户端：{message}", $"無法開啟用戶端：{message}", $"Could not open the client: {message}");
    public string CreatingProject(string name) => L($"正在创建“{name}”…", $"正在建立「{name}」…", $"Creating “{name}”…");
    public string ProjectCreationProgressTitle => L("正在创建项目", "正在建立專案", "Creating project");
    public string ProjectCreationProgressDescription => L("这可能需要几分钟，请不要关闭 Lakona Hub。", "這可能需要幾分鐘，請勿關閉 Lakona Hub。", "This may take a few minutes. Keep Lakona Hub open.");
    public string ProjectCreationPreparing => L("正在准备项目配置…", "正在準備專案設定…", "Preparing project configuration…");
    public string ProjectCreationRestoringDependencies => L("正在通过客户端编辑器恢复并验证依赖…", "正在透過用戶端編輯器還原並驗證相依套件…", "Restoring and verifying dependencies through the client editor…");
    public string ProjectCreationWritingFiles => L("正在写入项目文件…", "正在寫入專案檔案…", "Writing project files…");
    public string ProjectCreationInitializingGit => L("正在初始化 Git 仓库…", "正在初始化 Git 儲存庫…", "Initializing the Git repository…");
    public string ProjectCreationCompleting => L("正在完成创建…", "正在完成建立…", "Finishing project creation…");
    public string ProjectCreated(string name) => L($"已创建“{name}”。项目生成逻辑与 lakona-tool 完全共享。", $"已建立「{name}」。專案產生邏輯與 lakona-tool 完全共用。", $"Created “{name}”. Project generation is fully shared with lakona-tool.");
    public string ProjectCreationFailed(string message) => L($"创建项目失败：{message}", $"建立專案失敗：{message}", $"Project creation failed: {message}");
    public string HelpDialogDescription => L("问题反馈和功能建议将在 Lakona 的 GitHub Issues 页面中提交。是否打开该页面？", "問題回報和功能建議將在 Lakona 的 GitHub Issues 頁面中提交。是否開啟此頁面？", "Bug reports and feature requests are submitted on Lakona's GitHub Issues page. Open it now?");
    public string OpenGitHubIssues => L("前往 GitHub Issues", "前往 GitHub Issues", "Open GitHub Issues");
    public string OpenHelpPageFailed(string message) => L($"无法打开 GitHub Issues 页面：{message}", $"無法開啟 GitHub Issues 頁面：{message}", $"Could not open the GitHub Issues page: {message}");
    public string PreviousCrashTitle => L("Lakona Hub 上次异常退出", "Lakona Hub 上次異常結束", "Lakona Hub ended unexpectedly");
    public string PreviousCrashDescription => L("Hub 已保存一份脱敏诊断报告。是否打开预填的 GitHub Issue，帮助我们定位和修复问题？", "Hub 已儲存一份去識別化診斷報告。是否開啟預填的 GitHub Issue，協助我們定位並修正問題？", "Hub saved a redacted diagnostic report. Open a prefilled GitHub issue to help us diagnose and fix the problem?");
    public string PreviousCrashSummary(DateTimeOffset occurredAt, string activity) => L($"发生时间：{occurredAt:g}\n最后操作：{activity}", $"發生時間：{occurredAt:g}\n最後操作：{activity}", $"Occurred: {occurredAt:g}\nLast action: {activity}");
    public string CrashPrivacyNotice => L("报告包含 Hub 与系统版本、异常信息和堆栈；用户目录等路径已自动脱敏。提交前仍可在 GitHub 页面检查和编辑。", "報告包含 Hub 與系統版本、例外資訊和堆疊；使用者目錄等路徑已自動去識別化。提交前仍可在 GitHub 頁面檢查和編輯。", "The report includes Hub and OS versions, exception details, and a stack trace. User paths are redacted, and you can review or edit everything before submitting.");
    public string IgnoreCrashReport => L("不反馈", "不回報", "Don't report");
    public string SendCrashFeedback => L("反馈此问题", "回報此問題", "Report this problem");

    private string L(string simplifiedChinese, string traditionalChinese, string english) => Language switch
    {
        HubLanguage.SimplifiedChinese => simplifiedChinese,
        HubLanguage.TraditionalChinese => traditionalChinese,
        HubLanguage.English => english,
        _ => throw new ArgumentOutOfRangeException(nameof(Language), Language, null)
    };
}
