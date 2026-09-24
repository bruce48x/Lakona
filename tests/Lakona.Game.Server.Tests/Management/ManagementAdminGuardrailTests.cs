using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Management;

public sealed class ManagementAdminGuardrailTests
{
    [Fact]
    public void Validate_rejects_loopback_only_admin_on_non_loopback_listener()
    {
        var result = Validate(TestRuntime("0.0.0.0", requireLoopback: true));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA10130");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Management:Admin:RequireLoopback", diagnostic.Message);
    }

    [Fact]
    public void Validate_accepts_explicit_trusted_network_admin_listener()
    {
        var result = Validate(TestRuntime("0.0.0.0", requireLoopback: false));

        Assert.Empty(result.Diagnostics);
    }

    private static LakonaGameValidationResult Validate(LakonaGameRuntimeOptions runtime)
        => new LakonaGameRuntimeValidator().Validate(runtime,
            hotfixAssemblyPath: typeof(ManagementAdminGuardrailTests).Assembly.Location);

    private static LakonaGameRuntimeOptions TestRuntime(string host, bool requireLoopback) => new()
    {
        Node = new LakonaGameNodeOptions { Id = "dev-1" },
        Management = new LakonaManagementOptions
        {
            Http = new LakonaManagementHttpOptions { Host = host },
            Admin = new LakonaManagementAdminOptions { Enabled = true, RequireLoopback = requireLoopback }
        }
    };
}
