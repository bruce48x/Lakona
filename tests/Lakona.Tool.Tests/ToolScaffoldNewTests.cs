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

            var gitignore = await File.ReadAllTextAsync(Path.Combine(projectRoot, ".gitignore"));
            Assert.Contains("**/bin/", gitignore, StringComparison.Ordinal);
            Assert.Contains("**/obj/", gitignore, StringComparison.Ordinal);
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
            Assert.True(File.Exists(Path.Combine(projectRoot, "Server", "App", "Chat", "ChatConnectionLifecycle.cs")));
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

            var sharedMessages = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Shared", "Contracts", "Chat", "ChatMessages.cs"));
            var unityChatUi = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatUI.cs"));
            var chatClient = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatClient.cs"));

            Assert.DoesNotContain("ConnectionId", sharedMessages, StringComparison.Ordinal);
            Assert.DoesNotContain("session.ConnectionId", unityChatUi, StringComparison.Ordinal);
            Assert.DoesNotContain("new RpcClient.RpcNotificationBindings()", chatClient, StringComparison.Ordinal);

            var loginClient = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Login", "LoginClient.cs"));
            var serviceBinding = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "App", "Hosting", "ServiceBindingConfigurator.cs"));

            Assert.Contains("callbacks.Add((ILoginCallback)this);", loginClient, StringComparison.Ordinal);
            Assert.Contains("callbacks.Add((IChatCallback)this);", loginClient, StringComparison.Ordinal);
            Assert.Contains("public ChatClient(LoginClient loginClient)", chatClient, StringComparison.Ordinal);
            Assert.Contains("await _chatService.BindAsync(new ChatBindRequest());", chatClient, StringComparison.Ordinal);
            Assert.Contains("session.ContextId", serviceBinding, StringComparison.Ordinal);
            Assert.Contains("new LoginCallbackProxy(session)", serviceBinding, StringComparison.Ordinal);
            Assert.Contains("new ChatCallbackProxy(session)", serviceBinding, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
                Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldNewProject_UnityTcpJson_UsesTcpTransportAndClientLoginNamespace()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "lakona-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new NewCommandOptions(
                Name: "Test",
                OutputPath: null,
                ClientEngine: "unity",
                Transport: "tcp",
                NetworkProfile: "single",
                Serializer: "json",
                Persistence: "none",
                NuGetForUnitySource: "openupm",
                DeployProfile: "none");

            await new ProjectScaffolder().ScaffoldNewProjectAsync(projectRoot, options);

            var program = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "App", "Program.cs"));
            var appsettings = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Server", "App", "appsettings.json"));
            var unityChatUi = await File.ReadAllTextAsync(Path.Combine(projectRoot, "Client", "Assets", "Scripts", "Chat", "ChatUI.cs"));

            Assert.Contains(".UseTransport(\"tcp\")", program, StringComparison.Ordinal);
            Assert.Contains("new TcpConnectionAcceptor(opts.Port)", program, StringComparison.Ordinal);
            Assert.Contains("\"Transport\": \"tcp\"", appsettings, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Transport\": \"websocket\"", appsettings, StringComparison.Ordinal);
            Assert.Contains("using Client.Login;", unityChatUi, StringComparison.Ordinal);
            Assert.Contains("private LoginClient? _loginClient;", unityChatUi, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
