using System.Diagnostics;

namespace Lakona.Tool.Server;

internal sealed class DotNetCommandRunner : IDotNetCommandRunner
{
    public async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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
}
