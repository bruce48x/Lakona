# Unity RPC Generation Zero-Config Implementation Plan

> **For implementers:** Execute this plan task-by-task with one continuous owner for the coupled runtime/generator/template changes. Helper agents may review plans, review docs, or run independent scans, but they must not split ownership of the core code path.

**Goal:** Remove generated Unity RPC marker files and make Lakona game client generation zero-config while simplifying game handshake metadata to the fields the framework actually uses.

**Architecture:** One continuity-preserving owner should implement the runtime DTO/codec contract, generated client wrapper shape, analyzer option model, tool-generated project output, sample migrations, docs, and package version bumps as one coordinated change. The design deliberately removes unused framework metadata instead of moving it to hidden defaults or project-local configuration.

**Tech Stack:** C#/.NET 10 and netstandard2.1 runtime packages, Roslyn source generator in `Lakona.Rpc.Analyzers`, xUnit tests, Lakona.Tool project renderers, Unity/Godot/console generated client templates.

---

## Large-Change Scope Checkpoint

- **Classification:** Large cross-cutting change.
- **Goal:** New generated Unity/Tuanjie clients compile generated `Client.Generated` RPC and `LakonaGameClient` APIs without `Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs`, and Lakona game handshake no longer carries unused platform/runtime/game-version metadata.
- **Affected surfaces:** `Lakona.Game.Abstractions`, `Lakona.Game.Client`, `Lakona.Game.Server`, `Lakona.Rpc.Core`, `Lakona.Rpc.Analyzers`, `Lakona.Tool`, generated Unity/Godot/console project templates, samples, analyzer/tool/runtime tests, source-generation docs.
- **Coupling assessment:** Runtime handshake DTO/codec, generated wrapper, analyzer options, and template output are strongly coupled and must stay under one implementation owner. Do not split those code changes across parallel implementers.
- **Independent slices:** A reviewer agent can review this plan; after implementation stabilizes, helper agents may run source scans or review docs. Sample migration should happen after generator/tool shape compiles.
- **Compatibility stance:** Breaking compatibility is acceptable under `CONTRIBUTING.md` because the repo is early-stage and this removes unnecessary framework surface. Keep the old `LakonaGameGenerateClientAttribute(string, string, string)` constructor only as an obsolete no-op compatibility shim; generated projects must not use marker attributes.
- **Versioning impact:** Bump package versions for modified shippable packages under `src/**`: expected packages are `Lakona.Game.Abstractions`, `Lakona.Game.Client`, `Lakona.Game.Server`, `Lakona.Rpc.Core`, `Lakona.Rpc.Analyzers`, and `Lakona.Tool`. If implementation touches additional shippable packages, bump those too. Version bumps must happen before refreshing committed sample package copies.
- **Validation plan:** Run focused test projects first, then package version graph guard. Exact commands are listed in Task 8.

## File Structure And Ownership

- `src/Lakona.Game.Abstractions/Sessions/GameHandshake.cs`: owns game handshake DTO shape.
- `src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs`: owns binary encoding/decoding for framework internal payloads.
- `tests/Lakona.Game.Abstractions.Tests/Internal/LakonaInternalCodecTests.cs`: owns codec contract tests.
- `src/Lakona.Game.Client/LakonaGameClientOptions.cs`: owns generated game client runtime options exposed to app code.
- `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`: owns source-generation options, Unity auto-detection, generated namespace default, and generated `LakonaGameClient` source.
- `tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs`: owns generated source contract tests.
- `src/Lakona.Rpc.Core/Contracts/RpcAttributes.cs`: owns assembly marker attributes.
- `src/Lakona.Rpc.Core/buildTransitive/Lakona.Rpc.Core.props` and `src/Lakona.Rpc.Analyzers/buildTransitive/Lakona.Rpc.Analyzers.props`: own compiler-visible generation properties.
- `src/Lakona.Tool/Rendering/Client/UnityClientRenderer.cs`: owns generated Unity/Tuanjie client file list.
- `src/Lakona.Tool/Rendering/Client/UnityClientCodeTemplates.cs`: owns generated Unity client code text.
- `src/Lakona.Tool/Rendering/Client/GodotClientRenderer.cs`: owns generated Godot client project metadata.
- `src/Lakona.Tool/Rendering/Client/ConsoleClientRenderer.cs`: owns generated console client project metadata.
- `tests/Lakona.Tool.Tests/Rendering/ClientRendererTests.cs`: owns generated client template contract tests.
- `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`: owns repository/sample generated-output scans.
- `samples/Game.Unity.Agar/**`, `samples/Game.Godot.Chat/**`, `samples/Rpc.Unity.MemoryPack.Tcp/**`, `samples/Rpc.Unity.MemoryPack.Kcp/**`, `samples/Rpc.Unity.Json.Websocket/**`, and `samples/Rpc.Godot.MixedTransport/**`: migrate committed sample source, project metadata, and package copies that contain docs/props for changed packages.
- `docs/source-generation.md`, `docs/rpc.md`, `docs/tool/generation-architecture.md`, `docs/tool/default-experience.md`, `src/Lakona.Rpc.Analyzers/README.md`, `src/Lakona.Game.Client/README.md`, and `src/Lakona.Rpc.Client/README.md`: update user-facing and maintainer docs.
- `src/**/**/*.csproj` package versions and generated package-version consumers: bump versions per `CONTRIBUTING.md`.

