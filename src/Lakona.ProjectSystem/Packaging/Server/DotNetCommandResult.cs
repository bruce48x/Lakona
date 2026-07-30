namespace Lakona.ProjectSystem.Packaging.Server;

internal sealed record DotNetCommandResult(int ExitCode, string StandardOutput, string StandardError);
