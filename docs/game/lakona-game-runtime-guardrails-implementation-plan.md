# Lakona.Game Runtime Guardrails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Historical note:** This plan is superseded for configuration and startup shape by [Lakona.Game Configuration And Startup Model](lakona-game-configuration-startup.md). Old singular endpoint examples and service-shaped cluster examples below are historical only; current guidance uses `Lakona.Game:Endpoints[]`, `Lakona.Game:Node:Id`, compact `Lakona.Game:Feature`, and the `AddLakonaGame` Feature Catalog.

**Goal:** Build the first runtime guardrails loop so generated projects and server startup can validate Lakona.Game runtime invariants with shared framework diagnostics.

**Architecture:** Add small diagnostic and validation primitives to `Lakona.Game.Server`, introduce a resolved runtime model that records final values and provenance, implement the first low-risk validation rules, then make generated `--lakona-game-check` consume the framework validation model. Keep rule ownership in runtime packages and keep generated code responsible only for project-specific presentation.

**Tech Stack:** C#/.NET 10, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Configuration, xUnit v3, Lakona.Game.Server, Lakona.Tool templates.

---

## File Structure

- Create `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnosticSeverity.cs`: diagnostic severity enum.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnostic.cs`: stable diagnostic record with code, severity, message, and optional repair.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameValidationResult.cs`: aggregate validation result.
- Create `src/Lakona.Game.Server/Guardrails/ILakonaGameValidationRule.cs`: small rule interface.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeProfile.cs`: framework-owned profile values.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameValueSource.cs`: provenance enum.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedValue.cs`: value plus source/path.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedEndpoint.cs`: resolved endpoint data.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedHotfix.cs`: resolved hotfix source data.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedCluster.cs`: resolved cluster service data.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedReliablePush.cs`: resolved reliable push data.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`: aggregate runtime model.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeValidator.cs`: rule aggregator.
- Create `src/Lakona.Game.Server/Guardrails/Rules/NodeIdentityRule.cs`: node id validation.
- Create `src/Lakona.Game.Server/Guardrails/Rules/EndpointRule.cs`: transport/path/advertised endpoint validation.
- Create `src/Lakona.Game.Server/Guardrails/Rules/HotfixSourceRule.cs`: hotfix assembly presence validation.
- Create `src/Lakona.Game.Server/Guardrails/Rules/ClusterServiceGraphRule.cs`: duplicate service validation.
- Create `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`: DI registration.
- Modify `src/Lakona.Tool/Scaffolding/ToolTemplates.cs`: generated check command calls framework validation model and supports `--json`.
- Modify `Tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj`: add configuration/DI package references only if tests require them.
- Create `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`: unit tests for rules.
- Modify `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`: generated check command expectations.
- Modify `src/Lakona.Game.Server/Lakona.Game.Server.csproj`: bump package version before shipping runtime changes.
- Modify `src/Lakona.Tool/Lakona.Tool.csproj`: bump package version before shipping template changes.
- Modify `CHANGELOG.md`: note package changes and versions.

## Scope Boundary

This plan implements the first usable guardrails loop only. It does not implement full production-readiness validation, durable Reliable Push policy, or split-node topology validation. Those are later phases once the resolved runtime model and check integration are stable.

The first loop covers:

- diagnostic result types
- resolved runtime model with value provenance
- node id validation
- endpoint transport/path validation
- duplicate cluster service validation
- hotfix assembly presence validation
- `--lakona-game-check --json`
- generated check command reusing the framework validation result

## Task 1: Add Guardrail Diagnostic Primitives

**Files:**
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnosticSeverity.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnostic.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameValidationResult.cs`
- Test: `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Write failing tests for diagnostic success and failure**

Create `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`:

```csharp
using Lakona.Game.Server.Guardrails;
using Xunit;

namespace Lakona.Game.Server.Tests.Guardrails;

