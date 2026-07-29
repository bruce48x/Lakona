using System.Diagnostics;
using System.Text;
using Lakona.ProjectSystem.Generation.Execution;

namespace Lakona.ProjectSystem.Generation.Infrastructure;

internal sealed class GitCommandRunner : IGitCommandRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GitCommandResult(-1, "", $"Failed to start git: {ex.Message}");
        }

        if (process is null)
        {
            return new GitCommandResult(-1, "", "Failed to start git process.");
        }

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdOut.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdErr.AppendLine(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        return new GitCommandResult(
            process.ExitCode,
            stdOut.ToString().TrimEnd(),
            stdErr.ToString().TrimEnd());
    }
}
