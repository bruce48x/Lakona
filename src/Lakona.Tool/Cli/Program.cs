LakonaBrand.Print();

var text = ToolText.Current;
var exitCode = await new CliApplication(text)
    .RunAsync(args)
    .ConfigureAwait(false);

Environment.ExitCode = exitCode;