public sealed class LakonaGameRuntimeValidatorTests
{
    [Fact]
    public void ValidationResult_Succeeds_WhenNoErrorDiagnosticsExist()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("ULINK000", LakonaGameDiagnosticSeverity.Info, "ok"),
                new LakonaGameDiagnostic("ULINK050", LakonaGameDiagnosticSeverity.Warning, "local default")
            ]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationResult_Fails_WhenAnyErrorDiagnosticExists()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("ULINK001", LakonaGameDiagnosticSeverity.Error, "Node id is required.")
            ]);

        Assert.False(result.Succeeded);
    }
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path .).Path
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_NOLOGO='1'
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: FAIL because `Lakona.Game.Server.Guardrails` types do not exist.

- [ ] **Step 3: Add diagnostic primitives**

Create `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnosticSeverity.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public enum LakonaGameDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameDiagnostic.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameDiagnostic(
    string Code,
    LakonaGameDiagnosticSeverity Severity,
    string Message,
    string? Repair = null);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameValidationResult.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameValidationResult(
    IReadOnlyList<LakonaGameDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != LakonaGameDiagnosticSeverity.Error);

    public static LakonaGameValidationResult Success { get; } = new([]);
}
```

- [ ] **Step 4: Run the test and verify it passes**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit diagnostic primitives**

Run:

```powershell
git add src/Lakona.Game.Server/Guardrails Tests/Lakona.Game.Server.Tests/Guardrails
git commit -m "feat: add runtime guardrail diagnostics"
```

## Task 2: Add Resolved Runtime Model

**Files:**
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeProfile.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameValueSource.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedValue.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedEndpoint.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedHotfix.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedCluster.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedReliablePush.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`
- Test: `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Add failing tests for value provenance and default runtime construction**

Append to `LakonaGameRuntimeValidatorTests`:

```csharp
[Fact]
public void ResolvedValue_PreservesValueSourceAndPath()
{
    var value = new LakonaGameResolvedValue<string>(
        "dev-1",
        LakonaGameValueSource.Configuration,
        "Lakona.Game:Node:Id");

    Assert.Equal("dev-1", value.Value);
    Assert.Equal(LakonaGameValueSource.Configuration, value.Source);
    Assert.Equal("Lakona.Game:Node:Id", value.Path);
}

[Fact]
public void ResolvedRuntime_CarriesCoreRuntimeSections()
{
    var runtime = TestRuntime();

    Assert.Equal("dev-1", runtime.NodeId.Value);
    Assert.Equal("kcp", runtime.Endpoints[0].Transport.Value);
    Assert.Equal("Server.Hotfix.dll", runtime.Hotfix.AssemblyFileName.Value);
    Assert.Equal(LakonaGameRuntimeProfile.Development, runtime.Profile);
}

private static LakonaGameResolvedRuntime TestRuntime()
{
    return new LakonaGameResolvedRuntime(
        NodeId: new LakonaGameResolvedValue<string>("dev-1", LakonaGameValueSource.Configuration, "Lakona.Game:Node:Id"),
        Endpoints:
        [
            new LakonaGameResolvedEndpoint(
                Transport: new LakonaGameResolvedValue<string>("kcp", LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Transport"),
                Host: new LakonaGameResolvedValue<string>("127.0.0.1", LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Host"),
                Port: new LakonaGameResolvedValue<int>(20000, LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Port"),
                Path: new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Default),
                AdvertisedEndpoint: new LakonaGameResolvedValue<string>("kcp://127.0.0.1:20000", LakonaGameValueSource.GeneratedConvention))
        ],
        Cluster: new LakonaGameResolvedCluster(
            Services: [new LakonaGameResolvedClusterService("gateway", "gateway")], // Historical/superseded: current startup uses Feature Catalog, not service-shaped framework config.
            AdvertisedEndpoints: new Dictionary<string, string> { ["client"] = "kcp://127.0.0.1:20000" }),
        Hotfix: new LakonaGameResolvedHotfix(
            AssemblyPath: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
            AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
        ReliablePush: new LakonaGameResolvedReliablePush(
            StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
            PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
            ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
            HasSessionIdentityResolver: true),
        Profile: LakonaGameRuntimeProfile.Development);
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: FAIL because resolved runtime model types do not exist.

- [ ] **Step 3: Add resolved runtime model types**

Create `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeProfile.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public enum LakonaGameRuntimeProfile
{
    Development,
    Compose,
    Production
}
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameValueSource.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public enum LakonaGameValueSource
{
    Default,
    Configuration,
    Environment,
    GeneratedConvention,
    Code
}
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedValue.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedValue<T>(
    T Value,
    LakonaGameValueSource Source,
    string? Path = null);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedEndpoint.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedEndpoint(
    LakonaGameResolvedValue<string> Transport,
    LakonaGameResolvedValue<string> Host,
    LakonaGameResolvedValue<int> Port,
    LakonaGameResolvedValue<string> Path,
    LakonaGameResolvedValue<string> AdvertisedEndpoint);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedHotfix.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedHotfix(
    LakonaGameResolvedValue<string> AssemblyPath,
    LakonaGameResolvedValue<string> AssemblyFileName);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedCluster.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedCluster(
    IReadOnlyList<LakonaGameResolvedClusterService> Services,
    IReadOnlyDictionary<string, string> AdvertisedEndpoints);

