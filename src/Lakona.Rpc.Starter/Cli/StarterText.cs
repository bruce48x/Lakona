using System.Globalization;

namespace Lakona.Rpc.Starter;

internal enum StarterLanguage
{
    English,
    SimplifiedChinese,
    TraditionalChinese
}

internal sealed class StarterText
{
    private StarterText(StarterLanguage language)
    {
        Language = language;
    }

    public StarterLanguage Language { get; }

    public static StarterText Current => ForCulture(CultureInfo.CurrentUICulture);

    public static StarterText ForCulture(CultureInfo culture) => new(DetectLanguage(culture));

    public static StarterLanguage DetectLanguage(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.Length == 0)
            name = culture.TwoLetterISOLanguageName;

        var normalized = name.Replace('_', '-').ToLowerInvariant();
        if (!normalized.StartsWith("zh", StringComparison.Ordinal))
            return StarterLanguage.English;

        if (normalized.Contains("hant", StringComparison.Ordinal) ||
            normalized is "zh-cht" ||
            normalized is "zh-tw" or "zh-hk" or "zh-mo")
        {
            return StarterLanguage.TraditionalChinese;
        }

        return StarterLanguage.SimplifiedChinese;
    }

    public string UsageHeader => Language switch
    {
        StarterLanguage.SimplifiedChinese => "用法:",
        StarterLanguage.TraditionalChinese => "用法:",
        _ => "Usage:"
    };

    public string ClientEnginePrompt => Language switch
    {
        StarterLanguage.SimplifiedChinese => "请选择客户端引擎:",
        StarterLanguage.TraditionalChinese => "請選擇客戶端引擎:",
        _ => "Select client engine:"
    };

    public string TransportPrompt => Language switch
    {
        StarterLanguage.SimplifiedChinese => "请选择传输协议:",
        StarterLanguage.TraditionalChinese => "請選擇傳輸協定:",
        _ => "Select transport:"
    };

    public string SerializerPrompt => Language switch
    {
        StarterLanguage.SimplifiedChinese => "请选择序列化器:",
        StarterLanguage.TraditionalChinese => "請選擇序列化器:",
        _ => "Select serializer:"
    };

    public string NextStepsHeader => Language switch
    {
        StarterLanguage.SimplifiedChinese => "下一步:",
        StarterLanguage.TraditionalChinese => "下一步:",
        _ => "Next steps:"
    };

    public string InvalidClientEngineValue => Language switch
    {
        StarterLanguage.SimplifiedChinese => "--client-engine 的值无效。",
        StarterLanguage.TraditionalChinese => "--client-engine 的值無效。",
        _ => "Invalid --client-engine value."
    };

    public string InvalidTransportValue => Language switch
    {
        StarterLanguage.SimplifiedChinese => "--transport 的值无效。",
        StarterLanguage.TraditionalChinese => "--transport 的值無效。",
        _ => "Invalid --transport value."
    };

    public string InvalidSerializerValue => Language switch
    {
        StarterLanguage.SimplifiedChinese => "--serializer 的值无效。",
        StarterLanguage.TraditionalChinese => "--serializer 的值無效。",
        _ => "Invalid --serializer value."
    };

    public string InvalidNuGetForUnitySourceValue => Language switch
    {
        StarterLanguage.SimplifiedChinese => "--nugetforunity-source 的值无效。",
        StarterLanguage.TraditionalChinese => "--nugetforunity-source 的值無效。",
        _ => "Invalid --nugetforunity-source value."
    };

    public string EmptyName => Language switch
    {
        StarterLanguage.SimplifiedChinese => "--name 不能为空。",
        StarterLanguage.TraditionalChinese => "--name 不能為空。",
        _ => "--name cannot be empty."
    };

    public string ClientEngineOption(int number, ClientEngineKind clientEngine) =>
        $"  {number}) {GetClientEngineDisplayName(clientEngine)}";

    public string EnterRange(int first, int last) => Language switch
    {
        StarterLanguage.SimplifiedChinese => $"请输入 {first}-{last}。",
        StarterLanguage.TraditionalChinese => $"請輸入 {first}-{last}。",
        _ => $"Please enter {first}-{last}."
    };

    public string UnknownCommand(string command) => Language switch
    {
        StarterLanguage.SimplifiedChinese => $"未知命令: {command}",
        StarterLanguage.TraditionalChinese => $"未知命令: {command}",
        _ => $"Unknown command: {command}"
    };

    public string UnknownOrIncompleteOption(string option) => Language switch
    {
        StarterLanguage.SimplifiedChinese => $"未知或不完整的选项: {option}",
        StarterLanguage.TraditionalChinese => $"未知或不完整的選項: {option}",
        _ => $"Unknown or incomplete option: {option}"
    };

    public string CreatedProject(string rootPath) => Language switch
    {
        StarterLanguage.SimplifiedChinese => $"已创建 Lakona.Rpc 项目: {rootPath}",
        StarterLanguage.TraditionalChinese => $"已建立 Lakona.Rpc 專案: {rootPath}",
        _ => $"Created Lakona.Rpc project at: {rootPath}"
    };

    public string OpenClientStep(ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity or ClientEngineKind.UnityCn or ClientEngineKind.Tuanjie => Language switch
        {
            StarterLanguage.SimplifiedChinese => $"  3) 使用 {GetStarterClientLabel(clientEngine)} 打开 \"Client\"。",
            StarterLanguage.TraditionalChinese => $"  3) 使用 {GetStarterClientLabel(clientEngine)} 開啟 \"Client\"。",
            _ => $"  3) Open \"Client\" with {clientEngine.GetStarterClientLabel()}."
        },
        ClientEngineKind.Godot => Language switch
        {
            StarterLanguage.SimplifiedChinese => "  3) 使用 Godot 4.6 打开 \"Client\" 并构建 C# 方案。",
            StarterLanguage.TraditionalChinese => "  3) 使用 Godot 4.6 開啟 \"Client\" 並建置 C# 方案。",
            _ => "  3) Open \"Client\" with Godot 4.6 and build the C# solution."
        },
        ClientEngineKind.Console => Language switch
        {
            StarterLanguage.SimplifiedChinese => "  3) 运行 \"dotnet run --project Client/Client.csproj\" 启动 console 客户端。",
            StarterLanguage.TraditionalChinese => "  3) 執行 \"dotnet run --project Client/Client.csproj\" 啟動 console 客戶端。",
            _ => "  3) Run \"dotnet run --project Client/Client.csproj\" to start the console client."
        },
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    private string GetClientEngineDisplayName(ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => "Unity",
        ClientEngineKind.UnityCn => Language switch
        {
            StarterLanguage.SimplifiedChinese => "Unity 中国版",
            StarterLanguage.TraditionalChinese => "Unity 中國版",
            _ => "Unity CN"
        },
        ClientEngineKind.Tuanjie => Language switch
        {
            StarterLanguage.SimplifiedChinese => "团结引擎",
            StarterLanguage.TraditionalChinese => "團結引擎",
            _ => "Tuanjie"
        },
        ClientEngineKind.Godot => "Godot",
        ClientEngineKind.Console => ".NET Console",
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    private string GetStarterClientLabel(ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => "Unity 2022 LTS",
        ClientEngineKind.UnityCn => Language switch
        {
            StarterLanguage.SimplifiedChinese => "Unity 2022 LTS（中国网络环境默认配置）",
            StarterLanguage.TraditionalChinese => "Unity 2022 LTS（中國網路環境預設配置）",
            _ => clientEngine.GetStarterClientLabel()
        },
        ClientEngineKind.Tuanjie => Language switch
        {
            StarterLanguage.SimplifiedChinese => "团结引擎（Unity 兼容）",
            StarterLanguage.TraditionalChinese => "團結引擎（Unity 相容）",
            _ => clientEngine.GetStarterClientLabel()
        },
        ClientEngineKind.Godot => "Godot 4.6",
        ClientEngineKind.Console => ".NET console",
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };
}
