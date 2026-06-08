using Lakona.Tool.RpcStarter;

LakonaBrand.Print();

var text = ToolText.Current;
var exitCode = await new CliApplication(
        new RpcStarterGenerator(),
        new ProjectScaffolder(),
        new ToolConfigStore(),
        text)
    .RunAsync(args)
    .ConfigureAwait(false);

Environment.ExitCode = exitCode;
