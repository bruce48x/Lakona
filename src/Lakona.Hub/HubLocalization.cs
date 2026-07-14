using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lakona.Hub;

public enum HubLanguage
{
    SimplifiedChinese,
    English
}

public sealed record HubLanguageOption(HubLanguage Language, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class HubLocalization : INotifyPropertyChanged
{
    public static readonly HubLanguageOption SimplifiedChinese = new(HubLanguage.SimplifiedChinese, "简体中文");
    public static readonly HubLanguageOption English = new(HubLanguage.English, "English");

    private HubLanguageOption selectedLanguage;

    public HubLocalization()
        : this(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? HubLanguage.SimplifiedChinese
            : HubLanguage.English)
    {
    }

    public HubLocalization(HubLanguage language)
    {
        selectedLanguage = OptionFor(language);
        Text = HubText.For(language);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<HubLanguageOption> LanguageOptions { get; } = [SimplifiedChinese, English];

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

    private static HubLanguageOption OptionFor(HubLanguage language) => language switch
    {
        HubLanguage.SimplifiedChinese => SimplifiedChinese,
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
    private bool Zh => Language == HubLanguage.SimplifiedChinese;

    public static HubText For(HubLanguage language) => new(language);

    public string Projects => Zh ? "项目" : "Projects";
    public string Settings => Zh ? "设置" : "Settings";
    public string HelpAndFeedback => Zh ? "帮助与反馈" : "Help & feedback";
    public string Minimize => Zh ? "最小化" : "Minimize";
    public string Maximize => Zh ? "最大化" : "Maximize";
    public string Close => Zh ? "关闭" : "Close";
    public string WelcomeTitle => Zh ? "欢迎使用 Lakona Hub" : "Welcome to Lakona Hub";
    public string WelcomeDescription => Zh ? "创建一个新的 Lakona 项目，或者导入现有项目开始工作。" : "Create a new Lakona project or import an existing one to get started.";
    public string CreateProject => Zh ? "创建项目" : "Create project";
    public string ImportExistingProject => Zh ? "导入现有项目" : "Import existing project";
    public string DotNetReady => Zh ? ".NET 10 环境已就绪" : ".NET 10 is ready";
    public string DetectingTools => Zh ? "正在识别本机开发工具…" : "Detecting development tools…";
    public string ToolDetectionFailed => Zh ? "本机开发工具识别失败" : "Development tool detection failed";
    public string MyProjects => Zh ? "我的项目" : "My projects";
    public string ProjectName => Zh ? "项目名称" : "Project name";
    public string EngineVersion => Zh ? "引擎版本" : "Engine version";
    public string LakonaVersion => Zh ? "Lakona 版本" : "Lakona version";
    public string LastOpened => Zh ? "上次打开" : "Last opened";
    public string OpenServer => Zh ? "打开服务端" : "Open server";
    public string OpenClient => Zh ? "打开客户端" : "Open client";
    public string Open => Zh ? "打开" : "Open";
    public string BackToProjects => Zh ? "返回项目列表" : "Back to projects";
    public string CreateDescription => Zh ? "填写项目配置，所有选项都可以直接查看和修改。" : "Configure the project. Every option is visible and editable.";
    public string BasicInformation => Zh ? "基本信息" : "Basic information";
    public string ProjectFolderHint => Zh ? "将同时作为项目文件夹名称" : "This is also used as the project folder name";
    public string OutputLocation => Zh ? "保存位置" : "Output location";
    public string Browse => Zh ? "浏览…" : "Browse…";
    public string OutputLocationHint => Zh ? "项目会创建在该目录下的新文件夹中" : "The project is created in a new folder under this location";
    public string ClientType => Zh ? "客户端类型" : "Client type";
    public string ClientTypeHint => Zh ? "可选择 Unity、团结引擎、Godot 或 Console" : "Choose Unity, Tuanjie, Godot, or Console";
    public string ClientVersion => Zh ? "客户端版本" : "Client version";
    public string ProjectConfiguration => Zh ? "项目配置" : "Project configuration";
    public string Transport => Zh ? "传输协议" : "Transport";
    public string DefaultKcp => Zh ? "默认 KCP" : "Default: KCP";
    public string Serializer => Zh ? "序列化方式" : "Serializer";
    public string DefaultMemoryPack => Zh ? "默认 MemoryPack" : "Default: MemoryPack";
    public string Persistence => Zh ? "持久化" : "Persistence";
    public string PersistenceHint => Zh ? "选择项目需要的数据库支持" : "Choose the database support required by the project";
    public string NuGetForUnitySource => Zh ? "NuGetForUnity 来源" : "NuGetForUnity source";
    public string DeploymentProfile => Zh ? "部署配置" : "Deployment profile";
    public string DeploymentProfileHint => Zh ? "可选生成 Docker Compose 配置" : "Optionally generate Docker Compose configuration";
    public string ProjectWillBeCreatedAt => Zh ? "项目将创建到" : "Project will be created at";
    public string Cancel => Zh ? "取消" : "Cancel";
    public string ContinueCreating => Zh ? "继续创建" : "Create project";

    public string SettingsDescription => Zh ? "管理 Lakona Hub 的显示语言和本机开发环境。" : "Manage the display language and local development environment.";
    public string LanguageAndRegion => Zh ? "语言与区域" : "Language & region";
    public string LanguageDescription => Zh ? "切换后立即应用到整个应用，测试时无需修改系统语言。" : "Changes apply immediately across the app without changing the system language.";
    public string DisplayLanguage => Zh ? "显示语言" : "Display language";
    public string DisplayLanguageHint => Zh ? "仅影响 Lakona Hub，不修改操作系统设置。" : "Affects Lakona Hub only and does not change operating system settings.";
    public string DevelopmentEnvironment => Zh ? "开发环境" : "Development environment";
    public string DevelopmentEnvironmentDescription => Zh ? "集中查看 .NET SDK 和受支持编辑器的识别状态。" : "Review the detected .NET SDK and supported editors in one place.";
    public string RuntimeStatus => Zh ? "运行环境" : "Runtime";
    public string RuntimeReadyTitle => Zh ? "内置 .NET 10 已就绪" : "Bundled .NET 10 is ready";
    public string RuntimeReadyDescription => Zh ? "项目操作使用 Hub 自带的 SDK，不依赖系统全局安装。" : "Project operations use Hub's bundled SDK and do not require a global installation.";
    public string DetectedTools => Zh ? "已识别的开发工具" : "Detected development tools";
    public string RefreshDetection => Zh ? "重新检测" : "Detect again";
    public string EnvironmentReady => Zh ? "环境就绪" : "Ready";

    public string SelectProjectFolder => Zh ? "选择 Lakona 项目目录" : "Select a Lakona project folder";
    public string SelectOutputFolder => Zh ? "选择新项目的保存位置" : "Select a location for the new project";
    public string ClientVersionHint(bool hasVersion) => hasVersion
        ? (Zh ? "选择客户端使用的编辑器版本" : "Choose the editor version used by the client")
        : (Zh ? "Console 客户端不需要引擎版本" : "Console clients do not require an engine version");
    public string NuGetForUnityHint(bool usesNuGetForUnity) => usesNuGetForUnity
        ? (Zh ? "Unity 系客户端的包获取方式" : "Package source for Unity-family clients")
        : (Zh ? "当前客户端不使用 NuGetForUnity" : "The selected client does not use NuGetForUnity");
    public string TargetPathMissing => Zh ? "请填写项目名称和保存位置" : "Enter a project name and output location";
    public string InvalidProjectPath => Zh ? "项目路径无效" : "The project path is invalid";
    public string ProjectNameRequired => Zh ? "请输入项目名称" : "Enter a project name";
    public string InvalidProjectName => Zh ? "项目名称包含不能用于文件夹的字符" : "The project name contains characters that cannot be used in a folder name";
    public string OutputLocationRequired => Zh ? "请选择保存位置" : "Choose an output location";
    public string FullOutputPathRequired => Zh ? "请选择完整的保存路径" : "Choose a fully qualified output path";
    public string InvalidOutputPath => Zh ? "保存路径无效" : "The output path is invalid";
    public string ClientVersionRequired => Zh ? "请选择客户端版本" : "Choose a client version";
    public string ConfigurationReady => Zh ? "配置完整，可以继续创建" : "Configuration is complete and ready";
    public string NoDatabase => Zh ? "不使用数据库" : "No database";
    public string NoDeploymentProfile => Zh ? "不生成部署配置" : "No deployment configuration";
    public string EmbeddedPackages => Zh ? "内置包源" : "Bundled packages";
    public string Tuanjie => Zh ? "团结引擎" : "Tuanjie";
    public string TuanjieVersion => Zh ? "团结引擎 1.6.7" : "Tuanjie 1.6.7";

    public string UnnamedProject => Zh ? "未命名项目" : "Unnamed project";
    public string NotDetected => Zh ? "未检测到" : "Not detected";
    public string ProjectReady => Zh ? "项目结构完整" : "Project structure is complete";
    public string ProjectNeedsAttention => Zh ? "项目结构需要检查" : "Project structure needs attention";
    public string JustNow => Zh ? "刚刚" : "Just now";
    public string Unknown => Zh ? "未识别" : "Unknown";
    public string ClientAction(string clientName) => Zh ? $"{clientName} 打开" : $"Open in {clientName}";
    public string OpenClientAction => Zh ? "打开客户端" : "Open client";
    public string NoServerIde => Zh ? "未检测到 Rider、Visual Studio 或 VS Code" : "Rider, Visual Studio, and VS Code were not detected";
    public string OpenServerWith(string editor) => Zh ? $"使用 {editor} 打开服务端" : $"Open the server with {editor}";
    public string NoClientEditor(string client) => Zh ? $"未检测到可用于 {client} 的编辑器" : $"No editor was detected for {client}";
    public string OpenClientWith(string editor) => Zh ? $"使用 {editor} 打开客户端" : $"Open the client with {editor}";
    public string EnvironmentNone => Zh ? "未识别 Rider、Visual Studio、VS Code、Unity 或 Godot" : "Rider, Visual Studio, VS Code, Unity, and Godot were not detected";
    public string EnvironmentDetected(string names) => Zh ? $"已识别 {names}" : $"Detected {names}";
    public string EnvironmentSeparator => Zh ? "、" : ", ";
    public string ToolDetectionError(string message) => Zh ? $"无法识别本机开发工具：{message}" : $"Could not detect local development tools: {message}";
    public string Imported(string name) => Zh ? $"已导入“{name}”。Hub 只读取了项目结构，没有写入任何管理文件。" : $"Imported “{name}”. Hub only read the project structure and did not write management files.";
    public string ImportedIncomplete(string name, int count) => Zh ? $"“{name}”已加入列表，但项目结构需要检查：{count} 项提示。" : $"“{name}” was added, but its structure needs attention: {count} issue(s).";
    public string NotLakonaProject => Zh ? "所选目录不是可识别的 Lakona 项目。项目内容没有被修改。" : "The selected folder is not a recognized Lakona project. No project files were changed.";
    public string ProjectNotFound => Zh ? "所选项目目录不存在。" : "The selected project folder does not exist.";
    public string ProjectUnrecognized => Zh ? "无法识别该项目。" : "The project could not be recognized.";
    public string ProjectSelection(string name, string status, string path) => Zh ? $"{name}：{status}。路径：{path}" : $"{name}: {status}. Path: {path}";
    public string NoSupportedIde => Zh ? "未检测到 Rider、Visual Studio 或 VS Code。请先安装一个受支持的 IDE。" : "Rider, Visual Studio, and VS Code were not detected. Install a supported IDE first.";
    public string OpeningServer(string editor, string project) => Zh ? $"正在使用 {editor} 打开“{project}”的服务端。" : $"Opening the server for “{project}” with {editor}.";
    public string OpenServerFailed(string message) => Zh ? $"无法打开服务端：{message}" : $"Could not open the server: {message}";
    public string NoMatchingClientEditor => Zh ? "没有检测到与当前项目客户端匹配的 Unity 或 Godot 编辑器。" : "No Unity or Godot editor matching this project client was detected.";
    public string OpeningClient(string editor, string project) => Zh ? $"正在使用 {editor} 打开“{project}”的客户端。" : $"Opening the client for “{project}” with {editor}.";
    public string OpenClientFailed(string message) => Zh ? $"无法打开客户端：{message}" : $"Could not open the client: {message}";
    public string CreatingProject(string name) => Zh ? $"正在创建“{name}”…" : $"Creating “{name}”…";
    public string ProjectCreated(string name) => Zh ? $"已创建“{name}”。项目生成逻辑与 lakona-tool 完全共享。" : $"Created “{name}”. Project generation is fully shared with lakona-tool.";
    public string ProjectCreationFailed(string message) => Zh ? $"创建项目失败：{message}" : $"Project creation failed: {message}";
    public string HelpComingSoon => Zh ? "帮助与反馈入口将在发布准备阶段接入。" : "Help and feedback will be connected during release preparation.";
}
