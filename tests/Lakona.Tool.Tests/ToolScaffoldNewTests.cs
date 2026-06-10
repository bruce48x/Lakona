using Xunit;

namespace Lakona.Tool.Tests;

public sealed class ToolScaffoldNewTests
{
    [Fact]
    public async Task ScaffoldNewProject_CreatesGitInfrastructure()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = CliParser.ParseNewOptions([]);
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, options);

            Assert.True(File.Exists(Path.Combine(projectRoot, ".gitignore")), ".gitignore should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, ".gitattributes")), ".gitattributes should exist");

            var gitignore = await File.ReadAllTextAsync(Path.Combine(projectRoot, ".gitignore"));
            Assert.Contains("**/bin/", gitignore, StringComparison.Ordinal);
            Assert.Contains("**/obj/", gitignore, StringComparison.Ordinal);

            var gitattributes = await File.ReadAllTextAsync(Path.Combine(projectRoot, ".gitattributes"));
            Assert.Contains("*.cs text eol=lf", gitattributes, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_CreatesSharedProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Shared.csproj")), "Shared.csproj should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Shared.asmdef")), "Shared.asmdef should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "package.json")), "package.json should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Directory.Build.props")), "Directory.Build.props should exist");

            var csproj = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Shared", "Shared.csproj"));
            Assert.Contains("TargetFrameworks", csproj, StringComparison.Ordinal);
            Assert.Contains("netstandard2.1", csproj, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_CreatesSharedGameContracts()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "RpcContractIds.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "Login.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatProtocols.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatMessages.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_CreatesServerAppProject_NotServerServer()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "App", "Server.App.csproj")),
                "Server.App.csproj should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "App", "Program.cs")),
                "Server.App Program.cs should exist");
            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "Server.slnx")),
                "Server.slnx should exist");
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "Server", "Server")),
                "Server/Server/ must NOT exist");

            var slnx = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "Server.slnx"));
            Assert.Contains("App/Server.App.csproj", slnx, StringComparison.Ordinal);
            Assert.DoesNotContain("Server/Server.csproj", slnx, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_CreatesHotfixProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, CliParser.ParseNewOptions([]));

            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "Hotfix", "Server.Hotfix.csproj")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "Hotfix", "Login", "LoginService.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "Hotfix", "Chat", "ChatService.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_CreatesUnityClientProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new NewCommandOptions("Test", null, "unity", "websocket", "single", "memorypack", "none", "openupm", "none");
            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, options);

            Assert.True(File.Exists(Path.Combine(projectRoot, "Client", "Packages", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Client", "Assets", "packages.config")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Client", "Assets", "NuGet.config")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Login", "LoginClient.cs")));
            Assert.True(File.Exists(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatClient.cs")));
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }
}
