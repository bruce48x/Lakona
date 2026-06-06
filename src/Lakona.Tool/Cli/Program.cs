var text = ToolText.Current;
var exitCode = await new CliApplication(
        new ToolProcessRunner(text),
        new ProjectScaffolder(),
        new ToolConfigStore(),
        text)
    .RunAsync(args)
    .ConfigureAwait(false);

Environment.ExitCode = exitCode;
