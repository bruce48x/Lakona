using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class MembershipProviderValidationRepositoryTests
{
    private const string RequiredEnvironmentVariable = "LAKONA_REQUIRE_MEMBERSHIP_PROVIDER_TESTS";

    [Fact]
    public void DailyValidation_RunsEveryMembershipProviderAgainstTheSharedContract()
    {
        var root = GitChangeSetReader.FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "daily-validation.yml"));
        var regularWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-nuget.yml"));

        AssertProvider(root, workflow, "InMemory", "InMemory", "InMemoryMembershipTableTests", null, null);
        AssertProvider(root, workflow, "Postgres", "PostgreSQL", "PostgresMembershipTableTests", "postgres", "LAKONA_TEST_POSTGRES_CONNECTION");
        AssertProvider(root, workflow, "Redis", "Redis", "RedisMembershipTableTests", "redis", "LAKONA_TEST_REDIS_CONNECTION");
        AssertProvider(root, workflow, "MySql", "MySQL", "MySqlMembershipTableTests", "mysql", "LAKONA_TEST_MYSQL_CONNECTION");
        Assert.Equal(3, workflow.Split(RequiredEnvironmentVariable, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(RequiredEnvironmentVariable, regularWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void GodotValidation_GeneratesThePostgresMembershipConfiguration()
    {
        var root = GitChangeSetReader.FindRepositoryRoot();
        var validationScript = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "game",
            "ci",
            "verify-lakona-tool-godot.ps1"));

        Assert.Contains(
            "\"--membership-provider\", \"postgres\"",
            validationScript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"add\", $serverProject, \"package\", \"Lakona.Game.Clustering.Postgres\"",
            validationScript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Generated Server.App Program.cs does not expose the expected service-registration block.",
            validationScript,
            StringComparison.Ordinal);
    }

    private static void AssertProvider(
        string root,
        string workflow,
        string category,
        string displayName,
        string testClass,
        string? service,
        string? connectionVariable)
    {
        var source = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Lakona.Game.Cluster.Tests",
            $"{testClass}.cs"));

        Assert.Contains($"class {testClass} : MembershipTableContractTests", source, StringComparison.Ordinal);
        Assert.Contains($"[Trait(\"Category\", \"{category}Integration\")]", source, StringComparison.Ordinal);
        Assert.Contains($"Run mandatory {displayName} Membership contract", workflow, StringComparison.Ordinal);
        Assert.Contains($"--filter \"Category={category}Integration\"", workflow, StringComparison.Ordinal);

        if (service is not null)
        {
            Assert.Contains($"      {service}:", workflow, StringComparison.Ordinal);
        }

        if (connectionVariable is not null)
        {
            Assert.Contains(connectionVariable, source, StringComparison.Ordinal);
            Assert.Contains($"          {connectionVariable}:", workflow, StringComparison.Ordinal);
        }
    }
}
