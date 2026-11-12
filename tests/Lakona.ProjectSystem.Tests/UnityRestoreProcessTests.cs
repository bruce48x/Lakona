using System.Diagnostics;
using Xunit;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Tests;

public sealed class UnityRestoreProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_restore_stops_editor_cleans_workspace_and_terminates_child(bool timeout)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        readiness.CancelAfter(TimeSpan.FromSeconds(30));
        string root = "";
        Process? editor = null;
        Process? child = null;
        var restorer = new UnityDependencyRestorer(info =>
        {
            root = info.ArgumentList[info.ArgumentList.IndexOf("-projectPath") + 1];
            var start = PowerShell("""
                $p = [Diagnostics.Process]::Start((Get-Process -Id $PID).Path, '-NoProfile -Command Start-Sleep 120')
                [IO.File]::WriteAllText('child.tmp', $p.Id.ToString())
                [IO.File]::Move('child.tmp', 'child.ready')
                Start-Sleep 120
                """);
            start.WorkingDirectory = root;
            var process = Process.Start(start)!;
            editor = Process.GetProcessById(process.Id);
            return process;
        }, timeout ? TimeSpan.FromSeconds(8) : TimeSpan.FromMinutes(2));
        var plan = new GenerationPlan("", [
            new("Client/ProjectSettings/ProjectVersion.txt", ""),
            new("Client/Assets/packages.config", "<packages/>"),
            new("Client/Assets/NuGet.config", "<configuration/>")
        ], [], []);
        var restore = restorer.RestoreAsync(plan, new UnityEditorInstallation("test-editor", "", null), cancel.Token);
        try
        {
            var ready = Path.Combine(root, "child.ready");
            while (!File.Exists(ready))
            {
                if (restore.IsCompleted) await restore;
                await Task.Delay(25, readiness.Token);
            }
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(ready, readiness.Token)));
            if (timeout)
                Assert.Contains("timed out", (await Assert.ThrowsAsync<LakonaProjectCreationException>(() => restore)).Message);
            else
            {
                cancel.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
                Assert.Equal(cancel.Token, error.CancellationToken);
            }
            Assert.True(editor!.HasExited);
            Assert.False(Directory.Exists(root));

            // Kill(entireProcessTree: true) requests descendant termination, but waiting
            // for the editor does not wait for its children. On Unix a killed grandchild
            // may also remain visible until its new parent reaps it.
            using var childExit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            childExit.CancelAfter(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(childExit.Token);
            Assert.True(child.HasExited);
        }
        finally
        {
            cancel.Cancel();
            foreach (var process in new[] { editor, child })
            {
                if (process is null) continue;
                using (process)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanup.Token);
                }
            }
        }
    }

    [Fact]
    public async Task Stop_accepts_an_editor_that_already_exited()
    {
        using var process = Process.Start(PowerShell("exit 0"))!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await UnityDependencyRestorer.StopEditorAsync(process, "test-workspace");
    }

    private static ProcessStartInfo PowerShell(string command) => new("pwsh")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", command }
    };
}
