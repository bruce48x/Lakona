using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Lakona.Hub.Installation;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? work = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var paths = InstallationPaths.Current;
            paths.Prepare();
            paths.CleanupCompletedWork();
            work = Path.Combine(paths.State, "setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            using var lease = paths.HoldWork(work);
            var archive = Path.Combine(work, "payload.zip");
            using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lakona.Hub.Payload.zip")
                ?? throw new InvalidDataException("This setup is incomplete. Download the complete Lakona Hub installer."))
            using (var file = File.Create(archive)) embedded.CopyTo(file);
            var runner = Path.Combine(work, "runner");
            SignedPackage.Release().Extract(archive, runner);
            var start = new ProcessStartInfo(Path.Combine(runner, "Lakona.Hub.Updater.exe")) { UseShellExecute = false, WorkingDirectory = runner };
            start.ArgumentList.Add("--package"); start.ArgumentList.Add(archive);
            if (args.Contains("--smoke-test")) start.ArgumentList.Add("--smoke-test");
            using var process = Process.Start(start) ?? throw new IOException("The setup window could not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception)
        {
            MessageBoxW(0, "Lakona Hub setup could not verify or open its package. Download a fresh installer from the official release page. Your installed Hub has not been changed.", "Lakona Hub", 0x10);
            return 1;
        }
        finally
        {
            if (work is not null)
            {
                try { InstallationPaths.Current.RemoveWorkDirectory(work); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
