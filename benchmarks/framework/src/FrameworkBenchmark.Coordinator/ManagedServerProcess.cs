using System.Diagnostics;
using System.Text.Json;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

internal sealed class ManagedServerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly StreamWriter stdoutLog;
    private readonly StreamWriter stderrLog;
    private readonly Task stdoutPump;
    private readonly Task stderrPump;
    private readonly TaskCompletionSource<IReadOnlyDictionary<string, string>> readiness =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string role;
    private Exception? lifecycleFailure;
    private int readyEventCount;

    private ManagedServerProcess(
        Process process,
        StreamWriter stdoutLog,
        StreamWriter stderrLog,
        string role)
    {
        this.process = process;
        this.stdoutLog = stdoutLog;
        this.stderrLog = stderrLog;
        this.role = role;
        stdoutPump = PumpStdoutAsync();
        stderrPump = PumpStderrAsync();
    }

    public bool HasExited => process.HasExited;

    public int ExitCode => process.HasExited ? process.ExitCode : throw new InvalidOperationException("Process has not exited.");

    public static ManagedServerProcess Start(
        ProcessCommand command,
        string workingDirectory,
        string role,
        string stdoutLogPath,
        string stderrLogPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutLogPath)!);
        var stdoutLog = new StreamWriter(stdoutLogPath, append: false) { AutoFlush = true };
        var stderrLog = new StreamWriter(stderrLogPath, append: false) { AutoFlush = true };
        try
        {
            var process = Process.Start(CreateStartInfo(command, workingDirectory)) ??
                throw new BenchmarkToolException($"Failed to start server role '{role}'.");
            return new ManagedServerProcess(process, stdoutLog, stderrLog, role);
        }
        catch (Exception ex)
        {
            stdoutLog.Dispose();
            stderrLog.Dispose();
            throw new BenchmarkToolException($"Failed to start server role '{role}' using '{command.FileName}'.", ex);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> WaitForReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await readiness.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new BenchmarkToolException($"Server role '{role}' did not become ready within {timeout}.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            await stdoutLog.DisposeAsync().ConfigureAwait(false);
            await stderrLog.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void EnsureHealthy()
    {
        if (Volatile.Read(ref lifecycleFailure) is { } failure)
        {
            throw new BenchmarkToolException($"Server role '{role}' violated the lifecycle protocol.", failure);
        }

        if (process.HasExited)
        {
            throw new BenchmarkToolException(
                $"Server role '{role}' exited unexpectedly with code {process.ExitCode}.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(ProcessCommand command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (command.Environment is not null)
        {
            foreach (var pair in command.Environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return startInfo;
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                await stdoutLog.WriteLineAsync(line).ConfigureAwait(false);
                ReadLifecycleLine(line);
            }

            if (!readiness.Task.IsCompleted)
            {
                readiness.TrySetException(new BenchmarkToolException(
                    $"Server role '{role}' exited before readiness with code {process.ExitCode}."));
            }
        }
        catch (Exception ex)
        {
            SetLifecycleFailure(new BenchmarkToolException($"Failed to read stdout for server role '{role}'.", ex));
        }
    }

    private async Task PumpStderrAsync()
    {
        while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await stderrLog.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private void ReadLifecycleLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventName) ||
                !string.Equals(eventName.GetString(), "ready", StringComparison.Ordinal))
            {
                return;
            }

            if (Interlocked.Increment(ref readyEventCount) != 1)
            {
                SetLifecycleFailure(new BenchmarkToolException(
                    $"Server role '{role}' reported readiness more than once."));
                return;
            }

            var eventRole = root.GetProperty("role").GetString();
            if (!string.Equals(eventRole, role, StringComparison.Ordinal))
            {
                SetLifecycleFailure(new BenchmarkToolException(
                    $"Server role '{role}' reported readiness for '{eventRole}'."));
                return;
            }

            var endpoints = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("endpoints", out var endpointObject))
            {
                foreach (var property in endpointObject.EnumerateObject())
                {
                    endpoints.Add(property.Name, property.Value.GetString() ?? string.Empty);
                }
            }

            readiness.TrySetResult(endpoints);
        }
        catch (JsonException ex)
        {
            SetLifecycleFailure(new BenchmarkToolException(
                $"Server role '{role}' wrote malformed lifecycle JSON to stdout.", ex));
        }
        catch (InvalidOperationException ex)
        {
            SetLifecycleFailure(new BenchmarkToolException(
                $"Server role '{role}' wrote an invalid readiness event.", ex));
        }
    }

    private void SetLifecycleFailure(Exception failure)
    {
        Interlocked.CompareExchange(ref lifecycleFailure, failure, null);
        readiness.TrySetException(failure);
    }
}
