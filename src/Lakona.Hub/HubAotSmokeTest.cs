using Lakona.ProjectSystem;

namespace Lakona.Hub;

internal static class HubAotSmokeTest
{
    internal const string Argument = "--aot-smoke-test";
    internal static IReadOnlyList<string> BundledSkillNames { get; } =
    [
        "lakona-define-rpc-contract",
        "lakona-implement-actor",
        "lakona-implement-http-service",
        "lakona-implement-module",
        "lakona-implement-service",
        "lakona-implement-session-lifecycle",
        "lakona-implement-timer",
        "lakona-organize-server"
    ];

    public static bool IsRequested { get; private set; }

    public static string[] Capture(string[] args)
    {
        IsRequested = args.Length == 1 && string.Equals(args[0], Argument, StringComparison.Ordinal);
        return IsRequested ? [] : args;
    }

    public static async Task<int> RunAsync(MainWindow mainWindow)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"lakona-hub-aot-smoke-{Guid.NewGuid():N}");
        try
        {
            VerifyLocalization();
            VerifyLanguageSwitching(mainWindow);
            await VerifyProjectCreationAsync(temporaryRoot);
            Console.WriteLine("Lakona Hub NativeAOT smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lakona Hub NativeAOT smoke test failed: {ex}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                try
                {
                    DeleteTemporaryRoot(temporaryRoot);
                }
                catch (Exception cleanupError)
                {
                    Console.Error.WriteLine($"Lakona Hub NativeAOT smoke cleanup failed: {cleanupError}");
                }
            }
        }
    }

    private static void DeleteTemporaryRoot(string temporaryRoot)
    {
        foreach (var path in Directory.EnumerateFiles(temporaryRoot, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(temporaryRoot, recursive: true);
    }

    private static void VerifyLocalization()
    {
        foreach (var language in Enum.GetValues<HubLanguage>())
        {
            var text = new HubLocalization(language).Text;
            if (string.IsNullOrWhiteSpace(text.Projects) || string.IsNullOrWhiteSpace(text.Settings))
            {
                throw new InvalidOperationException($"Localization resources are incomplete for {language}.");
            }
        }
    }

    private static void VerifyLanguageSwitching(MainWindow mainWindow)
    {
        foreach (var language in Enum.GetValues<HubLanguage>())
        {
            mainWindow.Localization.SetLanguage(language);
        }
    }

    private static async Task VerifyProjectCreationAsync(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        var result = await new LakonaProjectCreator().CreateAsync(
            new LakonaProjectCreationRequest(
                "AotSmokeProject",
                outputRoot,
                LakonaClientEngine.Console));
        foreach (var skillName in BundledSkillNames)
        {
            var skillPath = Path.Combine(result.RootPath, ".agents", "skills", skillName, "SKILL.md");
            if (!File.Exists(skillPath))
            {
                throw new InvalidOperationException($"Bundled Agent Skill was not generated: {skillName}");
            }
        }
    }
}