public sealed record LakonaGameResolvedClusterService(
    string Kind,
    string Name);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedReliablePush.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedReliablePush(
    LakonaGameResolvedValue<string> StorageMode,
    LakonaGameResolvedValue<int> PendingLimit,
    LakonaGameResolvedValue<int> ReplayWindowSeconds,
    bool HasSessionIdentityResolver);
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameResolvedRuntime.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameResolvedRuntime(
    LakonaGameResolvedValue<string> NodeId,
    LakonaGameResolvedEndpoint Endpoint,
    LakonaGameResolvedCluster Cluster,
    LakonaGameResolvedHotfix Hotfix,
    LakonaGameResolvedReliablePush ReliablePush,
    LakonaGameRuntimeProfile Profile);
```

- [ ] **Step 4: Run the test and verify it passes**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: PASS.

- [ ] **Step 5: Commit resolved runtime model**

Run:

```powershell
git add src/Lakona.Game.Server/Guardrails Tests/Lakona.Game.Server.Tests/Guardrails
git commit -m "feat: add resolved Lakona.Game runtime model"
```

## Task 3: Add First Validation Rules

**Files:**
- Create: `src/Lakona.Game.Server/Guardrails/ILakonaGameValidationRule.cs`
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeValidator.cs`
- Create: `src/Lakona.Game.Server/Guardrails/Rules/NodeIdentityRule.cs`
- Create: `src/Lakona.Game.Server/Guardrails/Rules/EndpointRule.cs`
- Create: `src/Lakona.Game.Server/Guardrails/Rules/HotfixSourceRule.cs`
- Create: `src/Lakona.Game.Server/Guardrails/Rules/ClusterServiceGraphRule.cs`
- Test: `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Add failing tests for validation rules**

Append to `LakonaGameRuntimeValidatorTests`:

```csharp
[Fact]
public void RuntimeValidator_Fails_WhenNodeIdIsMissing()
{
    var runtime = TestRuntime() with
    {
        NodeId = new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Configuration, "Lakona.Game:Node:Id")
    };
    var result = Validate(runtime);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK001");
}

[Fact]
public void RuntimeValidator_Fails_WhenWebSocketPathIsMissing()
{
    var runtime = TestRuntime() with
    {
        Endpoints =
        [
            TestRuntime().Endpoints[0] with
            {
                Transport = new LakonaGameResolvedValue<string>("websocket", LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Transport"),
                Path = new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Default)
            }
        ]
    };
    var result = Validate(runtime);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK023");
}

