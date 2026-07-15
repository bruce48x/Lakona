using System.Diagnostics;

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

    public static async Task<int> RunAsync()
    {
        try
        {
            VerifyLocalization();
            await VerifyBundledSdkAsync();
            Console.WriteLine($"Lakona Hub NativeAOT smoke test passed (.NET SDK {HubRuntimeInfo.BundledDotNetSdkVersion}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lakona Hub NativeAOT smoke test failed: {ex}");
            return 1;
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

    private static async Task VerifyBundledSdkAsync()
    {
        var executable = HubRuntimeInfo.BundledDotNetExecutablePath();
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The bundled .NET SDK executable is missing.", executable);
        }

        var startInfo = new ProcessStartInfo(executable, "--version")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the bundled .NET SDK.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The bundled .NET SDK exited with code {process.ExitCode}: {error}");
        }

        if (!string.Equals(output, HubRuntimeInfo.BundledDotNetSdkVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bundled .NET SDK version mismatch. Expected {HubRuntimeInfo.BundledDotNetSdkVersion}, got {output}.");
        }
    }
}
