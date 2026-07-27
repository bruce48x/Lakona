using System.Globalization;
using Lakona.Tool.Domain;

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

            命令:
              lakona-tool new
                  交互式创建项目。会询问项目名称、客户端引擎、Unity 版本、传输协议、序列化器（输出目录、NuGetForUnity 来源、部署配置均可选，使用默认值）。

              lakona-tool new --name MyGame --client-engine unity [--client-engine-version 2022|6.0|6.3] --transport kcp --serializer memorypack [--output .] [--nugetforunity-source openupm] [--deploy-profile none]
                  用于脚本和 CI 的非交互式创建。输入被重定向时，缺少必填选项会失败。

              lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
                  打包自包含服务端 zip，并内置初始热更版本。

              lakona-tool version
                  显示版本号。

              lakona-tool help
                  显示此帮助。
            """,
        ToolLanguage.TraditionalChinese =>
            $$"""
            Lakona.Tool {{version}}

            命令:
              lakona-tool new
                  互動式建立專案。會詢問專案名稱、用戶端引擎、Unity 版本、傳輸協定、序列化器（輸出目錄、NuGetForUnity 來源、部署設定均可選，使用預設值）。

              lakona-tool new --name MyGame --client-engine unity [--client-engine-version 2022|6.0|6.3] --transport kcp --serializer memorypack [--output .] [--nugetforunity-source openupm] [--deploy-profile none]
                  用於指令碼和 CI 的非互動式建立。輸入被重新導向時，缺少必填選項會失敗。

              lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
                  打包自包含伺服器 zip，並內建初始熱更版本。

              lakona-tool version
                  顯示版本號。

              lakona-tool help
                  顯示此幫助。
            """,
        _ =>
            $$"""
            Lakona.Tool {{version}}

            Commands:
              lakona-tool new
                  Interactive project creation. Prompts for project name, client engine, Unity version, transport, and serializer (output directory, NuGetForUnity source, and deploy profile are optional with defaults).

              lakona-tool new --name MyGame --client-engine unity [--client-engine-version 2022|6.0|6.3] --transport kcp --serializer memorypack [--output .] [--nugetforunity-source openupm] [--deploy-profile none]
                  Non-interactive project creation for scripts and CI. Missing required choices fail when input is redirected.

              lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
                  Package a self-contained server zip with an installed initial hotfix version.

              lakona-tool version
                  Show the version number.

              lakona-tool help
                  Show this help.
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
                ? $"  5) 用团结引擎打开 Client/ (团结 {ClientEngineVersions.Tuanjie})"
                : $"  5) 在 Unity Hub 中打开 Client/（Unity {clientEngineVersion}）",
            ToolLanguage.TraditionalChinese => isConsole
                ? "  5) dotnet run --project \"Client/Client.csproj\" -- smoke"
                : isGodot
                ? "  5) 在 Godot Engine 中開啟 Client/"
                : isTuanjie
                ? $"  5) 用團結引擎開啟 Client/ (團結 {ClientEngineVersions.Tuanjie})"
                : $"  5) 在 Unity Hub 中開啟 Client/（Unity {clientEngineVersion}）",
            _ => isConsole
                ? "  5) dotnet run --project \"Client/Client.csproj\" -- smoke"
                : isGodot
                ? "  5) Open Client/ in Godot Engine"
                : isTuanjie
                ? $"  5) Open Client/ in Tuanjie Engine (Tuanjie {ClientEngineVersions.Tuanjie})"
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