[Fact]
public void RuntimeValidator_Fails_WhenHotfixAssemblyIsMissing()
{
    var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Server.Hotfix.dll");
    var runtime = TestRuntime() with
    {
        Hotfix = TestRuntime().Hotfix with
        {
            AssemblyPath = new LakonaGameResolvedValue<string>(missingPath, LakonaGameValueSource.GeneratedConvention)
        }
    };
    var result = Validate(runtime);

    Assert.False(result.Succeeded);
    var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Code == "ULINK071"));
    Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    Assert.Equal("dotnet build Server/Hotfix/Server.Hotfix.csproj", diagnostic.Repair);
}

[Fact]
public void RuntimeValidator_Fails_WhenClusterServiceNameIsDuplicated()
{
    var runtime = TestRuntime() with
    {
        Cluster = TestRuntime().Cluster with
        {
            Services = // Historical/superseded: current startup uses Feature Catalog, not service-shaped framework config.
            [
                new LakonaGameResolvedClusterService("gateway", "gateway"),
                new LakonaGameResolvedClusterService("gateway", "gateway")
            ]
        }
    };
    var result = Validate(runtime);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK041");
}

private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
{
    var validator = new LakonaGameRuntimeValidator(
        [
            new NodeIdentityRule(),
            new EndpointRule(),
            new HotfixSourceRule(),
            new ClusterServiceGraphRule()
        ]);

    return validator.Validate(runtime);
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: FAIL because validation rules do not exist.

- [ ] **Step 3: Add validation rule interface and aggregator**

Create `src/Lakona.Game.Server/Guardrails/ILakonaGameValidationRule.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public interface ILakonaGameValidationRule
{
    IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime);
}
```

Create `src/Lakona.Game.Server/Guardrails/LakonaGameRuntimeValidator.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails;

public sealed class LakonaGameRuntimeValidator
{
    private readonly IReadOnlyList<ILakonaGameValidationRule> _rules;

    public LakonaGameRuntimeValidator(IEnumerable<ILakonaGameValidationRule> rules)
    {
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
    }

    public LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var diagnostics = new List<LakonaGameDiagnostic>();
        foreach (var rule in _rules)
        {
            diagnostics.AddRange(rule.Validate(runtime));
        }

        return new LakonaGameValidationResult(diagnostics);
    }
}
```

- [ ] **Step 4: Add node and endpoint rules**

Create `src/Lakona.Game.Server/Guardrails/Rules/NodeIdentityRule.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class NodeIdentityRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.NodeId.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK001",
                LakonaGameDiagnosticSeverity.Error,
                "Node id is required.",
                "Set Lakona.Game:Node:Id to a stable node id.");
        }
    }
}
```

Create `src/Lakona.Game.Server/Guardrails/Rules/EndpointRule.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class EndpointRule : ILakonaGameValidationRule
{
    private static readonly HashSet<string> KnownTransports = new(StringComparer.OrdinalIgnoreCase)
    {
        "kcp",
        "tcp",
        "websocket"
    };

    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var endpoint = runtime.Endpoints[0];
        var transport = endpoint.Transport.Value;
        if (!KnownTransports.Contains(transport))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK020",
                LakonaGameDiagnosticSeverity.Error,
                $"Endpoint transport '{transport}' is unknown.",
                "Use kcp, tcp, or websocket.");
            yield break;
        }

        if (string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(endpoint.Path.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK023",
                LakonaGameDiagnosticSeverity.Error,
                "WebSocket endpoint path is required.",
                "Set Lakona.Game:Endpoints:0:Path to /ws or another explicit WebSocket path.");
        }
    }
}
```

- [ ] **Step 5: Add hotfix and cluster rules**

Create `src/Lakona.Game.Server/Guardrails/Rules/HotfixSourceRule.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class HotfixSourceRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        if (!File.Exists(runtime.Hotfix.AssemblyPath.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK071",
                LakonaGameDiagnosticSeverity.Error,
                "Hotfix assembly was not found.",
                "dotnet build Server/Hotfix/Server.Hotfix.csproj");
        }
    }
}
```

Create `src/Lakona.Game.Server/Guardrails/Rules/ClusterServiceGraphRule.cs`:

```csharp
namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ClusterServiceGraphRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var duplicated = runtime.Cluster.Services
            .GroupBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicated is not null)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK041",
                LakonaGameDiagnosticSeverity.Error,
                $"Cluster service name '{duplicated.Key}' is duplicated.",
                "Use unique service names in the resolved cluster service list.");
        }
    }
}
```

- [ ] **Step 6: Fix test usings**

Add to the top of `LakonaGameRuntimeValidatorTests.cs`:

```csharp
using Lakona.Game.Server.Guardrails.Rules;
```

- [ ] **Step 7: Run the tests and verify they pass**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter LakonaGameRuntimeValidatorTests --no-restore
```

Expected: PASS.

- [ ] **Step 8: Commit validation rules**

Run:

```powershell
git add src/Lakona.Game.Server/Guardrails Tests/Lakona.Game.Server.Tests/Guardrails
git commit -m "feat: add initial runtime guardrail rules"
```

## Task 4: Add DI Registration For Guardrails

**Files:**
- Create: `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`
- Test: `Tests/Lakona.Game.Server.Tests/Guardrails/LakonaGameRuntimeValidatorTests.cs`

- [ ] **Step 1: Add failing DI registration test**

Append to `LakonaGameRuntimeValidatorTests.cs`:

```csharp
[Fact]
public void AddLakonaGameRuntimeValidation_RegistersDefaultValidator()
{
    var services = new ServiceCollection();

    services.AddLakonaGameRuntimeValidation();

    using var provider = services.BuildServiceProvider();
    var validator = provider.GetRequiredService<LakonaGameRuntimeValidator>();

    Assert.NotNull(validator);
}
```

Add required usings:

```csharp
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter AddLakonaGameRuntimeValidation --no-restore
```

Expected: FAIL because `AddLakonaGameRuntimeValidation` does not exist.

- [ ] **Step 3: Add package reference for DI test helpers**

Add this package to `Tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj` so `ServiceCollection` and `BuildServiceProvider()` are directly available to the tests:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
```

- [ ] **Step 4: Add DI extension**

Create `src/Lakona.Game.Server/Guardrails/LakonaGameGuardrailServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Guardrails.Rules;

namespace Lakona.Game.Server.Guardrails;

public static class LakonaGameGuardrailServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameRuntimeValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, NodeIdentityRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, EndpointRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, HotfixSourceRule>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaGameValidationRule, ClusterServiceGraphRule>());
        services.TryAddSingleton<LakonaGameRuntimeValidator>();

        return services;
    }
}
```

- [ ] **Step 5: Run the test and verify it passes**

Run:

```powershell
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --filter AddLakonaGameRuntimeValidation --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit DI registration**

Run:

```powershell
git add src/Lakona.Game.Server/Guardrails Tests/Lakona.Game.Server.Tests/Guardrails Tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj
git commit -m "feat: register runtime guardrail validation"
```

## Task 5: Update Generated Check Command To Use Validation Result

**Files:**
- Modify: `src/Lakona.Tool/Scaffolding/ToolTemplates.cs`
- Test: `tests/Lakona.Tool.Tests/ToolTemplateTests.cs`

- [ ] **Step 1: Add failing template tests for framework guardrail usage and JSON output**

Update `RenderClusterOptions_IncludesLakonaGameCheckOutputLabels` in `tests/Lakona.Tool.Tests/ToolTemplateTests.cs` to include:

```csharp
Assert.Contains("using Lakona.Game.Server.Guardrails;", source);
Assert.Contains("LakonaGameValidationResult", source);
Assert.Contains("--json", source);
Assert.Contains("JsonSerializer.Serialize", source);
Assert.Contains("\"succeeded\"", source);
Assert.Contains("ULINK071", source);
```

- [ ] **Step 2: Run the template test and verify it fails**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter RenderClusterOptions_IncludesLakonaGameCheckOutputLabels --no-restore
```

Expected: FAIL because the generated check command does not yet reference the framework guardrail result or JSON output.

- [ ] **Step 3: Update generated cluster options usings**

In `ToolTemplates.RenderClusterOptions()`, add generated usings:

```csharp
using System.Text.Json;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
```

- [ ] **Step 4: Update generated check command helper**

Replace generated `LakonaGameCheck.Run(...)` with a version that builds a resolved runtime model, invokes the framework rules, and formats text or JSON:

```csharp
internal static class LakonaGameCheck
{
    public static int Run(LakonaGameRuntimeOptions runtime, ClusterOptions clusterOptions, string[] args)
    {
        var resolved = ToResolvedRuntime(runtime, clusterOptions);
        var validator = new LakonaGameRuntimeValidator(
            [
                new NodeIdentityRule(),
                new EndpointRule(),
                new HotfixSourceRule(),
                new ClusterServiceGraphRule()
            ]);
        var result = validator.Validate(resolved);

        if (args.Contains("--json", StringComparer.Ordinal))
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    succeeded = result.Succeeded,
                    diagnostics = result.Diagnostics.Select(diagnostic => new
                    {
                        code = diagnostic.Code,
                        severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                        message = diagnostic.Message,
                        repair = diagnostic.Repair
                    })
                },
                new JsonSerializerOptions { WriteIndented = true }));
            return result.Succeeded ? 0 : 1;
        }

        return WriteText(runtime, clusterOptions, result);
    }

    private static int WriteText(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions,
        LakonaGameValidationResult result)
    {
        var serviceNames = clusterOptions.Services.Select(service => service.Name); // Historical/superseded: current startup uses Feature Catalog, not service-shaped framework config.
        var rpcEndpoint = clusterOptions.AdvertisedEndpoints.TryGetValue("client", out var clientEndpoint)
            ? clientEndpoint
            : runtime.Endpoints[0].ToAdvertisedEndpoint();

        Console.WriteLine("cluster: ok single-node");
        Console.WriteLine($"node: ok {clusterOptions.NodeId}");
        Console.WriteLine($"services: ok {string.Join(", ", serviceNames)}");

        var hotfixFailure = result.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Code == "ULINK071");
        if (hotfixFailure is not null)
        {
            Console.Error.WriteLine("hotfix: failed local build output not found");
            Console.Error.WriteLine($"fix: {hotfixFailure.Repair}");
            return 1;
        }

        Console.WriteLine("hotfix: ok local-build Server.Hotfix.dll");
        Console.WriteLine("reliable-push: ok pending limit 256, replay window 120s");
        Console.WriteLine($"rpc: ok {rpcEndpoint}");

        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == LakonaGameDiagnosticSeverity.Error))
        {
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.Repair))
            {
                Console.Error.WriteLine($"fix: {diagnostic.Repair}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static LakonaGameResolvedRuntime ToResolvedRuntime(
        LakonaGameRuntimeOptions runtime,
        ClusterOptions clusterOptions)
    {
        var hotfixPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "../../../../Hotfix/bin/Debug/net10.0",
                "Server.Hotfix.dll"));

        return new LakonaGameResolvedRuntime(
            NodeId: new LakonaGameResolvedValue<string>(clusterOptions.NodeId, LakonaGameValueSource.Configuration, "Lakona.Game:Node:Id"),
            Endpoints:
            [
                new LakonaGameResolvedEndpoint(
                    Transport: new LakonaGameResolvedValue<string>(runtime.Endpoints[0].Transport, LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Transport"),
                    Host: new LakonaGameResolvedValue<string>(runtime.Endpoints[0].Host, LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Host"),
                    Port: new LakonaGameResolvedValue<int>(runtime.Endpoints[0].Port, LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Port"),
                    Path: new LakonaGameResolvedValue<string>(runtime.Endpoints[0].Path, LakonaGameValueSource.Configuration, "Lakona.Game:Endpoints:0:Path"),
                    AdvertisedEndpoint: new LakonaGameResolvedValue<string>(runtime.Endpoints[0].ToAdvertisedEndpoint(), LakonaGameValueSource.GeneratedConvention))
            ],
            Cluster: new LakonaGameResolvedCluster(
                Services: clusterOptions.Services // Historical/superseded: current startup uses Feature Catalog, not service-shaped framework config.
                    .Select(service => new LakonaGameResolvedClusterService(service.Kind, service.Name))
                    .ToArray(),
                AdvertisedEndpoints: clusterOptions.AdvertisedEndpoints),
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>(hotfixPath, LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Profile: LakonaGameRuntimeProfile.Development);
    }
}
```

- [ ] **Step 5: Update generated Program invocation**

In `RenderLakonaGameCheckExit`, change the generated invocation to:

```csharp
return LakonaGameCheck.Run(runtimeOptions, runtimeOptions.ToClusterOptions(builder.Configuration), args);
```

- [ ] **Step 6: Run template tests and fix string expectations**

Run:

```powershell
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --filter ToolTemplateTests --no-restore
```

Expected: PASS after updating existing assertions from the old `LakonaGameCheck.Run(runtimeOptions, ...)` signature to include `args`.

- [ ] **Step 7: Commit generated check integration**

Run:

```powershell
git add src/Lakona.Tool/Scaffolding/ToolTemplates.cs tests/Lakona.Tool.Tests/ToolTemplateTests.cs
git commit -m "feat: use runtime guardrails in generated check"
```

## Task 6: Verify Generated Project End To End

**Files:**
- Modify only if verification exposes template issues.

- [ ] **Step 1: Build tool and server tests**

Run:

```powershell
dotnet build src\Lakona.Game.Server\Lakona.Game.Server.csproj --no-restore
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
dotnet build src\Lakona.Tool\Lakona.Tool.csproj --no-restore
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: all commands pass.

- [ ] **Step 2: Generate a fresh verification project**

Run:

```powershell
dotnet run --project src\Lakona.Tool\Lakona.Tool.csproj -- new --name VerifyGuardrails --output VerifyOut
```

Expected: `VerifyOut\VerifyGuardrails` is created.

- [ ] **Step 3: Run human-readable check**

Run:

```powershell
dotnet run --project VerifyOut\VerifyGuardrails\Server\Server\Server.csproj -- --lakona-game-check
```

Expected output includes:

```txt
cluster: ok single-node
node: ok dev-1
services: ok node-directory, route-directory, gateway
hotfix: ok local-build Server.Hotfix.dll
reliable-push: ok pending limit 256, replay window 120s
rpc: ok kcp://127.0.0.1:20000
```

- [ ] **Step 4: Run JSON check**

Run:

```powershell
dotnet run --project VerifyOut\VerifyGuardrails\Server\Server\Server.csproj -- --lakona-game-check --json
```

Expected output includes:

```json
{
  "succeeded": true,
  "diagnostics": []
}
```

- [ ] **Step 5: Verify missing Hotfix remains an error**

Move the generated hotfix DLL aside, run the already-built server DLL, and restore the DLL:

```powershell
$dll = Resolve-Path 'VerifyOut\VerifyGuardrails\Server\Hotfix\bin\Debug\net10.0\Server.Hotfix.dll'
$bak = "$dll.bak"
Move-Item -LiteralPath $dll.Path -Destination $bak
try {
    dotnet "VerifyOut\VerifyGuardrails\Server\Server\bin\Debug\net10.0\Server.dll" --lakona-game-check
    $code = $LASTEXITCODE
} finally {
    Move-Item -LiteralPath $bak -Destination $dll.Path
}
exit $code
```

Expected: exit code `1`, output includes:

```txt
hotfix: failed local build output not found
fix: dotnet build Server/Hotfix/Server.Hotfix.csproj
```

- [ ] **Step 6: Clean verification output**

Run:

```powershell
$target = Resolve-Path 'VerifyOut'
if ($target.Path -eq (Join-Path (Resolve-Path '.').Path 'VerifyOut')) {
    Remove-Item -LiteralPath $target.Path -Recurse -Force
} else {
    throw "Refusing to remove unexpected path $($target.Path)"
}
```

Expected: `VerifyOut` is removed.

- [ ] **Step 7: Commit any fixes from verification**

If verification required code or template changes, commit them:

```powershell
git add src Tests CHANGELOG.md
git commit -m "fix: make runtime guardrails pass generated checks"
```

If no fixes were required, do not create an empty commit.

## Task 7: Version And Documentation Updates

**Files:**
- Modify: `src/Lakona.Game.Server/Lakona.Game.Server.csproj`
- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`
- Modify: `CHANGELOG.md`
- Modify: `src/Lakona.Game.Server/README.md`
- Modify: `src/Lakona.Tool/README.md`

- [ ] **Step 1: Bump package versions**

Update `src/Lakona.Game.Server/Lakona.Game.Server.csproj`:

```xml
<Version>0.1.10</Version>
```

Update `src/Lakona.Tool/Lakona.Tool.csproj` to the next patch version from its current value. Preserve the existing major/minor version.

- [ ] **Step 2: Update changelog**

Add entries to `CHANGELOG.md`:

```markdown
## Unreleased

- Lakona.Game.Server: Added runtime guardrail diagnostics, resolved runtime model, and initial validation rules for node id, endpoints, hotfix presence, and duplicate cluster services.
- Lakona.Tool: Updated generated `--lakona-game-check` to reuse runtime guardrail diagnostics and support `--json` output.
```

If `CHANGELOG.md` already has an `Unreleased` section, append these bullets there instead of creating a duplicate section.

- [ ] **Step 3: Update package READMEs**

In `src/Lakona.Game.Server/README.md`, add a short section:

```markdown
## Runtime Guardrails

Lakona.Game.Server provides runtime guardrail diagnostics for framework invariants such as node identity, endpoint shape, hotfix assembly presence, and cluster service graph consistency. Generated projects use these diagnostics through `--lakona-game-check`; server hosts can also register the default rules with `AddLakonaGameRuntimeValidation()`.
```

In `src/Lakona.Tool/README.md`, mention:

```markdown
The generated `--lakona-game-check --json` output is suitable for CI and deployment scripts that need machine-readable validation results.
```

- [ ] **Step 4: Run relevant verification**

Run:

```powershell
dotnet build src\Lakona.Game.Server\Lakona.Game.Server.csproj --no-restore
dotnet test Tests\Lakona.Game.Server.Tests\Lakona.Game.Server.Tests.csproj --no-restore
dotnet build src\Lakona.Tool\Lakona.Tool.csproj --no-restore
dotnet test tests\Lakona.Tool.Tests\Lakona.Tool.Tests.csproj --no-restore
```

Expected: all commands pass.

- [ ] **Step 5: Commit version and docs**

Run:

```powershell
git add src\Lakona.Game.Server\Lakona.Game.Server.csproj src\Lakona.Tool\Lakona.Tool.csproj CHANGELOG.md src\Lakona.Game.Server\README.md src\Lakona.Tool\README.md
git commit -m "docs: document runtime guardrails release"
```

## Self-Review

Spec coverage:

- Diagnostic result types are covered by Task 1.
- Resolved runtime model with provenance is covered by Task 2.
- Small deterministic rules are covered by Task 3.
- DI registration is covered by Task 4.
- Generated check command reuse and `--json` output are covered by Task 5.
- End-to-end generated project verification is covered by Task 6.
- Version and package docs are covered by Task 7.

Deferred work:

- Production-readiness profile rules beyond the first local checks.
- Durable Reliable Push policy.
- Split-node route-directory and node-directory dependency validation.
- Moving all current generated runtime option derivation into framework defaults.

Placeholder scan:

- This plan has no TBD or placeholder steps.
- Every code-writing step includes concrete code or concrete assertions.

Type consistency:

- `LakonaGameResolvedRuntime` is introduced before rules consume it.
- `ILakonaGameValidationRule` is introduced before DI registration.
- `LakonaGameRuntimeValidator` is used consistently by tests and generated check code.
