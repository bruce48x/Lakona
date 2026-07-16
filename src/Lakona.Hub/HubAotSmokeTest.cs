namespace Lakona.Hub;

internal static class HubAotSmokeTest
{
    internal const string Argument = "--aot-smoke-test";

    public static bool IsRequested { get; private set; }

    public static string[] Capture(string[] args)
    {
        IsRequested = args.Length == 1 && string.Equals(args[0], Argument, StringComparison.Ordinal);
        return IsRequested ? [] : args;
    }

    public static Task<int> RunAsync()
    {
        try
        {
            VerifyLocalization();
            Console.WriteLine("Lakona Hub NativeAOT smoke test passed.");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lakona Hub NativeAOT smoke test failed: {ex}");
            return Task.FromResult(1);
        }
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
}
