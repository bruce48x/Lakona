using System.Diagnostics;
using System.Text;

namespace Lakona.ProjectSystem.Packaging.Server;

internal sealed class DotNetCommandRunner : IDotNetCommandRunner
{
    private readonly string executablePath;

    public DotNetCommandRunner(string? executablePath = null)
    {
        this.executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? "dotnet"
            : executablePath;
    }

    public async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = CreateStartInfo(workingDirectory, arguments);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var process = (Process)state!;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            },
            process);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new DotNetCommandResult(process.ExitCode, output, error);
    }

    internal ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}
