namespace Lakona.Hub.Installation;

public static class InstallationLog
{
    public static void Write(InstallationPaths paths, string stage, Exception? error = null)
    {
        // Keep diagnostics useful without recording arguments, tokens, URLs,
        // project locations, profile paths, or arbitrary exception messages.
        if (stage.Length > 40 || !stage.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Invalid installation log stage.");
        try
        {
            paths.Prepare();
            var file = Path.Combine(paths.State, "installation.log");
            InstallationPaths.RejectLinks(file);
            if (File.Exists(file) && new FileInfo(file).Length > 64 * 1024) File.WriteAllText(file, "");
            File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {stage} {error?.GetType().Name}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