## Task 1: Simplify Game Handshake DTO And Codec

**Files:**
- Modify: `src/Lakona.Game.Abstractions/Sessions/GameHandshake.cs`
- Modify: `src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs`
- Modify: `tests/Lakona.Game.Abstractions.Tests/Internal/LakonaInternalCodecTests.cs`

- [ ] **Step 1: Write failing codec tests for the new `GameClientHello` shape**

Replace `GameClientHello_roundtrips_with_all_fields` in `tests/Lakona.Game.Abstractions.Tests/Internal/LakonaInternalCodecTests.cs` with:

```csharp
[Fact]
public void GameClientHello_roundtrips_protocol_version_only()
{
    var hello = new GameClientHello { ProtocolVersion = 1 };

    var payload = LakonaInternalCodec.EncodeGameClientHello(hello);
    var decoded = LakonaInternalCodec.DecodeGameClientHello(payload);

    Assert.Equal(hello.ProtocolVersion, decoded.ProtocolVersion);
    Assert.Equal(10, payload.Length);
}
```

Replace `Decode_rejects_oversized_string_list_count` with:

```csharp
[Fact]
public void Decode_rejects_invalid_game_client_hello_protocol_version()
{
    var payload = CreatePayload(GameClientHelloKind, builder => WriteInt32BigEndian(builder, 0));

    Assert.Throws<InvalidOperationException>(() => LakonaInternalCodec.DecodeGameClientHello(payload));
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj --no-restore --filter LakonaInternalCodecTests
```

Expected before implementation: compile failure because `GameClientHello.ProtocolVersion` does not exist.

- [ ] **Step 3: Replace `GameClientHello` fields with one protocol version**

In `src/Lakona.Game.Abstractions/Sessions/GameHandshake.cs`, replace the `GameClientHello` class with:

```csharp
public sealed class GameClientHello
{
    public int ProtocolVersion { get; set; } = 1;
}
```

Keep `GameServerHello` and `ReliablePushHandshakeSettings` unchanged in this task.

- [ ] **Step 4: Update internal codec encode/decode**

In `src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs`, replace the body of `EncodeGameClientHello` after the null check with:

```csharp
ValidatePositiveProtocolVersion(value.ProtocolVersion);

var writer = CreateWriter(GameClientHelloKind);
writer.WriteInt32(value.ProtocolVersion);
return writer.ToArray();
```

Replace the `DecodeGameClientHello` object creation with:

```csharp
var value = new GameClientHello
{
    ProtocolVersion = reader.ReadInt32(),
};

ValidatePositiveProtocolVersion(value.ProtocolVersion);
reader.EnsureEnd();
return value;
```

Remove the now-unused `ValidateProtocolRange` method if no other code calls it.

