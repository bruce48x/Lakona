using System.ComponentModel;
using System.Diagnostics;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Generation;

public sealed class UnityEditorProbeTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Theory(Skip = "Windows file sharing is needed to force directory cleanup failure.", SkipUnless = nameof(IsWindows))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_failure_does_not_replace_version_or_cancellation(bool cancelProbe)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        string root = "";
        FileStream? locked = null;
        try
        {
            var probe = UnityEditorLocator.ProbeAsync("editor", cancel.Token, info =>
            {
                root = CheckProject(info);
                locked = new FileStream(Path.Combine(root, "locked"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                var process = StartFake(info, "[Console]::WriteLine('Unity version: 2022.3.61t8')");
                if (cancelProbe) cancel.Cancel();
                return process;
            });
            if (cancelProbe)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
                Assert.Equal(cancel.Token, error.CancellationToken);
            }
            else Assert.Equal("2022.3.61t8", (await probe)!.Version);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            locked?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Probe_uses_an_isolated_project_and_reads_the_actual_version(bool stderr, bool buildHeader)
    {
        string root = "";
        var executable = Path.Combine("relative-editor", "Unity.exe");
        var result = await UnityEditorLocator.ProbeAsync(executable, TestContext.Current.CancellationToken, info =>
        {
            root = CheckProject(info);
            Assert.Equal(Path.GetFullPath(executable), info.FileName);
            var header = buildHeader
                ? "Built from 'tuanjie/1.6/staging' branch; Version is '2022.3.61t8 (123456abcdef) revision 5053517'; Build Type 'Release'"
                : "Unity Editor version: 2022.3.61t8 (123456abcdef)";
            var writer = stderr ? "Error" : "Out";
            return StartFake(info, """
                [IO.File]::WriteAllText('ProjectSettings/generated.txt', 'editor output')
                """ + $"\n[Console]::{writer}.WriteLine('{header.Replace("'", "''")}')");
        });
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(executable), result.ExecutablePath);
        Assert.Equal("2022.3.61t8", result.Version);
        Assert.Equal("123456abcdef", result.Revision);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Unavailable_editor_or_unparseable_output_cleans_project(int failure)
    {
        string root = "";
        var result = await UnityEditorLocator.ProbeAsync("editor", TestContext.Current.CancellationToken, info =>
        {
            root = CheckProject(info);
            if (failure == 0) throw new Win32Exception("Cannot start");
            return failure == 1 ? null : StartFake(info, "[Console]::Error.WriteLine('license unavailable'); exit 1");
        });
        Assert.Null(result);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Precancelled_probe_does_not_start_editor()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UnityEditorLocator.ProbeAsync("editor", cancel.Token, _ => throw new Exception("Must not start")));
        Assert.Equal(cancel.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Concurrent_probes_have_independent_projects_and_results()
    {
        var roots = new List<string>();
        Task<UnityEditorInstallation?> Start(string version) => UnityEditorLocator.ProbeAsync(
            "editor", TestContext.Current.CancellationToken, info =>
            {
                roots.Add(CheckProject(info));
                return StartFake(info, $"while (!(Test-Path 'release')) {{ Start-Sleep -Milliseconds 20 }}; [Console]::WriteLine('Unity version: {version}')");
            });
        var first = Start("2022.3.61t8");
        var second = Start("6000.0.1f1");
        Assert.Equal(2, roots.Distinct().Count());
        foreach (var root in roots) await File.WriteAllTextAsync(Path.Combine(root, "release"), "", TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(first, second);
        Assert.Equal("2022.3.61t8", results[0]!.Version);
        Assert.Equal("6000.0.1f1", results[1]!.Version);
        Assert.All(roots, root => Assert.False(Directory.Exists(root)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_probe_joins_editor_and_readers_before_removing_project(bool timeout)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        readiness.CancelAfter(TimeSpan.FromSeconds(30));
        string root = "";
        Process? editor = null;
        var probe = UnityEditorLocator.ProbeAsync("editor", cancel.Token, info =>
        {
            root = CheckProject(info);
            var process = StartFake(info, "[IO.File]::WriteAllText('ready', ''); while ($true) { [Console]::Out.WriteLine('waiting'); [Console]::Error.WriteLine('waiting'); Start-Sleep -Milliseconds 20 }");
            editor = Process.GetProcessById(process.Id);
            return process;
        }, timeout ? TimeSpan.FromSeconds(8) : TimeSpan.FromMinutes(2));
        try
        {
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (probe.IsCompleted) { await probe; Assert.Fail("Editor stopped before readiness"); }
                await Task.Delay(25, readiness.Token);
            }
            if (timeout) Assert.Null(await probe);
            else
            {
                cancel.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
                Assert.Equal(cancel.Token, error.CancellationToken);
            }
            Assert.True(editor!.HasExited);
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            cancel.Cancel();
            try { await probe; } catch (OperationCanceledException) { }
            editor?.Dispose();
        }
    }

    private static string CheckProject(ProcessStartInfo info)
    {
        var root = info.ArgumentList[info.ArgumentList.IndexOf("-projectPath") + 1];
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Equal(root, info.WorkingDirectory);
        Assert.NotEqual(Path.GetFullPath(Environment.CurrentDirectory), root);
        Assert.True(Directory.Exists(Path.Combine(root, "Assets")));
        Assert.True(Directory.Exists(Path.Combine(root, "ProjectSettings")));
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.True(info.CreateNoWindow);
        return root;
    }

    private static Process StartFake(ProcessStartInfo info, string command)
    {
        info.FileName = "pwsh";
        info.ArgumentList.Clear();
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }
}
