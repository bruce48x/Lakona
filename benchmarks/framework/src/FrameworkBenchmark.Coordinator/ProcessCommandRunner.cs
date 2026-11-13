using System.Diagnostics;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

internal static class ProcessCommandRunner
{
    public static async Task<int> RunAsync(
        ProcessCommand command,
        string workingDirectory,
        string stdoutLogPath,
        string stderrLogPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutLogPath)!);
        Process process;
        try
        {
            process = Process.Start(ManagedServerProcess.CreateStartInfo(command, workingDirectory)) ??
                throw new BenchmarkToolException($"Failed to start driver '{command.FileName}'.");
        }
        catch (Exception ex) when (ex is not BenchmarkToolException)
        {
            throw new BenchmarkToolException($"Failed to start driver '{command.FileName}'.", ex);
        }

        using (process)
        {
            await using var stdoutLog = new StreamWriter(stdoutLogPath, append: false) { AutoFlush = true };
            await using var stderrLog = new StreamWriter(stderrLogPath, append: false) { AutoFlush = true };
            using var outputCancellation = new CancellationTokenSource();
            var stdoutPump = PumpAsync(process.StandardOutput, stdoutLog, outputCancellation.Token);
            var stderrPump = PumpAsync(process.StandardError, stderrLog, outputCancellation.Token);
            Exception? commandError = null;

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                commandError = new BenchmarkToolException($"Command '{command.FileName}' exceeded timeout {timeout}.", ex);
                throw commandError;
            }
            catch (Exception ex) { commandError = ex; throw; }
            finally
            {
                try { await FinishAsync(process, stdoutPump, stderrPump, outputCancellation).ConfigureAwait(false); }
                catch (Exception cleanupError) when (commandError is not null)
                {
                    throw new AggregateException("Benchmark command and cleanup failed.", commandError, cleanupError);
                }
            }

            return process.ExitCode;
        }
    }

    // A separate, finite cleanup grace still applies after a phase budget expires.
    internal static async Task FinishAsync(Process process, Task stdoutPump, Task stderrPump,
        CancellationTokenSource outputCancellation)
    {
        outputCancellation.CancelAfter(TimeSpan.FromSeconds(5));
        var errors = new List<Exception>();
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(outputCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { errors.Add(ex); }
        try { await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0)
            throw new AggregateException("Benchmark process or output cleanup failed within its five-second grace.", errors);
    }

    private static async Task PumpAsync(StreamReader reader, StreamWriter writer, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
