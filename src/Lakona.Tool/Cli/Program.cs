if (CliProgramBrandPolicy.ShouldPrintBanner(args))
    LakonaBrand.Print();

var text = ToolText.Current;
var exitCode = await new CliApplication(text)
    .RunAsync(args)
    .ConfigureAwait(false);

Environment.ExitCode = exitCode;

internal static class CliProgramBrandPolicy
{
    public static bool ShouldPrintBanner(string[] args)
    {
        if (args.Length == 0)
            return false;

        return args[0] is not ("help" or "--help" or "-h" or "version" or "--version");
    }
}