- [ ] **Step 5: Run the focused abstraction tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj --no-restore --filter LakonaInternalCodecTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/Lakona.Game.Abstractions/Sessions/GameHandshake.cs src/Lakona.Game.Abstractions/Internal/LakonaInternalCodec.cs tests/Lakona.Game.Abstractions.Tests/Internal/LakonaInternalCodecTests.cs
git diff --staged
git commit -m "Simplify game client handshake payload"
```

## Task 2: Update Server Handshake Validation

**Files:**
- Modify: `src/Lakona.Game.Server/Sessions/GameHandshakeService.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameHandshakeTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/GameHandshakeGateTests.cs`
- Modify: `tests/Lakona.Game.Server.Tests/ReliablePushAckRpcTests.cs`

- [ ] **Step 1: Update tests to construct single-version hellos**

Replace all `new GameClientHello { ProtocolVersionMin = 1, ProtocolVersionMax = 1 }` and variants in server tests with:

```csharp
new GameClientHello { ProtocolVersion = 1 }
```

Add this focused rejection test to `tests/Lakona.Game.Server.Tests/GameHandshakeTests.cs`:

```csharp
[Fact]
public async Task Handshake_rejects_unsupported_protocol_version()
{
    var service = new GameHandshakeService(
        new LakonaGameRuntimeOptions
        {
            Node = { Id = "node-a" }
        },
        new ReliablePushOptions());

    var exception = await Assert.ThrowsAsync<GameHandshakeRejectedException>(async () =>
        await service.HandshakeAsync(
            new GameClientHello { ProtocolVersion = 2 },
            "kcp",
            "memorypack"));

    Assert.Contains("protocol version 1", exception.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run server handshake tests and verify failure**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "GameHandshake"
```

Expected before implementation: compile failure from removed `ProtocolVersionMin` / `ProtocolVersionMax` references or failing rejection behavior for protocol version `2`.

- [ ] **Step 3: Update validation logic**

In `src/Lakona.Game.Server/Sessions/GameHandshakeService.cs`, replace:

```csharp
if (hello.ProtocolVersionMin > 1 || hello.ProtocolVersionMax < 1)
{
    throw new GameHandshakeRejectedException(
        "Client does not support Lakona game handshake protocol version 1.");
}
```

with:

```csharp
if (hello.ProtocolVersion != 1)
{
    throw new GameHandshakeRejectedException(
        "Client does not support Lakona game handshake protocol version 1.");
}
```

- [ ] **Step 4: Run server tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore --filter "GameHandshake|ReliablePushAckRpc"
```

Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```powershell
git add src/Lakona.Game.Server/Sessions/GameHandshakeService.cs tests/Lakona.Game.Server.Tests/GameHandshakeTests.cs tests/Lakona.Game.Server.Tests/GameHandshakeGateTests.cs tests/Lakona.Game.Server.Tests/ReliablePushAckRpcTests.cs
git diff --staged
git commit -m "Use single game handshake protocol version"
```

## Task 3: Remove Generated Game Client Metadata Options

**Files:**
- Modify: `src/Lakona.Game.Client/LakonaGameClientOptions.cs`
- Modify: `src/Lakona.Rpc.Core/Contracts/RpcAttributes.cs`
- Modify: `src/Lakona.Rpc.Core/buildTransitive/Lakona.Rpc.Core.props`
- Modify: `src/Lakona.Rpc.Analyzers/buildTransitive/Lakona.Rpc.Analyzers.props`
- Modify: `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`
- Modify: `tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs`

- [ ] **Step 1: Update analyzer tests for metadata-free wrapper**

In `SourceGenerator_GameClientWrapper_ForwardsApiAndAutoBindsCallbacks`, change the input properties to:

```csharp
new Dictionary<string, string>
{
    ["build_property.LakonaGameGenerateClient"] = "true",
    ["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated"
}
```

Update assertions:

```csharp
Assert.Contains("public global::Client.Generated.RpcApi Api", wrapper);
Assert.Contains("new global::Client.Generated.RpcClient(_options.RpcOptions)", wrapper);
Assert.Contains("ProtocolVersion = 1", wrapper);
Assert.DoesNotContain("ClientRuntime", wrapper);
Assert.DoesNotContain("Platform", wrapper);
Assert.DoesNotContain("GameVersion", wrapper);
```

Remove `SourceGenerator_GameClientWrapper_GameVersionFallback_UsesAssemblyNameOrGame`.

Replace `SourceGenerator_GameClientWrapper_UnityMarker_EnablesWrapperAndMetadata` with:

```csharp
[Fact]
public void SourceGenerator_GameClientWrapper_UnityMarker_EnablesWrapperWithoutMetadata()
{
    var source = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        [assembly: LakonaRpcGenerateClient("Client.Generated")]
        [assembly: LakonaGameGenerateClient]

        namespace Game.Contracts
        {
            public sealed class PingRequest { }
            public sealed class PingReply { }

            [RpcService(1)]
            public interface IPingService
            {
                [RpcMethod(1)]
                ValueTask<PingReply> PingAsync(PingRequest request);
            }
        }
        """;

    var runResult = AnalyzerTestHelpers.RunGenerator(
        AnalyzerTestHelpers.CreateCompilation(source, assemblyName: "Assembly-CSharp"),
        null,
        out var outputCompilation);

    Assert.Empty(runResult.Diagnostics);
    Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

    var wrapper = GetGeneratedSource(runResult, "LakonaGameClient.g.cs");
    Assert.Contains("ProtocolVersion = 1", wrapper);
    Assert.DoesNotContain("ClientRuntime", wrapper);
    Assert.DoesNotContain("Platform", wrapper);
    Assert.DoesNotContain("GameVersion", wrapper);
}
```

Replace `SourceGenerator_GameClientWrapper_PropertyPrecedence_OverridesMarker` with a test that only asserts explicit `LakonaGameGenerateClient=false` disables the wrapper:

```csharp
[Fact]
public void SourceGenerator_GameClientWrapper_PropertyPrecedence_CanDisableMarker()
{
    var source = """
        using System.Threading.Tasks;
        using Lakona.Rpc.Core;

        [assembly: LakonaRpcGenerateClient("Client.Generated")]
        [assembly: LakonaGameGenerateClient]

        namespace Game.Contracts
        {
            public sealed class PingRequest { }
            public sealed class PingReply { }

            [RpcService(1)]
            public interface IPingService
            {
                [RpcMethod(1)]
                ValueTask<PingReply> PingAsync(PingRequest request);
            }
        }
        """;

    var disabledRun = AnalyzerTestHelpers.RunGenerator(
        AnalyzerTestHelpers.CreateCompilation(source, assemblyName: "Assembly-CSharp"),
        new Dictionary<string, string>
        {
            ["build_property.LakonaGameGenerateClient"] = "false"
        },
        out _);

Assert.DoesNotContain(
    disabledRun.Results.Single().GeneratedSources,
    static source => source.HintName == "LakonaGameClient.g.cs");
}
```

Update `SourceGenerator_GameClientWrapper_CompilesWithoutNotificationContracts` so it sets:

```csharp
["build_property.LakonaRpcGeneratedNamespace"] = "Client.Generated",
["build_property.LakonaGameGenerateClient"] = "true"
```

and replace its wrapper assertion with:

```csharp
Assert.Contains("new global::Client.Generated.RpcClient(_options.RpcOptions)", wrapper);
Assert.Contains("ProtocolVersion = 1", wrapper);
Assert.DoesNotContain("ClientRuntime", wrapper);
Assert.DoesNotContain("Platform", wrapper);
Assert.DoesNotContain("GameVersion", wrapper);
```

- [ ] **Step 2: Run analyzer tests and verify failure**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-restore --filter LakonaRpcSourceGeneratorTests
```

Expected before implementation: compile failure or assertion failure because generated wrapper still references metadata options.

- [ ] **Step 3: Remove options from client runtime API**

In `src/Lakona.Game.Client/LakonaGameClientOptions.cs`, remove these properties:

```csharp
public string? ClientRuntime { get; set; }
public string? Platform { get; set; }
public string? GameVersion { get; set; }
```

Keep heartbeat options unchanged.

- [ ] **Step 4: Simplify marker attribute without breaking old source immediately**

In `src/Lakona.Rpc.Core/Contracts/RpcAttributes.cs`, keep the old constructor as a compatibility no-op but mark it obsolete:

```csharp
public LakonaGameGenerateClientAttribute()
{
}

[Obsolete("Lakona game client generation no longer uses runtime, platform, or game-version metadata. Use LakonaGameGenerateClientAttribute() instead.")]
public LakonaGameGenerateClientAttribute(string clientRuntime, string platform, string gameVersion)
{
    _ = clientRuntime;
    _ = platform;
    _ = gameVersion;
}
```

Remove `ClientRuntime`, `Platform`, and `GameVersion` properties from the attribute.

- [ ] **Step 5: Remove compiler-visible metadata properties**

In both `src/Lakona.Rpc.Core/buildTransitive/Lakona.Rpc.Core.props` and `src/Lakona.Rpc.Analyzers/buildTransitive/Lakona.Rpc.Analyzers.props`, remove:

```xml
<CompilerVisibleProperty Include="LakonaGameClientRuntime" />
<CompilerVisibleProperty Include="LakonaGameClientPlatform" />
<CompilerVisibleProperty Include="LakonaGameClientGameVersion" />
```

Keep:

```xml
<CompilerVisibleProperty Include="LakonaGameGenerateClient" />
```

- [ ] **Step 6: Simplify generator options and wrapper emission**

In `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`:

Remove constants:

```csharp
private const string GameClientRuntimeKey = "build_property.LakonaGameClientRuntime";
private const string GameClientPlatformKey = "build_property.LakonaGameClientPlatform";
private const string GameClientGameVersionKey = "build_property.LakonaGameClientGameVersion";
```

Remove `GameClientRuntime`, `GameClientPlatform`, and `GameClientGameVersion` from `GeneratorOptions`.

Change wrapper generation call to:

```csharp
ClientSourceEmitter.GenerateGameClientWrapper(
    services,
    generatedNamespace)
```

Change `GenerateGameClientWrapper` signature to:

```csharp
public static string GenerateGameClientWrapper(
    List<RpcServiceModel> services,
    string generatedNamespace)
```

Change generated `CreateClientHello` block to:

```csharp
private GameClientHello CreateClientHello()
{
    return new GameClientHello
    {
        ProtocolVersion = 1
    };
}
```

Remove generated `ResolveOption` helper if no generated code uses it.

Change `TryGetGameClientGenerationAttribute` to return only `bool`. It should only detect `LakonaGameGenerateClientAttribute`; it should not read constructor arguments.

- [ ] **Step 7: Run analyzer tests**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-restore --filter LakonaRpcSourceGeneratorTests
```

Expected: PASS.

- [ ] **Step 8: Commit Task 3**

```powershell
git add src/Lakona.Game.Client/LakonaGameClientOptions.cs src/Lakona.Rpc.Core/Contracts/RpcAttributes.cs src/Lakona.Rpc.Core/buildTransitive/Lakona.Rpc.Core.props src/Lakona.Rpc.Analyzers/buildTransitive/Lakona.Rpc.Analyzers.props src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs
git diff --staged
git commit -m "Remove game client generation metadata"
```

## Task 4: Add Zero-Config Unity Auto-Generation And New Default Namespace

**Files:**
- Modify: `src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs`
- Modify: `tests/Lakona.Rpc.Analyzers.Tests/AnalyzerTestHelpers.cs`
- Modify: `tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs`

- [ ] **Step 1: Add a Unity reference helper for analyzer tests**

In `tests/Lakona.Rpc.Analyzers.Tests/AnalyzerTestHelpers.cs`, add:

```csharp
public static MetadataReference CreateUnityEngineReference()
{
    var compilation = CSharpCompilation.Create(
        "UnityEngine.CoreModule",
        new[]
        {
            CSharpSyntaxTree.ParseText(
                "namespace UnityEngine { public class Object { } }",
                ParseOptions)
        },
        TrustedPlatformReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    return EmitReference(compilation);
}
```

Use this helper in tests that need to prove a compilation is Unity-compatible without relying only on the assembly name.

- [ ] **Step 2: Replace Unity marker-required test with zero-config tests**

Replace `SourceGenerator_UnityAssemblyRequiresClientGenerationMarker` with:

```csharp
[Fact]
public void SourceGenerator_UnityAssemblyCSharp_AutoGeneratesClientAndGameWrapper()
{
    var compilation = AnalyzerTestHelpers.CreateCompilation(
        SimpleClientContractSource,
        assemblyName: "Assembly-CSharp",
        includeServerRuntimeReference: false);

    var runResult = AnalyzerTestHelpers.RunGenerator(compilation, null, out var outputCompilation);

    Assert.Empty(runResult.Diagnostics);
    Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

    var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
    Assert.Contains("RpcApi.g.cs", generatedHintNames);
    Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
    Assert.Contains("LakonaGameClient.g.cs", generatedHintNames);

    var generatedSource = string.Join(
        "\n",
        runResult.Results.Single().GeneratedSources.Select(static source => source.SourceText.ToString()));
    Assert.Contains("namespace Client.Generated", generatedSource);
}
```

Add:

```csharp
[Fact]
public void SourceGenerator_UnityCustomAssembly_DoesNotAutoGenerateWithoutExplicitMarker()
{
    var compilation = AnalyzerTestHelpers.CreateCompilation(
        SimpleClientContractSource,
        assemblyName: "Game.Client",
        additionalReferences: new[] { AnalyzerTestHelpers.CreateUnityEngineReference() },
        includeServerRuntimeReference: false);

    var runResult = AnalyzerTestHelpers.RunGenerator(compilation, null, out _);

    Assert.Empty(runResult.Results.Single().GeneratedSources);
}
```

Add:

```csharp
[Fact]
public void SourceGenerator_UnityAssemblyCSharp_ExplicitFalseDisablesGameWrapper()
{
    var compilation = AnalyzerTestHelpers.CreateCompilation(
        SimpleClientContractSource,
        assemblyName: "Assembly-CSharp",
        includeServerRuntimeReference: false);

    var runResult = AnalyzerTestHelpers.RunGenerator(
        compilation,
        new Dictionary<string, string>
        {
            ["build_property.LakonaGameGenerateClient"] = "false"
        },
        out var outputCompilation);

    Assert.Empty(runResult.Diagnostics);
    Assert.Empty(AnalyzerTestHelpers.ErrorDiagnostics(outputCompilation));

    var generatedHintNames = runResult.Results.Single().GeneratedSources.Select(static source => source.HintName).ToArray();
    Assert.Contains("RpcApi.g.cs", generatedHintNames);
    Assert.Contains("PingServiceClient.g.cs", generatedHintNames);
    Assert.DoesNotContain("LakonaGameClient.g.cs", generatedHintNames);
}
```

- [ ] **Step 3: Run analyzer tests and verify failure**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-restore --filter "UnityAssemblyCSharp|UnityCustomAssembly|UnityAssemblyRequires"
```

Expected before implementation: zero-config `Assembly-CSharp` produces no generated client because Unity auto-detection currently returns early.

- [ ] **Step 4: Change default client namespace**

In `GeneratorOptions.From`, change the fallback for `ClientNamespaceKey` from:

```csharp
var clientNamespace = GetString(global, ClientNamespaceKey, "Rpc.Generated");
```

to:

```csharp
var clientNamespace = GetString(global, ClientNamespaceKey, "Client.Generated");
```

- [ ] **Step 5: Implement Unity auto-detection**

Replace the Unity early return in `WithAutoDetectedModes` with explicit `Assembly-CSharp` handling:

```csharp
var hasClientRuntime = compilation.GetTypeByMetadataName("Lakona.Rpc.Client.RpcClientRuntime") is not null;
var hasServerRuntime = compilation.GetTypeByMetadataName("Lakona.Rpc.Server.RpcServiceRegistry") is not null;
var hasGameClientRuntime = compilation.GetTypeByMetadataName("Lakona.Game.Client.LakonaGameClientCore") is not null;
var isUnityCompilation = IsUnityCompilation(compilation);
var isGeneratedUnityClientAssembly = isUnityCompilation && IsUnityMainUserAssembly(compilation);
var autoGenerateUnityGameClient =
    isGeneratedUnityClientAssembly &&
    hasGameClientRuntime &&
    hasClientRuntime &&
    !hasServerRuntime &&
    !HasGameClientSetting &&
    !GenerateGameClientDisabled;

return new GeneratorOptions(
    generateClient: hasClientRuntime && !hasServerRuntime && (!isUnityCompilation || isGeneratedUnityClientAssembly),
    generateServer: hasServerRuntime && !hasClientRuntime,
    generateGameClient: GenerateGameClient || autoGenerateUnityGameClient,
    hasExplicitGenerationMode: false,
    ClientNamespace,
    ServerNamespace);
```

Add helper:

```csharp
private static bool IsUnityMainUserAssembly(Compilation compilation) =>
    string.Equals(compilation.AssemblyName, "Assembly-CSharp", StringComparison.Ordinal);
```

Keep `IsUnityCompilation` for identifying Unity custom assemblies and preventing accidental auto-generation outside `Assembly-CSharp`.

To support this, keep enough option state to distinguish absent `LakonaGameGenerateClient` from explicit `false`. Add `HasGameClientSetting` and `GenerateGameClientDisabled` to `GeneratorOptions`, set them in `GeneratorOptions.From`, and make the explicit `false` test assert that `Assembly-CSharp` still generates `RpcApi.g.cs` / service clients but not `LakonaGameClient.g.cs`.

- [ ] **Step 6: Run analyzer tests**

Run:

```powershell
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-restore --filter LakonaRpcSourceGeneratorTests
```

Expected: PASS.

- [ ] **Step 7: Commit Task 4**

```powershell
git add src/Lakona.Rpc.Analyzers/SourceGeneration/LakonaRpcSourceGenerator.cs tests/Lakona.Rpc.Analyzers.Tests/AnalyzerTestHelpers.cs tests/Lakona.Rpc.Analyzers.Tests/LakonaRpcSourceGeneratorTests.cs
git diff --staged
git commit -m "Enable zero-config Unity client generation"
```

## Task 5: Update Generated Project Templates

**Files:**
- Modify: `src/Lakona.Tool/Rendering/Client/UnityClientRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Client/UnityClientCodeTemplates.cs`
- Modify: `src/Lakona.Tool/Rendering/Client/GodotClientRenderer.cs`
- Modify: `src/Lakona.Tool/Rendering/Client/ConsoleClientRenderer.cs`
- Modify generated client code templates that contain `using Rpc.Generated;`
- Modify: `tests/Lakona.Tool.Tests/Rendering/ClientRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Update tool renderer tests**

In `UnityClientRenderer_EmitsPlayableChatClientSlice`, remove:

```csharp
var rpcMarker = AssertPath(plan, "Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs").Content;
Assert.Contains("[assembly: LakonaRpcGenerateClient(\"Rpc.Generated\")]", rpcMarker, StringComparison.Ordinal);
Assert.Contains("[assembly: LakonaGameGenerateClient(\"unity\", \"unity\", \"chat\")]", rpcMarker, StringComparison.Ordinal);
```

Add:

```csharp
Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs");
Assert.DoesNotContain(plan.Files, file => file.RelativePath == "Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs.meta");
```

Replace every generated-client assertion or template expectation of `Rpc.Generated` for generated starter clients with `Client.Generated`.

In `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`, update `UnityAgarSample_SeparatesNodeLocalActorRuntimeFromPlacementAwareSelectors` so it no longer expects:

```csharp
[assembly: LakonaGameGenerateClient("unity", "unity", "agar")]
```

and instead asserts the committed Agar Unity sample has no `LakonaRpcGeneration.cs` marker file and no removed generation metadata.

In Godot and Console project tests, replace expected property groups with:

```xml
<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
```

and assert absence of:

```xml
<LakonaGameClientRuntime>
<LakonaGameClientPlatform>
<LakonaGameClientGameVersion>
<CompilerVisibleProperty Include="LakonaGameClientRuntime" />
<CompilerVisibleProperty Include="LakonaGameClientPlatform" />
<CompilerVisibleProperty Include="LakonaGameClientGameVersion" />
```

- [ ] **Step 2: Run tool tests and verify failure**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore --filter "ClientRendererTests|ToolArchitectureScanTests"
```

Expected before implementation: failures for marker file presence and old namespace/metadata output.

- [ ] **Step 3: Remove Unity marker file generation**

In `UnityClientRenderer.AddClientCodeFiles`, delete these lines:

```csharp
builder.AddFile("Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs", UnityClientCodeTemplates.RenderRpcGeneration(), FileWriteMode.Replace, GeneratedFileKind.Text);
builder.AddFile("Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs.meta", UnityClientAssetTemplates.RenderMonoScriptMeta(UnityClientAssetTemplates.RpcGenerationGuid), FileWriteMode.Replace, GeneratedFileKind.Text);
```

In `UnityClientCodeTemplates`, delete `RenderRpcGeneration()`.

If `UnityClientAssetTemplates.RpcGenerationGuid` is unused after deletion, remove that constant too.

- [ ] **Step 4: Update generated client namespace usages**

Replace `using Rpc.Generated;` with:

```csharp
using Client.Generated;
```

in generated Unity, Godot, and Console client templates where they consume generated RPC APIs.

Update generated Godot and Console client project metadata to:

```xml
<LakonaRpcGenerateClient>true</LakonaRpcGenerateClient>
<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
```

Remove compiler-visible properties for removed game metadata. Keep compiler-visible entries for `LakonaRpcGenerateClient`, `LakonaRpcGeneratedNamespace`, and `LakonaGameGenerateClient`.

- [ ] **Step 5: Run tool tests**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore --filter "ClientRendererTests|ToolArchitectureScanTests"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 5**

```powershell
git add src/Lakona.Tool/Rendering/Client tests/Lakona.Tool.Tests/Rendering/ClientRendererTests.cs tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs
git diff --staged
git commit -m "Generate zero-config client projects"
```

## Task 6: Migrate Samples And Documentation

**Files:**
- Modify/remove sample files under `samples/Game.Unity.Agar/**`, `samples/Game.Godot.Chat/**`, and RPC Unity samples containing `LakonaRpcGeneration.cs`, `Rpc.Generated`, or removed metadata properties.
- Modify: `docs/source-generation.md`
- Modify: `docs/rpc.md`
- Modify: `docs/tool/generation-architecture.md`
- Modify: `docs/tool/default-experience.md`
- Modify: `src/Lakona.Rpc.Analyzers/README.md`
- Modify: `src/Lakona.Game.Client/README.md`
- Modify: `src/Lakona.Rpc.Client/README.md`

- [ ] **Step 1: Run source scans before migration**

Run:

```powershell
rg -n "LakonaRpcGeneration|Rpc\.Generated|LakonaGameClientRuntime|LakonaGameClientPlatform|LakonaGameClientGameVersion|ClientRuntimeVersion|ClientRuntime\s*=|Platform\s*=|GameVersion\s*=|BuildId|SupportedCapabilities|ProtocolVersionMin|ProtocolVersionMax" samples docs src tests -S
```

Expected before migration: matches in sample marker files, sample client code, docs, and tests.

- [ ] **Step 2: Remove sample marker files**

Delete committed sample marker files and their `.meta` files when present:

```text
samples/Game.Unity.Agar/Client/Assets/Scripts/Rpc/LakonaRpcGeneration.cs
samples/Rpc.Unity.MemoryPack.Tcp/Client/Assets/Scripts/Rpc/Testing/LakonaRpcGeneration.cs
samples/Rpc.Unity.MemoryPack.Kcp/Client/Assets/Scripts/Rpc/Testing/LakonaRpcGeneration.cs
samples/Rpc.Unity.Json.Websocket/Client/Assets/Scripts/Rpc/Testing/LakonaRpcGeneration.cs
```

If a matching `.meta` exists for any deleted file, delete it in the same commit.

- [ ] **Step 3: Update sample client namespaces and projects**

In sample client code, replace:

```csharp
using Rpc.Generated;
```

with:

```csharp
using Client.Generated;
```

In Godot/Console sample project files, replace:

```xml
<LakonaRpcGeneratedNamespace>Rpc.Generated</LakonaRpcGeneratedNamespace>
```

with:

```xml
<LakonaRpcGeneratedNamespace>Client.Generated</LakonaRpcGeneratedNamespace>
```

Remove removed metadata properties and compiler-visible entries:

```xml
<LakonaGameClientRuntime>unity</LakonaGameClientRuntime>
<LakonaGameClientPlatform>unity</LakonaGameClientPlatform>
<LakonaGameClientGameVersion>chat</LakonaGameClientGameVersion>
<CompilerVisibleProperty Include="LakonaGameClientRuntime" />
<CompilerVisibleProperty Include="LakonaGameClientPlatform" />
<CompilerVisibleProperty Include="LakonaGameClientGameVersion" />
```

- [ ] **Step 4: Update docs**

In docs and package README files, replace instructions that say Unity clients opt in with `LakonaRpcGeneration.cs` or `[assembly: LakonaRpcGenerateClient("Rpc.Generated")]` with:

```markdown
Generated Unity and Tuanjie clients use framework-owned source-generator defaults. Generated projects do not contain a project-local RPC generation marker file. The generated client API is emitted into `Client.Generated`.
```

Update source-generation docs so generated game client setup uses:

```xml
<LakonaGameGenerateClient>true</LakonaGameGenerateClient>
```

and no runtime/platform/game-version properties.

Describe game handshake as:

```markdown
`GameClientHello` carries only `ProtocolVersion = 1`; platform, game version, build id, runtime, and capability metadata are application concerns, not default framework handshake fields.
```

Do not hand-edit compiled package-copy contents under `samples/**/Client/Assets/Packages/**` in this task. Those copies are refreshed from locally packed packages after the version bumps in Task 7.

- [ ] **Step 5: Run source scans after migration**

Run:

```powershell
rg -n "LakonaRpcGeneration|LakonaGameClientRuntime|LakonaGameClientPlatform|LakonaGameClientGameVersion|ClientRuntimeVersion|ClientRuntime\s*=|Platform\s*=|GameVersion\s*=|BuildId|SupportedCapabilities|ProtocolVersionMin|ProtocolVersionMax" samples docs src tests -S
```

Expected: no matches except historical references inside the implementation plan/spec documents or compatibility text explicitly saying those names were removed.

Run:

```powershell
rg -n "using Rpc\.Generated|namespace Rpc\.Generated|<LakonaRpcGeneratedNamespace>Rpc\.Generated</LakonaRpcGeneratedNamespace>" samples src tests docs -S
```

Expected: no matches in generated project templates, samples, or active docs. Migrate all committed sample references from `Rpc.Generated` to `Client.Generated`.

- [ ] **Step 6: Commit Task 6**

```powershell
git add samples docs src/Lakona.Rpc.Analyzers/README.md src/Lakona.Game.Client/README.md src/Lakona.Rpc.Client/README.md
git diff --staged
git commit -m "Migrate samples and docs to zero-config clients"
```

## Task 7: Package Version Bumps And Sample Package Copies

**Files:**
- Modify package `.csproj` files under `src/**` touched by implementation.
- Modify `samples/**/Client/Assets/packages.config` entries for changed Lakona packages.
- Refresh committed package copies under `samples/**/Client/Assets/Packages/**` for changed Lakona packages after locally packing the bumped packages.

- [ ] **Step 1: Bump directly modified shippable package versions**

Expected version bumps if the corresponding package source changed:

```xml
<!-- src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj -->
<Version>0.2.3</Version>

<!-- src/Lakona.Game.Client/Lakona.Game.Client.csproj -->
<Version>0.3.3</Version>

<!-- src/Lakona.Game.Server/Lakona.Game.Server.csproj -->
<Version>0.9.7</Version>

<!-- src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj -->
<Version>0.13.2</Version>

<!-- src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj -->
<Version>0.3.5</Version>

<!-- src/Lakona.Tool/Lakona.Tool.csproj -->
<Version>0.15.8</Version>
```

If any additional `src/<PackageName>` production code changed, bump that package by one patch version as well.

- [ ] **Step 2: Pack bumped packages into the local sample feed**

Run:

```powershell
dotnet pack src/Lakona.Game.Abstractions/Lakona.Game.Abstractions.csproj -c Release -o artifacts
dotnet pack src/Lakona.Game.Client/Lakona.Game.Client.csproj -c Release -o artifacts
dotnet pack src/Lakona.Rpc.Core/Lakona.Rpc.Core.csproj -c Release -o artifacts
dotnet pack src/Lakona.Rpc.Analyzers/Lakona.Rpc.Analyzers.csproj -c Release -o artifacts
```

Pack any additional changed package that is copied into Unity samples.

- [ ] **Step 3: Refresh Unity sample package copies**

Update these `packages.config` files to the new package versions for changed Lakona packages:

```text
samples/Game.Unity.Agar/Client/Assets/packages.config
samples/Rpc.Unity.Json.Websocket/Client/Assets/packages.config
samples/Rpc.Unity.MemoryPack.Kcp/Client/Assets/packages.config
samples/Rpc.Unity.MemoryPack.Tcp/Client/Assets/packages.config
```

Replace the corresponding versioned directories under each sample `Client/Assets/Packages/` directory from the locally packed `.nupkg` files in `artifacts/`:

```text
Lakona.Game.Abstractions.0.2.2 -> Lakona.Game.Abstractions.0.2.3
Lakona.Game.Client.0.3.2 -> Lakona.Game.Client.0.3.3
Lakona.Rpc.Core.0.13.1 -> Lakona.Rpc.Core.0.13.2
Lakona.Rpc.Analyzers.0.3.4 -> Lakona.Rpc.Analyzers.0.3.5
```

Carry forward Unity `.meta` files for unchanged relative package files, rename package-root `.meta` files to the new versioned directory names, and remove stale old-version directories and old-version root `.meta` files. The final source scans must include `samples/**/Client/Assets/Packages/**`; do not narrow scans to hide stale committed package-copy files.

- [ ] **Step 4: Run package version graph guard**

Run:

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Expected: PASS. If it reports missing dependent version bumps, bump the listed packages, pack refreshed package copies again when affected, and rerun.

- [ ] **Step 5: Commit Task 7**

```powershell
git add src samples
git diff --staged
git commit -m "Bump package versions for zero-config clients"
```

## Task 8: Final Validation And Hygiene

**Files:**
- No planned source edits. Fix only issues exposed by validation.

- [ ] **Step 1: Run focused tests**

Run:

```powershell
dotnet test tests/Lakona.Game.Abstractions.Tests/Lakona.Game.Abstractions.Tests.csproj --no-restore
dotnet test tests/Lakona.Game.Server.Tests/Lakona.Game.Server.Tests.csproj --no-restore
dotnet test tests/Lakona.Rpc.Analyzers.Tests/Lakona.Rpc.Analyzers.Tests.csproj --no-restore
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --no-restore
```

Expected: PASS for all four projects.

- [ ] **Step 2: Run required source scans**

Run:

```powershell
rg -n "LakonaRpcGeneration|LakonaGameClientRuntime|LakonaGameClientPlatform|LakonaGameClientGameVersion|ClientRuntimeVersion|ClientRuntime\s*=|Platform\s*=|GameVersion\s*=|BuildId|SupportedCapabilities|ProtocolVersionMin|ProtocolVersionMax" src tests samples docs -S
```

Expected: no matches except in docs/spec/plan text that explicitly documents removed names.

Run:

```powershell
rg -n "using Rpc\.Generated|namespace Rpc\.Generated|<LakonaRpcGeneratedNamespace>Rpc\.Generated</LakonaRpcGeneratedNamespace>" src tests samples docs -S
```

Expected: no active generated project template, sample, or current doc references to `Rpc.Generated`.

- [ ] **Step 3: Run package version guard**

Run:

```powershell
pwsh -NoProfile -File scripts/nuget/check-package-version-graph.ps1
```

Expected: PASS.

- [ ] **Step 4: Run solution build when restore is available**

Run:

```powershell
dotnet build Lakona.slnx
```

Expected: PASS. If restore/network is unavailable in the environment, record the exact failure and run the focused `--no-restore` tests that can execute locally.

- [ ] **Step 5: Inspect staged diff and whitespace**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` has no output. `git status --short` only lists intentional files before the final commit.

- [ ] **Step 6: Final commit**

If validation fixes produced additional edits:

```powershell
git add <intentional-validation-fix-paths>
git diff --staged
git commit -m "Validate zero-config client generation"
```

If there are no additional edits, do not create an empty commit.

## Review Gates

- **After Task 2:** Review runtime handshake contract before changing generator/template output.
- **After Task 4:** Review generated source shape; this is the highest-risk point because analyzer defaults and Unity assembly detection meet here.
- **After Task 6:** Review sample/docs migration and source scans.
- **Before merge:** Final integration review across all commits, with focused test output and skipped validations recorded.
