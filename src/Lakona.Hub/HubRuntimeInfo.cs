namespace Lakona.Hub;

internal static class HubRuntimeInfo
{
    public const string BundledDotNetSdkVersion = "10.0.100";

    public static string BundledDotNetExecutablePath()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var direct = Path.Combine(AppContext.BaseDirectory, "dotnet", executable);
        if (File.Exists(direct))
        {
            return direct;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "dotnet", executable));
    }
}
