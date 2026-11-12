using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lakona.Hub.Installation;

namespace Lakona.Hub.Updater;

public sealed partial class InstallerWindow : Window
{
    private readonly InstallationPaths paths = InstallationPaths.Current;
    private readonly CancellationTokenSource cancellation = new();
    private readonly bool chinese;
    private readonly bool traditional;
    private bool busy;
    private bool complete;
    private bool migrationPending;

    public InstallerWindow()
    {
        InitializeComponent();
        chinese = Program.Option("--language") is "SimplifiedChinese" or "TraditionalChinese" ||
            (Program.Option("--language") is null && CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.Ordinal));
        traditional = Program.Option("--language") == "TraditionalChinese" ||
            (Program.Option("--language") is null && CultureInfo.CurrentUICulture.Name is "zh-TW" or "zh-HK" or "zh-Hant");
        Avalonia.Automation.AutomationProperties.SetName(close, Text("关闭", "Close"));
        ToolTip.SetTip(close, Text("关闭", "Close"));
        action.Content = Text("安装", "Install");
        if (Program.Arguments.Contains("--uninstall"))
        {
            title.Text = Text("卸载 Lakona Hub", "Uninstall Lakona Hub");
            ShowDescription(Text("保留项目、设置和已下载的 SDK。", "Your projects, settings and downloaded SDKs stay."));
            action.Content = Text("卸载", "Uninstall");
        }
        action.Click += async (_, _) => { if (migrationPending) await MigrateAsync(); else if (complete) Close(); else await RunAsync(); };
        logs.Content = Text("查看日志", "View log");
        logs.Click += (_, _) => Process.Start(new ProcessStartInfo(Path.Combine(paths.State, "installation.log")) { UseShellExecute = true })?.Dispose();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if (Path.GetDirectoryName(directory) == paths.State && Path.GetFileName(directory).StartsWith("handoff-", StringComparison.Ordinal))
            {
                try { File.WriteAllText(Path.Combine(directory, "completed"), "completed"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        };
        Opened += async (_, _) =>
        {
            if (Program.Arguments.Contains("--smoke-test"))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Program.Option("--screenshot") is { } output)
                    {
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
                        bitmap.Render(this);
                        bitmap.Save(output);
                    }
                    Close();
                }, DispatcherPriority.Background);
                return;
            }
            if (Program.Option("--ready") is { } ready)
            {
                var expected = Path.Combine(AppContext.BaseDirectory, "ready");
                if (!string.Equals(Path.GetFullPath(ready), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid update handoff.");
                File.WriteAllText(ready, "ready");
            }
            if (Program.Option("--parent") is not null || Program.Arguments.Contains("--recover")) await RunAsync();
        };
    }

    private void ShowDescription(string text)
    {
        description.Text = text;
        description.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void DragWindow(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void CloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private string Text(string zh, string en) => !chinese ? en : !traditional ? zh : zh switch
    {
        "安装" => "安裝",
        "卸载" => "解除安裝",
        "卸载 Lakona Hub" => "解除安裝 Lakona Hub",
        "保留项目、设置和已下载的 SDK。" => "保留專案、設定和已下載的 SDK。",
        "正在准备，请稍候…" => "正在準備，請稍候…",
        "正在验证安装包…" => "正在驗證安裝套件…",
        "正在安装，请勿关闭…" => "正在安裝，請勿關閉…",
        "正在打开 Hub…" => "正在開啟 Hub…",
        "正在恢复之前的版本…" => "正在還原先前的版本…",
        "请运行完整的 Lakona Hub 安装包。" => "請執行完整的 Lakona Hub 安裝套件。",
        "已完成。项目和设置已保留。" => "已完成。專案和設定已保留。",
        "新的 Hub 已就绪。旧版 MSI 安装仍然存在，移除它可能需要最后一次管理员授权。" => "新的 Hub 已就緒。舊版 MSI 安裝仍然存在，移除它可能需要最後一次管理員授權。",
        "移除旧版安装" => "移除舊版安裝",
        "重试" => "重試",
        "旧版尚未移除，新版仍可使用。可以稍后重试。" => "舊版尚未移除，新版仍可使用。可以稍後重試。",
        "迁移完成。" => "移轉完成。",
        "查看日志" => "檢視記錄",
        "无法自动恢复。已保留恢复文件，请查看日志并联系支持。" => "無法自動還原。已保留還原檔案，請檢視記錄並聯絡支援。",
        "关闭" => "關閉",
        _ => zh
    };

    private async Task RunAsync()
    {
        busy = true; close.IsEnabled = false; progress.IsVisible = true; statusArea.IsVisible = true; action.IsEnabled = false; logs.IsVisible = false; progress.IsIndeterminate = true;
        var host = new WindowsInstallationHost(paths);
        try
        {
            status.Text = Text("正在准备，请稍候…", "Getting ready…");
            await WaitForParentAsync();
            host.EnsureClosed();
            var transaction = new InstallationTransaction(paths, SignedPackage.Release(), host);
            var reporter = new Progress<string>(stage =>
            {
                progress.IsIndeterminate = stage is "verifying" or "launching" or "recovering";
                progress.Value = stage == "completed" ? 100 : stage == "installing" ? 65 : 20;
                status.Text = stage switch
                {
                    "verifying" => Text("正在验证安装包…", "Verifying your package…"),
                    "installing" => Text("正在安装，请勿关闭…", "Installing. Keep this window open…"),
                    "launching" => Text("正在打开 Hub…", "Opening Hub…"),
                    "recovering" => Text("正在恢复之前的版本…", "Restoring your previous version…"),
                    _ => Text("已完成", "All set.")
                };
            });
            if (Program.Arguments.Contains("--recover")) await Task.Run(() => transaction.RecoverAsync(reporter));
            else if (Program.Arguments.Contains("--uninstall")) await Task.Run(transaction.UninstallAsync);
            else
            {
                var archive = Program.Option("--package") ?? throw new InvalidOperationException(Text("请运行完整的 Lakona Hub 安装包。", "Please run the complete Lakona Hub setup package."));
                await Task.Run(() => transaction.InstallAsync(archive, repair: true, reporter, cancellation.Token));
            }
            complete = true; progress.Value = 100;
            Environment.ExitCode = 0;
            status.Text = Text("已完成。项目和设置已保留。", "All set. Your projects and settings are preserved.");
            action.Content = Text("完成", "Done");
            if (!Program.Arguments.Contains("--uninstall") && WindowsInstallationHost.HasLegacyMsi())
            {
                migrationPending = true;
                ShowDescription(Text("新的 Hub 已就绪。旧版 MSI 安装仍然存在，移除它可能需要最后一次管理员授权。", "Your new Hub is ready. Remove the old MSI installation to finish migration. Windows may request administrator permission once."));
                action.Content = Text("移除旧版安装", "Remove old installation");
            }
        }
        catch (Exception ex)
        {
            InstallationLog.Write(paths, "failed", ex);
            Environment.ExitCode = 1;
            logs.IsVisible = true;
            status.Text = Text("未能完成。", "Could not finish. ") + " " + ex.Message;
            action.Content = Text("重试", "Try again");
            if (File.Exists(paths.Journal) && Directory.Exists(paths.Backup))
            {
                ShowDescription(Text("无法自动恢复。已保留恢复文件，请查看日志并联系支持。", "Automatic recovery could not finish. Recovery files have been preserved. View the log and contact support."));
                action.Content = Text("关闭", "Close");
                complete = true;
            }
        }
        finally { busy = false; close.IsEnabled = true; action.IsEnabled = true; progress.IsIndeterminate = false; }
        if (complete && Environment.ExitCode == 0 && !migrationPending && !Program.Arguments.Contains("--uninstall"))
        {
            await Task.Delay(500);
            Close();
        }
    }

    private async Task MigrateAsync()
    {
        busy = true; close.IsEnabled = false; action.IsEnabled = false;
        try
        {
            migrationPending = !await new WindowsInstallationHost(paths).RemoveLegacyMsiAsync();
            status.Text = migrationPending ? Text("旧版尚未移除，新版仍可使用。可以稍后重试。", "The old installation remains. Your new Hub is available; you can retry later.") : Text("迁移完成。", "Migration complete.");
            if (!migrationPending) action.Content = Text("完成", "Done");
        }
        catch (Exception ex) { InstallationLog.Write(paths, "migration-failed", ex); status.Text = ex.Message; }
        finally { busy = false; close.IsEnabled = true; action.IsEnabled = true; }
    }

    private static async Task WaitForParentAsync()
    {
        if (!int.TryParse(Program.Option("--parent"), out var pid)) return;
        Process parent;
        try { parent = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            if (!long.TryParse(Program.Option("--parent-start"), out var started) || parent.StartTime.ToUniversalTime().Ticks != started) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await parent.WaitForExitAsync(timeout.Token);
        }
    }
}
