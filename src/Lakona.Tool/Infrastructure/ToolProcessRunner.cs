using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

internal sealed class ToolProcessRunner(ToolText? text = null)
{
    private readonly ToolText text = text ?? ToolText.Current;
    private const string StarterCommandName = "lakona-starter";
    private const string StarterPackageId = "Lakona.Rpc.Starter";

    public async Task<int> RunStarterNewAsync(string projectName, string outputDirectory, NewCommandOptions options)
    {
        var expectedVersion = ToolPackageVersions.ULinkRpcStarter;

        var installedVersion = await GetInstalledStarterVersionAsync().ConfigureAwait(false);
        if (installedVersion != null && !IsVersionAtLeast(installedVersion, expectedVersion))
        {
            Console.Error.WriteLine(text.StarterVersionMismatch(installedVersion, expectedVersion));
            var updateResult = await InstallOrUpdateStarterAsync().ConfigureAwait(false);
            if (updateResult is { Started: true, ExitCode: 0 })
            {
                Console.Error.WriteLine(text.StarterUpdated(expectedVersion));
            }
            else
            {
                Console.Error.WriteLine(text.UnableToUpdateStarter(StarterPackageId));
                Console.Error.WriteLine(text.InstallStarterBeforeNew);
                return updateResult.Started ? updateResult.ExitCode : 1;
            }
        }

        var arguments = new[]
        {
            "new",
            "--name", projectName,
            "--output", outputDirectory,
            "--client-engine", options.ClientEngine,
            "--transport", options.Transport,
            "--serializer", options.Serializer,
            "--nugetforunity-source", options.NuGetForUnitySource,
            "--no-next-steps"
        };

        var directInvocation = new ProcessInvocation(StarterCommandName, arguments, true);
        var directResult = await TryRunProcessAsync(directInvocation).ConfigureAwait(false);
        if (directResult.Started)
        {
            return directResult.ExitCode;
        }

        var localToolInvocation = new ProcessInvocation("dotnet", ["tool", "run", StarterCommandName, "--", .. arguments], true);
        var localToolResult = await TryRunProcessAsync(localToolInvocation).ConfigureAwait(false);
        if (localToolResult.Started && localToolResult.ExitCode == 0)
        {
            return 0;
        }

        Console.Error.WriteLine(text.InstallingStarter(StarterPackageId, expectedVersion));
        var installResult = await InstallOrUpdateStarterAsync().ConfigureAwait(false);
        if (!installResult.Started || installResult.ExitCode != 0)
        {
            Console.Error.WriteLine(text.UnableToInstallStarter(StarterPackageId));
            Console.Error.WriteLine(text.InstallStarterBeforeNew);
            return installResult.Started ? installResult.ExitCode : 1;
        }

        directResult = await TryRunProcessAsync(directInvocation).ConfigureAwait(false);
        if (directResult.Started)
        {
            return directResult.ExitCode;
        }

        Console.Error.WriteLine(text.UnableToLocateStarter);
        Console.Error.WriteLine(text.InstallStarterBeforeNew);
        return 1;
    }

    private async Task<string?> GetInstalledStarterVersionAsync()
    {
        try
        {
            var output = await RunProcessForOutputAsync(
                "dotnet",
                ["tool", "list", "--global"],
                Directory.GetCurrentDirectory()).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains(StarterPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"\d+\.\d+\.\d+");
                    if (match.Success)
                        return match.Value;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsVersionAtLeast(string installed, string expected)
    {
        if (Version.TryParse(installed, out var installedVer) &&
            Version.TryParse(expected, out var expectedVer))
        {
            return installedVer >= expectedVer;
        }

        return string.Equals(installed, expected, StringComparison.Ordinal);
    }

    private static async Task<ProcessRunResult> InstallOrUpdateStarterAsync()
    {
        var updateInvocation = new ProcessInvocation(
            "dotnet",
            ["tool", "update", "--global", StarterPackageId],
            true);

        var updateResult = await TryRunProcessAsync(updateInvocation).ConfigureAwait(false);
        if (updateResult.Started && updateResult.ExitCode == 0)
            return updateResult;

        var installInvocation = new ProcessInvocation(
            "dotnet",
            ["tool", "install", "--global", StarterPackageId],
            true);

        return await TryRunProcessAsync(installInvocation).ConfigureAwait(false);
    }

    private static async Task<ProcessRunResult> TryRunProcessAsync(ProcessInvocation invocation)
    {
        try
        {
            var exitCode = await RunProcessAsync(invocation.FileName, invocation.Arguments, Directory.GetCurrentDirectory()).ConfigureAwait(false);
            return new ProcessRunResult(true, exitCode);
        }
        catch (Win32Exception) when (invocation.CanFallback)
        {
            return ProcessRunResult.NotStarted;
        }
        catch (InvalidOperationException) when (invocation.CanFallback)
        {
            return ProcessRunResult.NotStarted;
        }
    }

    private static async Task<int> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task<string?> RunProcessForOutputAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return output;
        }
        catch
        {
            return null;
        }
    }
}

internal readonly record struct ProcessRunResult(bool Started, int ExitCode)
{
    public static ProcessRunResult NotStarted => new(false, 1);
}
