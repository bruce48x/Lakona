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

        if (args.Length > 1 &&
            (args[1] == "help" || args.Skip(1).Any(static argument => argument is "--help" or "-h")))
        {
            return false;
        }

        return args[0] is not ("help" or "--help" or "-h" or "version" or "--version");
    }
}
