using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Lakona.Tool.RpcStarter;
using Xunit;

namespace Lakona.Tool.Tests.RpcStarter;

public sealed class StarterTemplateGeneratorTests
{
    private static readonly ResolvedVersions Versions = new("1.2.3", "2.3.4", "3.4.5", "4.5.6", "5.6.7", "0.1.2", "6.7.8", "8.9.10");

    [Fact]
    public void GenerateIntoTargetDirectory_RollsBackStagingDirectory_WhenGenerationFails()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StarterOutputManager.GenerateIntoTargetDirectory(
                    targetRoot,
                    stagingRoot =>
                    {
                        File.WriteAllText(Path.Combine(stagingRoot, "partial.txt"), "partial");
                        throw new InvalidOperationException("boom");
                    }));

            Assert.Equal("boom", ex.Message);
            Assert.False(Directory.Exists(targetRoot));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".Sample.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public void GenerateIntoTargetDirectory_Fails_WhenTargetDirectoryIsNotEmpty()
    {
        var parentRoot = CreateTempRoot();
        var targetRoot = Path.Combine(parentRoot, "Sample");
        Directory.CreateDirectory(targetRoot);
        File.WriteAllText(Path.Combine(targetRoot, "existing.txt"), "x");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StarterOutputManager.GenerateIntoTargetDirectory(targetRoot, _ => { }));

            Assert.Contains("Target directory already exists and is not empty", ex.Message);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveVersions_UsesBundledReleaseManifest_InsteadOfIndependentLatestLookups()
    {
        var jsonVersions = NuGetVersionResolver.ResolveVersions(TransportKind.WebSocket, SerializerKind.Json);
        var memoryPackVersions = NuGetVersionResolver.ResolveVersions(TransportKind.Kcp, SerializerKind.MemoryPack);

        Assert.Equal(StarterReleaseVersions.Core, jsonVersions.Core);
        Assert.Equal(StarterReleaseVersions.Server, jsonVersions.Server);
        Assert.Equal(StarterReleaseVersions.Client, jsonVersions.Client);
        Assert.Equal(StarterReleaseVersions.TransportWebSocket, jsonVersions.Transport);
        Assert.Equal(StarterReleaseVersions.SerializerJson, jsonVersions.Serializer);
        Assert.Null(jsonVersions.SerializerRuntime);
        Assert.Null(jsonVersions.SerializerRuntimeCore);

        Assert.Equal(StarterReleaseVersions.TransportKcp, memoryPackVersions.Transport);
        Assert.Equal(StarterReleaseVersions.SerializerMemoryPack, memoryPackVersions.Serializer);
        Assert.Equal(StarterReleaseVersions.MemoryPackRuntime, memoryPackVersions.SerializerRuntime);
        Assert.Equal(StarterReleaseVersions.MemoryPackRuntimeCore, memoryPackVersions.SerializerRuntimeCore);
    }

    [Fact]
    public void StarterReleaseVersions_MatchSourceProjectVersions()
    {
        var repositoryRoot = FindRepositoryRoot();

        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Core", "Lakona.Rpc.Core.csproj"), StarterReleaseVersions.Core);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Server", "Lakona.Rpc.Server.csproj"), StarterReleaseVersions.Server);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Client", "Lakona.Rpc.Client.csproj"), StarterReleaseVersions.Client);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Transport.Tcp", "Lakona.Rpc.Transport.Tcp.csproj"), StarterReleaseVersions.TransportTcp);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Transport.WebSocket", "Lakona.Rpc.Transport.WebSocket.csproj"), StarterReleaseVersions.TransportWebSocket);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Transport.Kcp", "Lakona.Rpc.Transport.Kcp.csproj"), StarterReleaseVersions.TransportKcp);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Serializer.Json", "Lakona.Rpc.Serializer.Json.csproj"), StarterReleaseVersions.SerializerJson);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Serializer.MemoryPack", "Lakona.Rpc.Serializer.MemoryPack.csproj"), StarterReleaseVersions.SerializerMemoryPack);
        Assert.Equal(ReadProjectVersion(repositoryRoot, "src", "Lakona.Rpc.Analyzers", "Lakona.Rpc.Analyzers.csproj"), StarterReleaseVersions.Analyzers);
        Assert.Equal(ReadPackageReferenceVersion(repositoryRoot, "src", "Lakona.Rpc.Serializer.MemoryPack", "Lakona.Rpc.Serializer.MemoryPack.csproj", "MemoryPack"), StarterReleaseVersions.MemoryPackRuntime);
        Assert.Equal(ReadPackageReferenceVersion(repositoryRoot, "src", "Lakona.Rpc.Serializer.MemoryPack", "Lakona.Rpc.Serializer.MemoryPack.csproj", "MemoryPack"), StarterReleaseVersions.MemoryPackRuntimeCore);
    }

    [Fact]
    public void GenerateTemplate_CreatesSharedLayout_ForUnityCompatibilityRules()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "My Game!@#$", ClientEngineKind.Unity, TransportKind.Kcp, SerializerKind.MemoryPack, Versions);

            var sharedProps = File.ReadAllText(Path.Combine(root, "Shared", "Directory.Build.props"));
            var sharedCsproj = File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj"));
            var sharedAsmdef = File.ReadAllText(Path.Combine(root, "Shared", "Shared.asmdef"));
            var sharedDtos = File.ReadAllText(Path.Combine(root, "Shared", "Interfaces", "SharedDtos.cs"));
            var contractIds = File.ReadAllText(Path.Combine(root, "Shared", "Interfaces", "RpcContractIds.cs"));
            var pingContract = File.ReadAllText(Path.Combine(root, "Shared", "Interfaces", "IPingService.cs"));
            var gitIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));

            Assert.Contains("<LangVersion>latest</LangVersion>", sharedCsproj);
            Assert.Contains("<ImplicitUsings>disable</ImplicitUsings>", sharedCsproj);
            Assert.Contains("<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>", sharedCsproj);
            Assert.Contains("<RootNamespace>Shared</RootNamespace>", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Core\" Version=\"1.2.3\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\" Version=\"5.6.7\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"MemoryPack\" Version=\"6.7.8\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"MemoryPack.Generator\" Version=\"6.7.8\">", sharedCsproj);
            Assert.Contains(@"<MSBuildProjectExtensionsPath>..\_artifacts\Shared\obj\</MSBuildProjectExtensionsPath>", sharedProps);
            Assert.Contains(@"<BaseIntermediateOutputPath>..\_artifacts\Shared\obj\</BaseIntermediateOutputPath>", sharedProps);
            Assert.Contains(@"<BaseOutputPath>..\_artifacts\Shared\bin\</BaseOutputPath>", sharedProps);
            Assert.Contains("\"rootNamespace\": \"Shared\"", sharedAsmdef);
            Assert.Contains("\"overrideReferences\": true", sharedAsmdef);
            Assert.Contains("\"allowUnsafeCode\": true", sharedAsmdef);
            Assert.Contains("\"Lakona.Rpc.Core.dll\"", sharedAsmdef);
            Assert.Contains("\"MemoryPack.Core.dll\"", sharedAsmdef);
            Assert.Contains("\"System.Runtime.CompilerServices.Unsafe.dll\"", sharedAsmdef);
            Assert.DoesNotContain("My Game", sharedDtos, StringComparison.Ordinal);
            Assert.Contains("using MemoryPack;", sharedDtos);
            Assert.Contains("[MemoryPackable]", sharedDtos);
            Assert.DoesNotContain("GenerateType.VersionTolerant", sharedDtos, StringComparison.Ordinal);
            Assert.Contains("public sealed partial class PingRequest", sharedDtos);
            Assert.Contains("public sealed partial class PingReply", sharedDtos);
            Assert.Contains("[MemoryPackOrder(0)]", sharedDtos);
            Assert.Contains("[MemoryPackOrder(1)]", sharedDtos);
            Assert.Contains("namespace Shared.Interfaces", sharedDtos);
            Assert.DoesNotContain("namespace Shared.Interfaces;", sharedDtos, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTimeOffset", sharedDtos, StringComparison.Ordinal);
            Assert.Contains("public string ServerTimeUtc { get; set; } = string.Empty;", sharedDtos);
            Assert.Contains("public static class RpcContractIds", contractIds);
            Assert.Contains("public static class Services", contractIds);
            Assert.Contains("public const int Ping = 1;", contractIds);
            Assert.Contains("public static class PingServiceMethods", contractIds);
            Assert.Contains("public const int PingAsync = 1;", contractIds);
            Assert.Contains("[RpcService(RpcContractIds.Services.Ping)]", pingContract);
            Assert.Contains("[RpcMethod(RpcContractIds.PingServiceMethods.PingAsync)]", pingContract);
            Assert.True(File.Exists(Path.Combine(root, "Shared", "Interfaces", "IPingService.cs")));
            Assert.True(File.Exists(Path.Combine(root, "Shared", "package.json")));
            Assert.False(File.Exists(Path.Combine(root, "Shared", "UnityPackage", "package.json")));
            Assert.False(File.Exists(Path.Combine(root, "Shared", "UnityPackage", "SharedDtos.cs")));
            Assert.Contains("**/bin/", gitIgnore);
            Assert.Contains("/Client/[Ll]ibrary/", gitIgnore);
            Assert.Contains("/Client/Assets/Packages/", gitIgnore);
            Assert.Contains("/_artifacts/", gitIgnore);
            Assert.Contains(".vs/", gitIgnore);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesSolutionAndAddsSharedAndServerProjects()
    {
        var root = CreateTempRoot();
        try
        {
            var commands = new List<string>();
            var gitCommands = new List<string>();
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(commands), CreateFakeGitRunner(gitCommands));

            generator.GenerateTemplate(root, "Starter-App", ClientEngineKind.Unity, TransportKind.Tcp, SerializerKind.Json, Versions);

            var slnxPath = Path.Combine(root, "Server", "Server.slnx");
            var slnx = File.ReadAllText(slnxPath);

            Assert.Empty(commands);
            Assert.DoesNotContain("new tool-manifest", commands);
            Assert.DoesNotContain(commands, static command => command.Contains("lakona-rpc-codegen", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("<Project Path=\"../Shared/Shared.csproj\" />", slnx);
            Assert.Contains("<Project Path=\"Server/Server.csproj\" />", slnx);
            Assert.True(File.Exists(Path.Combine(root, "Server", "Server", "Server.csproj")));
            Assert.False(File.Exists(Path.Combine(root, "Server", "Server", "Generated", "AllServicesBinder.cs")));
            Assert.False(File.Exists(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Generated", "RpcApi.cs")));
            Assert.False(File.Exists(Path.Combine(root, "codegen.ps1")));
            Assert.False(File.Exists(Path.Combine(root, "codegen.sh")));
            Assert.Contains("init", gitCommands);
            Assert.True(Directory.Exists(Path.Combine(root, ".git")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesUnityClientFiles_WithOpenUpmByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Bad Project Name %$#", ClientEngineKind.Unity, TransportKind.WebSocket, SerializerKind.Json, Versions);

            var manifestJson = File.ReadAllText(Path.Combine(root, "Client", "Packages", "manifest.json"));
            using var manifest = JsonDocument.Parse(manifestJson);
            var sharedDependency = manifest.RootElement
                .GetProperty("dependencies")
                .GetProperty("com.lakona-rpc.badprojectname.shared")
                .GetString();
            var manifestText = File.ReadAllText(Path.Combine(root, "Client", "Packages", "manifest.json"));

            var serverProgram = File.ReadAllText(Path.Combine(root, "Server", "Server", "Program.cs"));
            var serverCsproj = File.ReadAllText(Path.Combine(root, "Server", "Server", "Server.csproj"));
            var pingServicePath = Path.Combine(root, "Server", "Server", "Services", "PingService.cs");
            var pingService = File.ReadAllText(pingServicePath);
            var packagesConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "packages.config"));
            var nugetConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "NuGet.config"));
            var projectVersion = File.ReadAllText(Path.Combine(root, "Client", "ProjectSettings", "ProjectVersion.txt"));
            var clientReadme = File.ReadAllText(Path.Combine(root, "Client", "README.md"));
            var generationMarker = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "LakonaRpcGeneration.cs"));
            var testerScript = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Testing", "RpcConnectionTester.cs"));
            var testerScriptMeta = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Testing", "RpcConnectionTester.cs.meta"));
            var scene = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scenes", "ConnectionTest.unity"));
            var autoOpenSceneScript = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Editor", "AutoOpenConnectionScene.cs"));
            var editorBuildSettings = File.ReadAllText(Path.Combine(root, "Client", "ProjectSettings", "EditorBuildSettings.asset"));
            var legacyEditorScriptPath = Path.Combine(root, "Client", "Assets", "Editor", "LakonaRpcCodeGenEditor.cs");
            var starterGeneratedAsmdefPath = Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Generated", "Lakona.Rpc.Generated.asmdef");
            var embeddedNuGetForUnity = Path.Combine(root, "Client", "Packages", "com.github-glitchenzo.nugetforunity", "package.json");

            Assert.Equal("file:../../Shared", sharedDependency);
            Assert.Contains("package.openupm.com", manifestText);
            Assert.Contains("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifestText);
            Assert.DoesNotContain("Bad Project Name", serverProgram, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTimeOffset", serverProgram, StringComparison.Ordinal);
            Assert.Contains("using Lakona.Rpc.Core;", serverProgram);
            Assert.Contains("using Lakona.Rpc.Server;", serverProgram);
            Assert.Contains("using Lakona.Rpc.Serializer.Json;", serverProgram);
            Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", serverProgram);
            Assert.Contains("var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();", serverProgram);
            Assert.Contains("var builder = RpcServerHostBuilder.Create()", serverProgram);
            Assert.Contains(".UseCommandLine(commandLineArgs)", serverProgram);
            Assert.Contains(".UseSerializer(new JsonRpcSerializer())", serverProgram);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", serverProgram);
            Assert.Contains("static void ConfigureTransportSecurity(TransportSecurityConfig security)", serverProgram);
            Assert.Contains("builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), \"/ws\", builder.Limits.MaxPendingAcceptedConnections, ct));", serverProgram);
            Assert.Contains("await builder.RunAsync();", serverProgram);
            Assert.True(File.Exists(pingServicePath));
            Assert.False(File.Exists(Path.Combine(root, "Server", "Server", "PingService.cs")));
            Assert.Contains("public sealed class PingService : IPingService", pingService);
            Assert.Contains("ServerTimeUtc = DateTime.UtcNow.ToString(\"O\")", pingService);
            Assert.Contains("<RootNamespace>Server</RootNamespace>", serverCsproj);
            Assert.Contains("<LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>", serverCsproj);
            Assert.Contains("<LakonaRpcServerGeneratedNamespace>Server.Generated</LakonaRpcServerGeneratedNamespace>", serverCsproj);
            Assert.Contains("<ProjectReference Include=\"..\\..\\Shared\\Shared.csproj\" />", serverCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.Json\" Version=\"5.6.7\" />", serverCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Analyzers\" Version=\"0.1.2\">", serverCsproj);
            Assert.Contains("<PrivateAssets>all</PrivateAssets>", serverCsproj);
            Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", serverCsproj);
            Assert.DoesNotContain("LakonaRpcGenerateCode", serverCsproj, StringComparison.Ordinal);
            Assert.DoesNotContain("lakona-rpc-codegen", serverCsproj, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<package id=\"Lakona.Rpc.Core\" version=\"1.2.3\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Analyzers\" version=\"0.1.2\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Transport.WebSocket\" version=\"4.5.6\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Serializer.Json\" version=\"5.6.7\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"Microsoft.Bcl.AsyncInterfaces\" version=\"10.0.7\" />", packagesConfig);
            Assert.Contains("<package id=\"System.IO.Pipelines\" version=\"10.0.6\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Text.Encodings.Web\" version=\"10.0.7\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Buffers\" version=\"4.6.1\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Memory\" version=\"4.6.3\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Runtime.CompilerServices.Unsafe\" version=\"6.1.2\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Threading.Tasks.Extensions\" version=\"4.6.3\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Text.Json\" version=\"10.0.7\" />", packagesConfig);
            Assert.Contains("<disabledPackageSources />", nugetConfig);
            Assert.Contains("<activePackageSource>", nugetConfig);
            Assert.Contains("<add key=\"All\" value=\"(Aggregate source)\" />", nugetConfig);
            Assert.Contains("m_EditorVersion: 2022.3.62f3c1", projectVersion);
            Assert.Contains("m_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)", projectVersion);
            Assert.Contains("the editor will auto-open `Assets/Scenes/ConnectionTest.unity`", clientReadme);
            Assert.Contains("download from OpenUPM", clientReadme);
            Assert.Contains("[assembly: LakonaRpcGenerateClient(\"Rpc.Generated\")]", generationMarker);
            Assert.Contains("using Rpc.Generated;", testerScript);
            Assert.Contains("using Shared.Interfaces;", testerScript);
            Assert.Contains("using Lakona.Rpc.Core;", testerScript);
            Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", testerScript);
            Assert.Contains("using Lakona.Rpc.Serializer.Json;", testerScript);
            Assert.Contains("new WsTransport($\"ws://{_endpoint.Host}:{_endpoint.Port}{NormalizePath(_endpoint.Path)}\")", testerScript);
            Assert.Contains("new JsonRpcSerializer()", testerScript);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", testerScript);
            Assert.Contains("private static void ConfigureTransportSecurity(TransportSecurityConfig security)", testerScript);
            Assert.Contains("_client.Api.Shared.Ping.PingAsync", testerScript);
            Assert.DoesNotContain("}, _cts.Token);", testerScript, StringComparison.Ordinal);
            Assert.Contains("                });", testerScript);
            Assert.Contains("public string Path = string.Empty;", testerScript);
            Assert.Contains("Path = \"/ws\"", testerScript);
            Assert.Contains("guid: 8fbb7dbe54784d7995143ce24cf85121", testerScriptMeta);
            Assert.Contains("guid: 8fbb7dbe54784d7995143ce24cf85121", scene);
            Assert.Contains("Path: /ws", scene);
            Assert.Contains("m_Name: RpcConnectionTester", scene);
            Assert.Contains("[InitializeOnLoad]", autoOpenSceneScript);
            Assert.Contains("EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);", autoOpenSceneScript);
            Assert.Contains("SessionState.GetBool(SessionStateKey, false)", autoOpenSceneScript);
            Assert.False(File.Exists(legacyEditorScriptPath));
            Assert.Contains("Assets/Scenes/ConnectionTest.unity", editorBuildSettings);
            Assert.Contains("guid: d4d2d5faafe942e58a33f4a41e3b7cf2", editorBuildSettings);
            Assert.False(File.Exists(starterGeneratedAsmdefPath));
            Assert.False(File.Exists(embeddedNuGetForUnity));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesServerProgram_WithTransportAndSerializerSpecificBuilderChain()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Builder-Test", ClientEngineKind.Unity, TransportKind.Tcp, SerializerKind.MemoryPack, Versions);

            var serverProgram = File.ReadAllText(Path.Combine(root, "Server", "Server", "Program.cs"));

            Assert.Contains("using Lakona.Rpc.Core;", serverProgram);
            Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", serverProgram);
            Assert.Contains("using Lakona.Rpc.Transport.Tcp;", serverProgram);
            Assert.Contains(".UseSerializer(new MemoryPackRpcSerializer())", serverProgram);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", serverProgram);
            Assert.Contains("builder.UseAcceptor(new TcpConnectionAcceptor(builder.ResolvePort(20000)));", serverProgram);
            Assert.DoesNotContain(".UseJson()", serverProgram, StringComparison.Ordinal);
            Assert.DoesNotContain(".UseWebSocket(", serverProgram, StringComparison.Ordinal);
            Assert.DoesNotContain(".UseMemoryPack()", serverProgram, StringComparison.Ordinal);
            Assert.DoesNotContain(".UseTcp(", serverProgram, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesMemoryPackClientPackages_WithRequiredUnityDependencies()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "MemoryPack-Test", ClientEngineKind.Unity, TransportKind.Tcp, SerializerKind.MemoryPack, Versions);

            var packagesConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "packages.config"));

            Assert.Contains("<package id=\"Lakona.Rpc.Core\" version=\"1.2.3\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Transport.Tcp\" version=\"4.5.6\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Serializer.MemoryPack\" version=\"5.6.7\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"MemoryPack\" version=\"6.7.8\" />", packagesConfig);
            Assert.Contains("<package id=\"MemoryPack.Core\" version=\"8.9.10\" />", packagesConfig);
            Assert.Contains("<package id=\"MemoryPack.Generator\" version=\"6.7.8\" />", packagesConfig);
            Assert.Contains("<package id=\"Microsoft.CodeAnalysis.Common\" version=\"5.3.0\" />", packagesConfig);
            Assert.Contains("<package id=\"Microsoft.CodeAnalysis.CSharp\" version=\"5.3.0\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Collections.Immutable\" version=\"10.0.6\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Reflection.Metadata\" version=\"10.0.7\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Text.Encoding.CodePages\" version=\"10.0.7\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Threading.Tasks.Extensions\" version=\"4.5.4\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Memory\" version=\"4.5.4\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Runtime.CompilerServices.Unsafe\" version=\"6.1.2\" />", packagesConfig);
            Assert.Contains("<package id=\"System.IO.Pipelines\" version=\"10.0.6\" />", packagesConfig);
            var serverCsproj = File.ReadAllText(Path.Combine(root, "Server", "Server", "Server.csproj"));
            Assert.Contains("<ProjectReference Include=\"..\\..\\Shared\\Shared.csproj\" />", serverCsproj);
            Assert.DoesNotContain("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\"", serverCsproj, StringComparison.Ordinal);
            var sharedCsproj = File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj"));
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\" Version=\"5.6.7\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"MemoryPack\" Version=\"6.7.8\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"MemoryPack.Generator\" Version=\"6.7.8\">", sharedCsproj);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesKcpClientPackages_WithRequiredUnityDependencies()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Kcp-Test", ClientEngineKind.Unity, TransportKind.Kcp, SerializerKind.MemoryPack, Versions);

            var packagesConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "packages.config"));
            var testerScript = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Testing", "RpcConnectionTester.cs"));
            var scene = File.ReadAllText(Path.Combine(root, "Client", "Assets", "Scenes", "ConnectionTest.unity"));
            var sharedDtos = File.ReadAllText(Path.Combine(root, "Shared", "Interfaces", "SharedDtos.cs"));

            Assert.Contains("<package id=\"Lakona.Rpc.Transport.Kcp\" version=\"4.5.6\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"Kcp\" version=\"2.7.0\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Memory\" version=\"4.5.4\" />", packagesConfig);
            Assert.Contains("<package id=\"System.Threading.Tasks.Extensions\" version=\"4.5.4\" />", packagesConfig);
            Assert.Contains("<package id=\"Lakona.Rpc.Serializer.MemoryPack\" version=\"5.6.7\" manuallyInstalled=\"true\" />", packagesConfig);
            Assert.Contains("<package id=\"MemoryPack\" version=\"6.7.8\" />", packagesConfig);
            Assert.Contains("using Lakona.Rpc.Transport.Kcp;", testerScript);
            Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", testerScript);
            Assert.Contains("using Lakona.Rpc.Core;", testerScript);
            Assert.Contains("new KcpTransport(_endpoint.Host, _endpoint.Port)", testerScript);
            Assert.Contains("new MemoryPackRpcSerializer()", testerScript);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", testerScript);
            Assert.Contains("[MemoryPackable]", sharedDtos);
            Assert.DoesNotContain("GenerateType.VersionTolerant", sharedDtos, StringComparison.Ordinal);
            Assert.Contains("public sealed partial class PingRequest", sharedDtos);
            Assert.Contains("public sealed partial class PingReply", sharedDtos);
            Assert.Contains("Path: ", scene);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesGodotClientFiles_WithSourceGenerator()
    {
        var root = CreateTempRoot();
        try
        {
            var commands = new List<string>();
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(commands), CreateFakeGitRunner());
            var sdkSource = CreateFakeGodotSdkSource(root, "4.6.1");

            WithGodotSdkSource(
                sdkSource,
                () => generator.GenerateTemplate(root, "Godot-Test", ClientEngineKind.Godot, TransportKind.WebSocket, SerializerKind.Json, Versions));

            var projectFile = File.ReadAllText(Path.Combine(root, "Client", "project.godot"));
            var clientCsproj = File.ReadAllText(Path.Combine(root, "Client", "Client.csproj"));
            var nugetConfig = File.ReadAllText(Path.Combine(root, "Client", "NuGet.config"));
            var clientReadme = File.ReadAllText(Path.Combine(root, "Client", "README.md"));
            var scene = File.ReadAllText(Path.Combine(root, "Client", "Main.tscn"));
            var testerScript = File.ReadAllText(Path.Combine(root, "Client", "Scripts", "Rpc", "Testing", "RpcConnectionTester.cs"));
            var generatedClientApi = Path.Combine(root, "Client", "Scripts", "Rpc", "Generated", "RpcApi.cs");

            Assert.DoesNotContain(commands, static command => command.Contains("lakona-rpc-codegen", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("config/name=\"Godot-Test\"", projectFile);
            Assert.Contains("run/main_scene=\"res://Main.tscn\"", projectFile);
            Assert.Contains("config/features=PackedStringArray(\"4.6\", \"C#\")", projectFile);
            Assert.Contains("project/assembly_name=\"Client\"", projectFile);
            Assert.DoesNotContain("websocket", projectFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("json", projectFile, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<Project Sdk=\"Godot.NET.Sdk/4.6.1\">", clientCsproj);
            Assert.Contains("<TargetFramework>net8.0</TargetFramework>", clientCsproj);
            Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", clientCsproj);
            Assert.Contains("<NuGetAudit>false</NuGetAudit>", clientCsproj);
            Assert.Contains("<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>", clientCsproj);
            Assert.Contains("<LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>", clientCsproj);
            Assert.Contains("<ProjectReference Include=\"..\\Shared\\Shared.csproj\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Transport.WebSocket\" Version=\"4.5.6\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.Json\" Version=\"5.6.7\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Analyzers\" Version=\"0.1.2\">", clientCsproj);
            Assert.Contains("<PrivateAssets>all</PrivateAssets>", clientCsproj);
            Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", clientCsproj);
            Assert.DoesNotContain("LakonaRpcGenerateCode", clientCsproj, StringComparison.Ordinal);
            Assert.DoesNotContain("lakona-rpc-codegen", clientCsproj, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<add key=\"godot-local\" value=\"" + sdkSource + "\" />", nugetConfig);
            Assert.Contains("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj")));
            Assert.Contains("Godot 4.6", clientReadme);
            Assert.Contains(sdkSource, clientReadme);
            Assert.Contains("[node name=\"Main\" type=\"Node\"]", scene);
            Assert.Contains("path=\"res://Scripts/Rpc/Testing/RpcConnectionTester.cs\"", scene);
            Assert.Contains("using Godot;", testerScript);
            Assert.Contains("using Lakona.Rpc.Core;", testerScript);
            Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", testerScript);
            Assert.Contains("using Lakona.Rpc.Serializer.Json;", testerScript);
            Assert.DoesNotContain("namespace Rpc.Testing", testerScript, StringComparison.Ordinal);
            Assert.Contains("public partial class RpcConnectionTester : Node", testerScript);
            Assert.Contains("new WsTransport($\"ws://{_host}:{_port}{NormalizePath(_path)}\")", testerScript);
            Assert.Contains("new JsonRpcSerializer()", testerScript);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", testerScript);
            Assert.Contains("GD.Print($\"Ping ok:", testerScript);
            Assert.Contains("[Export] private string _path = \"/ws\";", testerScript);
            Assert.Contains("public override void _Ready()", testerScript);
            Assert.Contains("_ = ConnectAndPingAsync();", testerScript);
            Assert.Contains("public async Task ConnectAndPingAsync()", testerScript);
            Assert.Contains("public override void _ExitTree()", testerScript);
            Assert.Contains("_ = ShutdownAsync();", testerScript);
            Assert.False(File.Exists(generatedClientApi));
            Assert.False(File.Exists(Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Generated", "RpcApi.cs")));
            Assert.False(File.Exists(Path.Combine(root, "codegen.ps1")));
            Assert.False(File.Exists(Path.Combine(root, "codegen.sh")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_GodotStableFiles_MatchGoldenFiles()
    {
        var root = CreateTempRoot();
        var sdkSource = CreateFakeGodotSdkSource(root, "4.6.1");
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            WithGodotSdkSource(sdkSource,
                () => generator.GenerateTemplate(root, "Godot-Golden", ClientEngineKind.Godot, TransportKind.WebSocket, SerializerKind.Json, Versions));

            AssertGoldenFile("GodotWebSocketJson", "project.godot", File.ReadAllText(Path.Combine(root, "Client", "project.godot")));
            AssertGoldenFile("GodotWebSocketJson", "Main.tscn", File.ReadAllText(Path.Combine(root, "Client", "Main.tscn")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesGodotKcpMemoryPackClient_WithExpectedReferencesAndScript()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());
            var sdkSource = CreateFakeGodotSdkSource(root, "4.6.1");

            WithGodotSdkSource(
                sdkSource,
                () => generator.GenerateTemplate(root, "Godot-Kcp", ClientEngineKind.Godot, TransportKind.Kcp, SerializerKind.MemoryPack, Versions));

            var projectFile = File.ReadAllText(Path.Combine(root, "Client", "project.godot"));
            var clientCsproj = File.ReadAllText(Path.Combine(root, "Client", "Client.csproj"));
            var nugetConfig = File.ReadAllText(Path.Combine(root, "Client", "NuGet.config"));
            var testerScript = File.ReadAllText(Path.Combine(root, "Client", "Scripts", "Rpc", "Testing", "RpcConnectionTester.cs"));
            var generatedClientApi = Path.Combine(root, "Client", "Scripts", "Rpc", "Generated", "RpcApi.cs");

            Assert.Contains("config/features=PackedStringArray(\"4.6\", \"C#\")", projectFile);
            Assert.Contains("project/assembly_name=\"Client\"", projectFile);
            Assert.DoesNotContain("config/features=PackedStringArray(\"4.6\", \"C#\", \"kcp\"", projectFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("config/features=PackedStringArray(\"4.6\", \"C#\", \"memorypack\"", projectFile, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("<Project Sdk=\"Godot.NET.Sdk/4.6.1\">", clientCsproj);
            Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", clientCsproj);
            Assert.Contains("<NuGetAudit>false</NuGetAudit>", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Transport.Kcp\" Version=\"4.5.6\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Analyzers\" Version=\"0.1.2\">", clientCsproj);
            Assert.Contains("<PrivateAssets>all</PrivateAssets>", clientCsproj);
            Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", clientCsproj);
            Assert.Contains("<ProjectReference Include=\"..\\Shared\\Shared.csproj\" />", clientCsproj);
            Assert.DoesNotContain("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\"", clientCsproj, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageReference Include=\"MemoryPack\"", clientCsproj, StringComparison.Ordinal);
            Assert.DoesNotContain("<PackageReference Include=\"MemoryPack.Core\"", clientCsproj, StringComparison.Ordinal);
            Assert.Contains("<add key=\"godot-local\" value=\"" + sdkSource + "\" />", nugetConfig);
            var sharedCsproj = File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj"));
            Assert.Contains("<TargetFrameworks>net8.0;net10.0</TargetFrameworks>", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.MemoryPack\" Version=\"5.6.7\" />", sharedCsproj);
            Assert.Contains("<PackageReference Include=\"MemoryPack\" Version=\"6.7.8\" />", sharedCsproj);

            Assert.Contains("using Lakona.Rpc.Transport.Kcp;", testerScript);
            Assert.Contains("using Lakona.Rpc.Serializer.MemoryPack;", testerScript);
            Assert.Contains("using Lakona.Rpc.Core;", testerScript);
            Assert.DoesNotContain("namespace Rpc.Testing", testerScript, StringComparison.Ordinal);
            Assert.Contains("public partial class RpcConnectionTester : Node", testerScript);
            Assert.Contains("new KcpTransport(_host, _port)", testerScript);
            Assert.Contains("new MemoryPackRpcSerializer()", testerScript);
            Assert.Contains(".UseSecurity(ConfigureTransportSecurity)", testerScript);
            Assert.Contains("[Export] private string _path = \"\";", testerScript);
            Assert.Contains("if (_isShuttingDown || _client is not null)", testerScript);
            Assert.False(File.Exists(generatedClientApi));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesConsoleClientFiles_WithSourceGenerator()
    {
        var root = CreateTempRoot();
        try
        {
            var commands = new List<string>();
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(commands), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Console-Test", ClientEngineKind.Console, TransportKind.WebSocket, SerializerKind.Json, Versions);

            var sharedCsproj = File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj"));
            var clientCsproj = File.ReadAllText(Path.Combine(root, "Client", "Client.csproj"));
            var program = File.ReadAllText(Path.Combine(root, "Client", "Program.cs"));
            var clientReadme = File.ReadAllText(Path.Combine(root, "Client", "README.md"));
            var solution = File.ReadAllText(Path.Combine(root, "Server", "Server.slnx"));

            Assert.Contains("<TargetFrameworks>net10.0</TargetFrameworks>", sharedCsproj);
            Assert.Contains("<OutputType>Exe</OutputType>", clientCsproj);
            Assert.Contains("<TargetFramework>net10.0</TargetFramework>", clientCsproj);
            Assert.Contains("<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>", clientCsproj);
            Assert.Contains("<LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>", clientCsproj);
            Assert.Contains("<ProjectReference Include=\"..\\Shared\\Shared.csproj\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Core\" Version=\"1.2.3\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Client\" Version=\"3.4.5\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Transport.WebSocket\" Version=\"4.5.6\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Serializer.Json\" Version=\"5.6.7\" />", clientCsproj);
            Assert.Contains("<PackageReference Include=\"Lakona.Rpc.Analyzers\" Version=\"0.1.2\">", clientCsproj);
            Assert.Contains("<PrivateAssets>all</PrivateAssets>", clientCsproj);
            Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", clientCsproj);
            Assert.Contains("using Rpc.Generated;", program);
            Assert.Contains("using Shared.Interfaces;", program);
            Assert.Contains("using Lakona.Rpc.Transport.WebSocket;", program);
            Assert.Contains("using Lakona.Rpc.Serializer.Json;", program);
            Assert.Contains("new WsTransport($\"ws://{host}:{port}{NormalizePath(path)}\")", program);
            Assert.Contains("new JsonRpcSerializer()", program);
            Assert.Contains("await client.ConnectAsync();", program);
            Assert.Contains("client.Api.Shared.Ping.PingAsync", program);
            Assert.Contains("Console.WriteLine($\"Ping ok:", program);
            Assert.Contains("ULINKRPC_HOST", program);
            Assert.Contains("Console Client Starter (.NET 10)", clientReadme);
            Assert.Contains("dotnet run --project Client.csproj -- hello", clientReadme);
            Assert.DoesNotContain("<Project Path=\"../Client/Client.csproj\" />", solution);
            Assert.DoesNotContain(commands, command => command.Contains($"{Path.DirectorySeparatorChar}Client{Path.DirectorySeparatorChar}Client.csproj", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesTuanjieClientFiles_UsingUnityCompatibleSourceGeneration()
    {
        var root = CreateTempRoot();
        try
        {
            var commands = new List<string>();
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(commands), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Tuanjie-Test", ClientEngineKind.Tuanjie, TransportKind.Tcp, SerializerKind.Json, Versions);

            var sharedCsproj = File.ReadAllText(Path.Combine(root, "Shared", "Shared.csproj"));
            var clientReadme = File.ReadAllText(Path.Combine(root, "Client", "README.md"));
            var manifestJson = File.ReadAllText(Path.Combine(root, "Client", "Packages", "manifest.json"));
            var projectVersion = File.ReadAllText(Path.Combine(root, "Client", "ProjectSettings", "ProjectVersion.txt"));
            var nugetConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "NuGet.config"));
            var generatedClientApi = Path.Combine(root, "Client", "Assets", "Scripts", "Rpc", "Generated", "RpcApi.cs");
            var embeddedNuGetForUnity = Path.Combine(root, "Client", "Packages", "com.github-glitchenzo.nugetforunity", "package.json");

            Assert.Contains("<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>", sharedCsproj);
            Assert.Contains("Tuanjie Client Starter (Tuanjie (Unity-compatible))", clientReadme);
            Assert.Contains("bundled embedded `NuGetForUnity` package", clientReadme);
            Assert.Contains("Open this folder with Tuanjie (Unity-compatible).", clientReadme);
            Assert.Contains("file:../../Shared", manifestJson);
            Assert.DoesNotContain("package.openupm.com", manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("com.github-glitchenzo.nugetforunity", manifestJson, StringComparison.Ordinal);
            Assert.Contains("m_EditorVersion: 2022.3.61t11", projectVersion);
            Assert.Contains("m_EditorVersionWithRevision: 2022.3.61t11 (122146d53e32)", projectVersion);
            Assert.Contains("m_TuanjieEditorVersion: 1.6.10", projectVersion);
            Assert.Contains("<add key=\"nuget.org\" value=\"https://nuget.cdn.azure.cn/v3/index.json\" enableCredentialProvider=\"false\" />", nugetConfig);
            Assert.DoesNotContain(commands, static command => command.Contains("lakona-rpc-codegen", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(embeddedNuGetForUnity));
            Assert.False(File.Exists(generatedClientApi));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateTemplate_CreatesUnityCnClientFiles_WithEmbeddedNuGetForUnityByDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());

            generator.GenerateTemplate(root, "Unity-Cn-Test", ClientEngineKind.UnityCn, TransportKind.Tcp, SerializerKind.Json, Versions);

            var clientReadme = File.ReadAllText(Path.Combine(root, "Client", "README.md"));
            var manifestJson = File.ReadAllText(Path.Combine(root, "Client", "Packages", "manifest.json"));
            var projectVersion = File.ReadAllText(Path.Combine(root, "Client", "ProjectSettings", "ProjectVersion.txt"));
            var nugetConfig = File.ReadAllText(Path.Combine(root, "Client", "Assets", "NuGet.config"));
            var embeddedNuGetForUnity = Path.Combine(root, "Client", "Packages", "com.github-glitchenzo.nugetforunity", "package.json");

            Assert.Contains("Unity CN Client Starter (Unity 2022 LTS (China-friendly defaults))", clientReadme);
            Assert.Contains("bundled embedded `NuGetForUnity` package", clientReadme);
            Assert.Contains("file:../../Shared", manifestJson);
            Assert.DoesNotContain("package.openupm.com", manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("com.github-glitchenzo.nugetforunity", manifestJson, StringComparison.Ordinal);
            Assert.Contains("m_EditorVersion: 2022.3.62f3c1", projectVersion);
            Assert.Contains("m_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)", projectVersion);
            Assert.Contains("<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" enableCredentialProvider=\"false\" />", nugetConfig);
            Assert.True(File.Exists(embeddedNuGetForUnity));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StarterWorkspace_DetectsUnityCnStarterProject()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());
            generator.GenerateTemplate(root, "Unity-Cn-Test", ClientEngineKind.UnityCn, TransportKind.Tcp, SerializerKind.Json, Versions);

            var found = StarterWorkspace.TryResolveProjectContext(Path.Combine(root, "Client"), out var context, out var error);

            Assert.True(found, error);
            Assert.Equal(ClientEngineKind.UnityCn, context.ClientEngine);
            Assert.Equal(Path.Combine(root, "Client"), context.ClientPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StarterWorkspace_DetectsConsoleStarterProject()
    {
        var root = CreateTempRoot();
        try
        {
            var generator = new StarterTemplateGenerator(CreateFakeDotNetRunner(), CreateFakeGitRunner());
            generator.GenerateTemplate(root, "Console-Test", ClientEngineKind.Console, TransportKind.Tcp, SerializerKind.Json, Versions);

            var found = StarterWorkspace.TryResolveProjectContext(Path.Combine(root, "Client"), out var context, out var error);

            Assert.True(found, error);
            Assert.Equal(ClientEngineKind.Console, context.ClientEngine);
            Assert.Equal(Path.Combine(root, "Client"), context.ClientPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClientEngine_DefaultNuGetForUnitySource_MatchesExpected()
    {
        Assert.Equal(NuGetForUnitySourceKind.OpenUpm, ClientEngineKind.Unity.GetDefaultNuGetForUnitySource());
        Assert.Equal(NuGetForUnitySourceKind.Embedded, ClientEngineKind.UnityCn.GetDefaultNuGetForUnitySource());
        Assert.Equal(NuGetForUnitySourceKind.Embedded, ClientEngineKind.Tuanjie.GetDefaultNuGetForUnitySource());
        Assert.Equal(NuGetForUnitySourceKind.Embedded, ClientEngineKind.Console.GetDefaultNuGetForUnitySource());
    }

    [Fact]
    public void ToolReadme_DocumentsToolUsage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Tool", "README.md"));

        Assert.Contains("lakona-tool", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("lakona-starter", readme, StringComparison.OrdinalIgnoreCase);
    }


    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lakona-rpc_starter_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertGoldenFile(string scenario, string fileName, string actual)
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "RpcStarter", "Golden", scenario, fileName);
        var expected = File.ReadAllText(goldenPath);
        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual));
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from test output directory.");
    }

    private static string ReadProjectVersion(string repositoryRoot, params string[] pathParts)
    {
        var project = XDocument.Load(Path.Combine([repositoryRoot, .. pathParts]));
        return project.Root?
            .Elements("PropertyGroup")
            .Elements("Version")
            .Select(element => element.Value)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? throw new InvalidOperationException($"Project version not found in {Path.Combine(pathParts)}.");
    }

    private static string ReadPackageReferenceVersion(string repositoryRoot, string directory, string projectDirectory, string projectFile, string packageId)
    {
        var project = XDocument.Load(Path.Combine(repositoryRoot, directory, projectDirectory, projectFile));
        return project.Root?
            .Elements("ItemGroup")
            .Elements("PackageReference")
            .Where(element => string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("Version"))
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? throw new InvalidOperationException($"Package reference '{packageId}' not found in {projectFile}.");
    }

    private static string CreateFakeGodotSdkSource(string root, string version)
    {
        var dir = Path.Combine(root, "fake-godot-sdk");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"Godot.NET.Sdk.{version}.nupkg"), string.Empty);
        return dir;
    }

    private static void WithGodotSdkSource(string path, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("ULINKRPC_GODOT_NUPKGS");
        Environment.SetEnvironmentVariable("ULINKRPC_GODOT_NUPKGS", path);

        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ULINKRPC_GODOT_NUPKGS", previous);
        }
    }

    private static Action<string, string> CreateFakeDotNetRunner(List<string>? commands = null)
    {
        return (workingDirectory, arguments) =>
        {
            commands?.Add(arguments);

            if (arguments.StartsWith("new sln -n ", StringComparison.Ordinal))
            {
                var nameAndOptions = arguments["new sln -n ".Length..];
                var formatIndex = nameAndOptions.IndexOf(" --format ", StringComparison.Ordinal);
                var solutionName = (formatIndex >= 0 ? nameAndOptions[..formatIndex] : nameAndOptions)
                    .Trim()
                    .Trim('"');
                var slnxPath = Path.Combine(workingDirectory, $"{solutionName}.slnx");
                File.WriteAllText(slnxPath, "<Solution>\n</Solution>\n");
                return;
            }

            if (arguments.StartsWith("sln ", StringComparison.Ordinal) && arguments.Contains(" add ", StringComparison.Ordinal))
            {
                var addIndex = arguments.IndexOf(" add ", StringComparison.Ordinal);
                var slnxPath = arguments["sln ".Length..addIndex].Trim().Trim('"');
                var projectPath = arguments[(addIndex + " add ".Length)..].Trim().Trim('"').Replace('\\', '/');
                var solution = File.ReadAllText(slnxPath);
                var projectEntry = $"  <Project Path=\"{projectPath}\" />\n";
                solution = solution.Replace("</Solution>\n", projectEntry + "</Solution>\n", StringComparison.Ordinal);
                File.WriteAllText(slnxPath, solution);
                return;
            }

            if (arguments == "build-server shutdown")
            {
                return;
            }

            throw new InvalidOperationException($"Unexpected dotnet command in test: {arguments}");
        };
    }

    private static Action<string, string> CreateFakeGitRunner(List<string>? commands = null)
    {
        return (workingDirectory, arguments) =>
        {
            commands?.Add(arguments);

            if (string.Equals(arguments, "init", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(workingDirectory, ".git"));
                return;
            }

            throw new InvalidOperationException($"Unexpected git command in test: {arguments}");
        };
    }
}
