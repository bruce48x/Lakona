using System.Runtime.InteropServices;

namespace Lakona.Hub.Applications;

internal static partial class ApplicationExecutable
{
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool IsRunnable(string path) =>
        File.Exists(path) && (!OperatingSystem.IsLinux() || Access(path, 1 /* X_OK */) == 0);

    public static string InstallationKey(LocalApplicationKind kind, string path)
    {
        if (!OperatingSystem.IsLinux() || kind == LocalApplicationKind.Other)
        {
            return path;
        }
        var target = ResolveLinuxPath(path);
        // Keep the invocation path of dispatchers such as /snap/bin/code -> /usr/bin/snap.
        // Hub launchers are informational and can have several names for one installation.
        if (kind is LocalApplicationKind.UnityHub or LocalApplicationKind.TuanjieHub)
        {
            return $"{kind}:{target}";
        }
        return SystemApplicationProbeSource.MatchesLinuxExecutable(kind, Path.GetFileName(target)) ? target : path;
    }

    // realpath resolves links in parent directories too (e.g. /bin or a Flatpak revision).
    public static string ResolveLinuxPath(string path)
    {
        var resolved = RealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return path;
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved)!;
        }
        finally
        {
            Free(resolved);
        }
    }

    [LibraryImport("libc", EntryPoint = "access", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Access(string path, int mode);

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr buffer);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
