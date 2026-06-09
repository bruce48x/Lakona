using System.Globalization;

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

    public string HelpText => Language switch
    {
        ToolLanguage.SimplifiedChinese =>
            """
            Lakona.Tool

            命令:
              lakona-tool new
                  交互式创建项目。会询问项目名称、客户端引擎、传输协议、序列化器（输出目录、持久化、NuGetForUnity 来源、部署配置均可选，使用默认值）。

              lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack [--output .] [--persistence none] [--nugetforunity-source openupm] [--deploy-profile none]
                  用于脚本和 CI 的非交互式创建。输入被重定向时，缺少必填选项会失败。

              lakona-tool help
                  显示此帮助。
            """,
        ToolLanguage.TraditionalChinese =>
            """
            Lakona.Tool

            命令:
              lakona-tool new
                  互動式建立專案。會詢問專案名稱、用戶端引擎、傳輸協定、序列化器（輸出目錄、持久化、NuGetForUnity 來源、部署設定均可選，使用預設值）。

              lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack [--output .] [--persistence none] [--nugetforunity-source openupm] [--deploy-profile none]
                  用於指令碼和 CI 的非互動式建立。輸入被重新導向時，缺少必填選項會失敗。

              lakona-tool help
                  顯示此幫助。
            """,
        _ =>
            """
            Lakona.Tool

            Commands:
              lakona-tool new
                  Interactive project creation. Prompts for project name, client engine, transport, and serializer (output directory, persistence, NuGetForUnity source, and deploy profile are optional with defaults).

              lakona-tool new --name MyGame --client-engine unity --transport kcp --serializer memorypack [--output .] [--persistence none] [--nugetforunity-source openupm] [--deploy-profile none]
                  Non-interactive project creation for scripts and CI. Missing required choices fail when input is redirected.

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

    public string PersistencePrompt => Language switch
    {
        ToolLanguage.SimplifiedChinese => "持久化",
        ToolLanguage.TraditionalChinese => "持久化",
        _ => "Persistence"
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

    public string OpenClientStep(string clientEngine)
    {
        var isGodot = string.Equals(clientEngine, "godot", StringComparison.OrdinalIgnoreCase);
        return Language switch
        {
            ToolLanguage.SimplifiedChinese => isGodot
                ? "  5) 在 Godot Engine 中打开 Client/"
                : "  5) 在 Unity Hub 中打开 Client/（Unity 2022 LTS）",
            ToolLanguage.TraditionalChinese => isGodot
                ? "  5) 在 Godot Engine 中開啟 Client/"
                : "  5) 在 Unity Hub 中開啟 Client/（Unity 2022 LTS）",
            _ => isGodot
                ? "  5) Open Client/ in Godot Engine"
                : "  5) Open Client/ in Unity Hub (Unity 2022 LTS)"
        };
    }

    public string CheckProjectStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  2) dotnet run --project \"Server/App/Server.App.csproj\" -- --lakona-game-check",
        ToolLanguage.TraditionalChinese => "  2) dotnet run --project \"Server/App/Server.App.csproj\" -- --lakona-game-check",
        _ => "  2) dotnet run --project \"Server/App/Server.App.csproj\" -- --lakona-game-check"
    };

    public string BuildSolutionStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  3) dotnet build \"Server/Server.slnx\"",
        ToolLanguage.TraditionalChinese => "  3) dotnet build \"Server/Server.slnx\"",
        _ => "  3) dotnet build \"Server/Server.slnx\""
    };

    public string StartServerStep => Language switch
    {
        ToolLanguage.SimplifiedChinese => "  4) dotnet run --project \"Server/App/Server.App.csproj\" --no-build",
        ToolLanguage.TraditionalChinese => "  4) dotnet run --project \"Server/App/Server.App.csproj\" --no-build",
        _ => "  4) dotnet run --project \"Server/App/Server.App.csproj\" --no-build"
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
