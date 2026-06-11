using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Server;

internal sealed class HotfixRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Hotfix/Server.Hotfix.csproj", RenderProject(), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/Hotfix/Login/LoginService.cs", RenderLoginService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatService.cs", RenderChatService(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Server.Hotfix</RootNamespace>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0" />
            <ProjectReference Include="..\App\Server.App.csproj" ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderLoginService()
    {
        return """
        namespace Server.Hotfix.Login;

        public sealed class LoginService
        {
            public ValueTask<string> NormalizePlayerNameAsync(string? playerName)
            {
                var normalized = string.IsNullOrWhiteSpace(playerName)
                    ? "Player"
                    : playerName.Trim();

                return new ValueTask<string>(normalized);
            }
        }
        """;
    }

    private static string RenderChatService()
    {
        return """
        namespace Server.Hotfix.Chat;

        public sealed class ChatService
        {
            public ValueTask<string> FilterMessageAsync(string? message)
            {
                return new ValueTask<string>((message ?? string.Empty).Trim());
            }
        }
        """;
    }
}
