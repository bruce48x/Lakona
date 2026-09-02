using System.Globalization;
using Lakona.ProjectSystem;
using Lakona.Tool.Cli.Text;

internal enum ToolLanguage
{
    English,
    SimplifiedChinese,
    TraditionalChinese
}

internal sealed class ToolText
{
    private ToolText(ToolLanguage language)
    {
        Language = language;
    }

    public ToolLanguage Language { get; }

    public static ToolText Current => ForCulture(CultureInfo.CurrentUICulture);

    public static ToolText ForCulture(CultureInfo culture) => new(DetectLanguage(culture));

    public static ToolLanguage DetectLanguage(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.Length == 0)
            name = culture.TwoLetterISOLanguageName;

        var normalized = name.Replace('_', '-').ToLowerInvariant();
        if (!normalized.StartsWith("zh", StringComparison.Ordinal))
            return ToolLanguage.English;

        if (normalized.Contains("hant", StringComparison.Ordinal) ||
            normalized is "zh-cht" ||
            normalized is "zh-tw" or "zh-hk" or "zh-mo")
        {
            return ToolLanguage.TraditionalChinese;
        }

        return ToolLanguage.SimplifiedChinese;
    }

    public string ErrorPrefix => Language switch
    {
        ToolLanguage.SimplifiedChinese => "错误",
        ToolLanguage.TraditionalChinese => "錯誤",
        _ => "Error"
    };

    public string RunHelpForUsage => Language switch
    {
        ToolLanguage.SimplifiedChinese => "运行 `lakona-tool help` 查看用法。",
        ToolLanguage.TraditionalChinese => "執行 `lakona-tool help` 查看用法。",
        _ => "Run `lakona-tool help` for usage."
    };

    public string HelpText(string version) => Language switch
    {
        ToolLanguage.SimplifiedChinese =>
            $$"""
            Lakona.Tool {{version}}

            用法:
              lakona-tool <命令> [选项]

            命令:
              lakona-tool new
                  交互式创建项目。会询问项目名称、客户端引擎、客户端引擎版本（仅 Unity）、传输协议和序列化器。

              lakona-tool init
                  `new` 的别名，接受相同的选项。

              lakona-tool new --name <名称> --client-engine <引擎> --transport <传输协议> --serializer <序列化器> [选项]
                  用于脚本和 CI 的非交互式创建。必填选项为 --name、--client-engine、--transport 和 --serializer。
                  --client-engine: unity|tuanjie|godot|console
                  --client-engine-version: Unity 使用 2022|6.0|6.3；Tuanjie 使用 1.6.7；Godot 使用 4.6；Console 不适用。
                  --transport: websocket|tcp|kcp（默认 kcp）
                  --serializer: json|memorypack（默认 memorypack）
                  --membership-provider: memory|postgres|redis|mysql（默认 memory）
                  --output <目录>（默认当前目录）、--nugetforunity-source embedded|openupm（默认 openupm）、--deploy-profile none|compose（默认 none）

              lakona-tool server pack --runtime <RID> [--configuration <配置>] [--output <目录>] [--project <路径>] [--hotfix-project <路径>]
                  打整包：创建包含初始热更版本的自包含服务端 zip。--runtime 必填。
                  默认值：--configuration Release、--output Server/Build、--project Server/App/Server.App.csproj、--hotfix-project Server/Hotfix/Server.Hotfix.csproj。
                  包版本由打包时间自动生成，不接受 --version。

              lakona-tool hotfix pack [--project <路径>] [--output <目录>] [--configuration <配置>]
                  打热更包。默认值：--project Server/Hotfix/Server.Hotfix.csproj、--output Server/Build、--configuration Release。
                  热更包版本由打包时间自动生成，不接受 --version。

              lakona-tool hotfix install <热更包路径> [--root <目录>]
                  将热更包安装到节点本地热更目录。--root 默认 hotfix。

              lakona-tool hotfix activate <版本> [--server <URL>] [--expected-current-version <版本>]
                  通过服务端回环管理端点激活指定热更版本。--server 默认 http://127.0.0.1:20080。

              lakona-tool hotfix status [--server <URL>]
                  查询当前热更状态。--server 默认 http://127.0.0.1:20080。

              lakona-tool hotfix rollback [--server <URL>]
                  回滚当前热更版本。--server 默认 http://127.0.0.1:20080。

              lakona-tool version | --version
                  显示版本号。

              lakona-tool help | --help | -h
                  显示此帮助。将 --help 或 -h 放在命令后也会显示此帮助。

            appsettings.json 配置权威文档:
              https://github.com/bruce48x/Lakona/blob/main/docs/configuration.md
            """,
        ToolLanguage.TraditionalChinese =>
            $$"""
            Lakona.Tool {{version}}

            用法:
              lakona-tool <命令> [選項]

            命令:
              lakona-tool new
                  互動式建立專案。會詢問專案名稱、用戶端引擎、用戶端引擎版本（僅 Unity）、傳輸協定和序列化器。

              lakona-tool init
                  `new` 的別名，接受相同的選項。

              lakona-tool new --name <名稱> --client-engine <引擎> --transport <傳輸協定> --serializer <序列化器> [選項]
                  用於指令碼和 CI 的非互動式建立。必填選項為 --name、--client-engine、--transport 和 --serializer。
                  --client-engine: unity|tuanjie|godot|console
                  --client-engine-version: Unity 使用 2022|6.0|6.3；Tuanjie 使用 1.6.7；Godot 使用 4.6；Console 不適用。
                  --transport: websocket|tcp|kcp（預設 kcp）
                  --serializer: json|memorypack（預設 memorypack）
                  --membership-provider: memory|postgres|redis|mysql（預設 memory）
                  --output <目錄>（預設目前目錄）、--nugetforunity-source embedded|openupm（預設 openupm）、--deploy-profile none|compose（預設 none）

              lakona-tool server pack --runtime <RID> [--configuration <設定>] [--output <目錄>] [--project <路徑>] [--hotfix-project <路徑>]
                  打整包：建立包含初始熱更版本的自包含伺服器 zip。--runtime 必填。
                  預設值：--configuration Release、--output Server/Build、--project Server/App/Server.App.csproj、--hotfix-project Server/Hotfix/Server.Hotfix.csproj。
                  套件版本由打包時間自動產生，不接受 --version。

              lakona-tool hotfix pack [--project <路徑>] [--output <目錄>] [--configuration <設定>]
                  打熱更包。預設值：--project Server/Hotfix/Server.Hotfix.csproj、--output Server/Build、--configuration Release。
                  熱更包版本由打包時間自動產生，不接受 --version。

              lakona-tool hotfix install <熱更包路徑> [--root <目錄>]
                  將熱更包安裝到節點本地熱更目錄。--root 預設 hotfix。

              lakona-tool hotfix activate <版本> [--server <URL>] [--expected-current-version <版本>]
                  透過伺服器回環管理端點啟用指定熱更版本。--server 預設 http://127.0.0.1:20080。

              lakona-tool hotfix status [--server <URL>]
                  查詢目前熱更狀態。--server 預設 http://127.0.0.1:20080。

              lakona-tool hotfix rollback [--server <URL>]
                  回滾目前熱更版本。--server 預設 http://127.0.0.1:20080。

              lakona-tool version | --version
                  顯示版本號。

              lakona-tool help | --help | -h
                  顯示此幫助。將 --help 或 -h 放在命令後也會顯示此幫助。

            appsettings.json 設定權威文件:
              https://github.com/bruce48x/Lakona/blob/main/docs/configuration.md
            """,
        _ =>
            $$"""
            Lakona.Tool {{version}}

            Usage:
              lakona-tool <command> [options]

            Commands:
              lakona-tool new
                  Interactive project creation. Prompts for project name, client engine, client engine version (Unity only), transport, and serializer.

              lakona-tool init
                  Alias for `new`; accepts the same options.

              lakona-tool new --name <name> --client-engine <engine> --transport <transport> --serializer <serializer> [options]
                  Non-interactive project creation for scripts and CI. Required: --name, --client-engine, --transport, and --serializer.
                  --client-engine: unity|tuanjie|godot|console
                  --client-engine-version: 2022|6.0|6.3 for Unity; 1.6.7 for Tuanjie; 4.6 for Godot; not applicable to Console.
                  --transport: websocket|tcp|kcp (default kcp)
                  --serializer: json|memorypack (default memorypack)
                  --membership-provider: memory|postgres|redis|mysql (default memory)
                  --output <directory> (default current directory), --nugetforunity-source embedded|openupm (default openupm), --deploy-profile none|compose (default none)

              lakona-tool server pack --runtime <RID> [--configuration <configuration>] [--output <directory>] [--project <path>] [--hotfix-project <path>]
                  Create a full package: a self-contained server zip with an installed initial hotfix version. --runtime is required.
                  Defaults: --configuration Release, --output Server/Build, --project Server/App/Server.App.csproj, and --hotfix-project Server/Hotfix/Server.Hotfix.csproj.
                  The package version is generated from packaging time; --version is not accepted.

              lakona-tool hotfix pack [--project <path>] [--output <directory>] [--configuration <configuration>]
                  Create a hotfix package. Defaults: --project Server/Hotfix/Server.Hotfix.csproj, --output Server/Build, and --configuration Release.
                  The hotfix package version is generated from packaging time; --version is not accepted.

              lakona-tool hotfix install <hotfix-package-path> [--root <directory>]
                  Install a hotfix package into the node-local hotfix root. --root defaults to hotfix.

              lakona-tool hotfix activate <version> [--server <URL>] [--expected-current-version <version>]
                  Activate a hotfix version through the server's loopback admin endpoint. --server defaults to http://127.0.0.1:20080.

              lakona-tool hotfix status [--server <URL>]
                  Show the current hotfix status. --server defaults to http://127.0.0.1:20080.

              lakona-tool hotfix rollback [--server <URL>]
                  Roll back the current hotfix version. --server defaults to http://127.0.0.1:20080.

              lakona-tool version | --version
                  Show the version number.

              lakona-tool help | --help | -h
                  Show this help. Add --help or -h after a command to show this help.

            appsettings.json configuration reference:
              https://github.com/bruce48x/Lakona/blob/main/docs/configuration.md
            """
    };

    public string InteractiveNewHeader => Language switch
    {
        ToolLanguage.SimplifiedChinese => "创建 Lakona.Game 项目。按 Enter 使用括号中的默认值。",
        ToolLanguage.TraditionalChinese => "建立 Lakona.Game 專案。按 Enter 使用括號中的預設值。",
        _ => "Create a Lakona.Game project. Press Enter to use the default in parentheses."
    };

    public string ProjectNamePrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "项目名称",
        ToolLanguage.TraditionalChinese => "專案名稱",
        _ => "Project name"
    };

    public string OutputDirectoryPrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "输出目录",
        ToolLanguage.TraditionalChinese => "輸出目錄",
        _ => "Output directory"
    };

    public string ClientEnginePrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "客户端引擎",
        ToolLanguage.TraditionalChinese => "用戶端引擎",
        _ => "Client engine"
    };

    public string ClientEngineVersionPrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "客户端引擎版本",
        ToolLanguage.TraditionalChinese => "用戶端引擎版本",
        _ => "Client engine version"
    };

    public string TransportPrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "传输协议",
        ToolLanguage.TraditionalChinese => "傳輸協定",
        _ => "Transport"
    };

    public string SerializerPrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "序列化器",
        ToolLanguage.TraditionalChinese => "序列化器",
        _ => "Serializer"
    };

    public string NuGetForUnitySourcePrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "NuGetForUnity 来源",
        ToolLanguage.TraditionalChinese => "NuGetForUnity 來源",
        _ => "NuGetForUnity source"
    };

    public string DeployProfilePrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "部署配置",
        ToolLanguage.TraditionalChinese => "部署設定",
        _ => "Deploy profile"
    };

    public string InvalidSelection(string value, int max) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"无效选择 '{value}'。请输入 1 到 {max} 之间的数字。",
        ToolLanguage.TraditionalChinese => $"無效選擇 '{value}'。請輸入 1 到 {max} 之間的數字。",
        _ => $"Invalid selection '{value}'. Enter a number from 1 to {max}."
    };

    public string MissingNonInteractiveNewOptions => Language switch
    {
        ToolLanguage.SimplifiedChinese =>
            "非交互式创建项目缺少必要选项。必填: --name, --client-engine, --transport, --serializer。示例: lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack",
        ToolLanguage.TraditionalChinese =>
            "非互動式建立專案缺少必要選項。必填: --name, --client-engine, --transport, --serializer。範例: lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack",
        _ =>
            "Missing required options for non-interactive project creation. Required: --name, --client-engine, --transport, --serializer. Example: lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack"
    };

    public string UnknownCommand(string command) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"未知命令: {command}",
        ToolLanguage.TraditionalChinese => $"未知命令: {command}",
        _ => $"Unknown command: {command}"
    };

    public string MissingValue(string optionName) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"{optionName} 缺少值。",
        ToolLanguage.TraditionalChinese => $"{optionName} 缺少值。",
        _ => $"Missing value for {optionName}."
    };

    public string UnsupportedValue(string value, string optionName, IReadOnlyCollection<string> supportedValues, string? suggestion)
    {
        var message = Language switch
        {
            ToolLanguage.SimplifiedChinese => $"{optionName} 不支持值 '{value}'。应为以下之一: {string.Join("|", supportedValues)}。",
            ToolLanguage.TraditionalChinese => $"{optionName} 不支援值 '{value}'。應為以下之一: {string.Join("|", supportedValues)}。",
            _ => $"Unsupported value '{value}' for {optionName}. Expected one of: {string.Join("|", supportedValues)}."
        };

        return suggestion is null ? message : $"{message} {DidYouMeanValue(suggestion)}";
    }

    public string UnsupportedClientEngineVersion(
        string value,
        string clientEngine,
        IReadOnlyCollection<string> supportedValues)
    {
        if (supportedValues.Count == 0)
        {
            return Language switch
            {
                ToolLanguage.SimplifiedChinese => $"--client-engine-version 不适用于客户端引擎 '{clientEngine}'。",
                ToolLanguage.TraditionalChinese => $"--client-engine-version 不適用於用戶端引擎 '{clientEngine}'。",
                _ => $"--client-engine-version does not apply to client engine '{clientEngine}'."
            };
        }

        return Language switch
        {
            ToolLanguage.SimplifiedChinese => $"客户端引擎 '{clientEngine}' 不支持版本 '{value}'。应为以下之一: {string.Join("|", supportedValues)}。",
            ToolLanguage.TraditionalChinese => $"用戶端引擎 '{clientEngine}' 不支援版本 '{value}'。應為以下之一: {string.Join("|", supportedValues)}。",
            _ => $"Client engine '{clientEngine}' does not support version '{value}'. Expected one of: {string.Join("|", supportedValues)}."
        };
    }

    public string UnexpectedArgument(string argument) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"意外参数: {argument}。",
        ToolLanguage.TraditionalChinese => $"非預期參數: {argument}。",
        _ => $"Unexpected argument: {argument}."
    };

    public string UnsupportedOption(string argument, string? suggestion)
    {
        var message = Language switch
        {
            ToolLanguage.SimplifiedChinese => $"不支持的选项: {argument}。",
            ToolLanguage.TraditionalChinese => $"不支援的選項: {argument}。",
            _ => $"Unsupported option: {argument}."
        };

        return suggestion is null ? message : $"{message} {DidYouMeanOption(suggestion)}";
    }

    public string GeneratedProjectRootNotFound(string projectRoot) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"未找到生成的项目根目录: {projectRoot}",
        ToolLanguage.TraditionalChinese => $"找不到生成的專案根目錄: {projectRoot}",
        _ => $"Generated project root not found: {projectRoot}"
    };

    public string ConfigAlreadyExists(string configPath) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"配置已存在: {configPath}",
        ToolLanguage.TraditionalChinese => $"設定已存在: {configPath}",
        _ => $"Config already exists: {configPath}"
    };

    public string CreatedToolConfig(string configPath) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"已创建工具配置: {configPath}",
        ToolLanguage.TraditionalChinese => $"已建立工具設定: {configPath}",
        _ => $"Created tool config: {configPath}"
    };

    public string NewProjectReadyHeader => Language switch
    {
        ToolLanguage.SimplifiedChinese => "Lakona.Game 项目已就绪。下一步:",
        ToolLanguage.TraditionalChinese => "Lakona.Game 專案已就緒。下一步:",
        _ => "Lakona.Game project ready. Next steps:"
    };

    public string OpenClientStep(string clientEngine, string? clientEngineVersion)
    {
        var isGodot = string.Equals(clientEngine, "godot", StringComparison.OrdinalIgnoreCase);
        var isConsole = string.Equals(clientEngine, "console", StringComparison.OrdinalIgnoreCase);
        var isTuanjie = string.Equals(clientEngine, "tuanjie", StringComparison.OrdinalIgnoreCase);
        return Language switch
        {
            ToolLanguage.SimplifiedChinese => isConsole
                ? "  5) dotnet run --project \"Client/Client.csproj\" -- smoke"
                : isGodot
                ? "  5) 在 Godot Engine 中打开 Client/"
                : isTuanjie
                ? $"  5) 用团结引擎打开 Client/ (团结 {LakonaProjectOptionText.ToCliValue(LakonaClientEngineVersion.Tuanjie167)})"
                : $"  5) 在 Unity Hub 中打开 Client/（Unity {clientEngineVersion}）",
            ToolLanguage.TraditionalChinese => isConsole
                ? "  5) dotnet run --project \"Client/Client.csproj\" -- smoke"
                : isGodot
                ? "  5) 在 Godot Engine 中開啟 Client/"
                : isTuanjie
                ? $"  5) 用團結引擎開啟 Client/ (團結 {LakonaProjectOptionText.ToCliValue(LakonaClientEngineVersion.Tuanjie167)})"
                : $"  5) 在 Unity Hub 中開啟 Client/（Unity {clientEngineVersion}）",
            _ => isConsole
                ? "  5) dotnet run --project \"Client/Client.csproj\" -- smoke"
                : isGodot
                ? "  5) Open Client/ in Godot Engine"
                : isTuanjie
                ? $"  5) Open Client/ in Tuanjie Engine (Tuanjie {LakonaProjectOptionText.ToCliValue(LakonaClientEngineVersion.Tuanjie167)})"
                : $"  5) Open Client/ in Unity Hub (Unity {clientEngineVersion})"
        };
    }

    public string CheckProjectStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  4) curl http://127.0.0.1:20080/_lakona/health/ready",
        ToolLanguage.TraditionalChinese => "  4) curl http://127.0.0.1:20080/_lakona/health/ready",
        _ => "  4) curl http://127.0.0.1:20080/_lakona/health/ready"
    };

    public string BuildSolutionStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  2) dotnet build \"Server/Server.slnx\"",
        ToolLanguage.TraditionalChinese => "  2) dotnet build \"Server/Server.slnx\"",
        _ => "  2) dotnet build \"Server/Server.slnx\""
    };

    public string StartServerStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  3) dotnet run --project \"Server/App/Server.App.csproj\" --no-build",
        ToolLanguage.TraditionalChinese => "  3) dotnet run --project \"Server/App/Server.App.csproj\" --no-build",
        _ => "  3) dotnet run --project \"Server/App/Server.App.csproj\" --no-build"
    };

    public string GitStatusInitializedAndCommitted => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 已初始化仓库并创建初始提交",
        ToolLanguage.TraditionalChinese => "git: 已初始化儲存庫並建立初始提交",
        _ => "git: initialized repository and created initial commit"
    };

    public string GitStatusInitializedNoCommitMissingIdentity => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 已初始化仓库；由于 user.name 或 user.email 未配置，跳过初始提交",
        ToolLanguage.TraditionalChinese => "git: 已初始化儲存庫；由於 user.name 或 user.email 未設定，跳過初始提交",
        _ => "git: initialized repository; initial commit skipped because user.name or user.email is not configured"
    };

    public string GitStatusInitializedNoCommitNoFiles => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 已初始化仓库；由于没有要提交的文件，跳过初始提交",
        ToolLanguage.TraditionalChinese => "git: 已初始化儲存庫；由於沒有要提交的檔案，跳過初始提交",
        _ => "git: initialized repository; initial commit skipped because there are no files to commit"
    };

    public string GitStatusSkippedParentWorktree => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 由于项目位于已有 Git 工作树中，跳过初始化",
        ToolLanguage.TraditionalChinese => "git: 由於專案位於已有 Git 工作樹中，跳過初始化",
        _ => "git: skipped because the project is inside an existing Git worktree"
    };

    public string GitStatusSkippedAlreadyCommitted => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 由于项目根目录已有 Git 提交，跳过初始化",
        ToolLanguage.TraditionalChinese => "git: 由於專案根目錄已有 Git 提交，跳過初始化",
        _ => "git: skipped because the project root already has Git commits"
    };

    public string GitStatusSkippedGitUnavailable => Language switch
    {
        ToolLanguage.SimplifiedChinese => "git: 由于 Git 不可用，跳过初始化",
        ToolLanguage.TraditionalChinese => "git: 由於 Git 不可用，跳過初始化",
        _ => "git: skipped because Git is not available"
    };

    public string GitStatusInitFailed(string reason) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"git: 初始化失败: {reason}",
        ToolLanguage.TraditionalChinese => $"git: 初始化失敗: {reason}",
        _ => $"git: initialization failed: {reason}"
    };

    public string GitStatusCommitFailed(string reason) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"git: 提交失败: {reason}",
        ToolLanguage.TraditionalChinese => $"git: 提交失敗: {reason}",
        _ => $"git: commit failed: {reason}"
    };

    public string TargetDirectoryNotEmpty(string path) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"目标目录已存在且不为空: {path}",
        ToolLanguage.TraditionalChinese => $"目標目錄已存在且不為空: {path}",
        _ => $"Target directory already exists and is not empty: {path}"
    };

    public string UnableToDetermineParentDirectory(string path) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"无法确定目标路径的父目录: {path}",
        ToolLanguage.TraditionalChinese => $"無法確定目標路徑的父目錄: {path}",
        _ => $"Unable to determine parent directory for target path: {path}"
    };

    public string GenerationFailedWithCleanupError(string error, string stagingPath, string cleanupError) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"项目生成失败: {error}{Environment.NewLine}清理临时目录 '{stagingPath}' 也失败: {cleanupError}",
        ToolLanguage.TraditionalChinese => $"專案生成失敗: {error}{Environment.NewLine}清理暫存目錄 '{stagingPath}' 也失敗: {cleanupError}",
        _ => $"Project generation failed: {error}{Environment.NewLine}Cleanup of staging directory '{stagingPath}' also failed: {cleanupError}"
    };

    private string DidYouMeanValue(string suggestion) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"你是否想输入 '{suggestion}'?",
        ToolLanguage.TraditionalChinese => $"你是否想輸入 '{suggestion}'?",
        _ => $"Did you mean '{suggestion}'?"
    };

    private string DidYouMeanOption(string suggestion) => Language switch
    {
        ToolLanguage.SimplifiedChinese => $"你是否想输入 {suggestion}?",
        ToolLanguage.TraditionalChinese => $"你是否想輸入 {suggestion}?",
        _ => $"Did you mean {suggestion}?"
    };
}
