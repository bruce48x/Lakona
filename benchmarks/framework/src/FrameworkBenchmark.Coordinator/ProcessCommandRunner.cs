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
            var stdoutPump = PumpAsync(process.StandardOutput, stdoutLog);
            var stderrPump = PumpAsync(process.StandardError, stderrLog);

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                throw new BenchmarkToolException($"Driver '{command.FileName}' exceeded timeout {timeout}.", ex);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
            finally
            {
                await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);
            }

            return process.ExitCode;
        }
    }

    private static async Task PumpAsync(StreamReader reader, StreamWriter writer)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }
}
