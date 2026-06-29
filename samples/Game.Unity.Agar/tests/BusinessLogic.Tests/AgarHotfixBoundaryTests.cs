using System.Text.RegularExpressions;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarHotfixBoundaryTests
{
    [Fact]
    public void Agar_hotfix_has_one_partial_behavior_per_actor()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var behaviorPattern = new Regex(
            @"\[HotfixBehaviorOf\(typeof\((?<target>\w+)\)\)\]\s*(?<access>public|internal)\s+static\s+(?<partial>partial\s+)?class\s+(?<behavior>\w+Behavior)",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        var matches = Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => behaviorPattern.Matches(File.ReadAllText(file)).Select(match => new
            {
                File = Path.GetRelativePath(Directory.GetCurrentDirectory(), file),
                Target = match.Groups["target"].Value,
                Behavior = match.Groups["behavior"].Value,
                IsPartial = match.Groups["partial"].Success
            }))
            .ToArray();

        var nonActorTargets = matches
            .Where(match => !match.Target.EndsWith("Actor", StringComparison.Ordinal))
            .Select(match => $"{match.File}: {match.Behavior} targets {match.Target}")
            .ToArray();
        var duplicateActorBehaviors = matches
            .Where(match => match.Target.EndsWith("Actor", StringComparison.Ordinal))
            .GroupBy(match => match.Target, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(match => match.Behavior))}")
            .ToArray();
        var nonPartialBehaviors = matches
            .Where(match => !match.IsPartial)
            .Select(match => $"{match.File}: {match.Behavior}")
            .ToArray();
        var wrongNames = matches
            .Where(match => match.Target.EndsWith("Actor", StringComparison.Ordinal))
            .Where(match => !string.Equals(match.Behavior, match.Target[..^"Actor".Length] + "Behavior", StringComparison.Ordinal))
            .Select(match => $"{match.File}: {match.Target} -> {match.Behavior}")
            .ToArray();

        Assert.True(nonActorTargets.Length == 0, $"Behavior targets must be actors: {string.Join("; ", nonActorTargets)}");
        Assert.True(duplicateActorBehaviors.Length == 0, $"Duplicate behavior targets: {string.Join("; ", duplicateActorBehaviors)}");
        Assert.True(nonPartialBehaviors.Length == 0, $"Behavior classes must be partial: {string.Join("; ", nonPartialBehaviors)}");
        Assert.True(wrongNames.Length == 0, $"Behavior names must match actor names: {string.Join("; ", wrongNames)}");
        Assert.Contains(matches, match => match.Target == "UserActor" && match.Behavior == "UserBehavior");
        Assert.DoesNotContain(matches, match => match.Behavior is "PlayerSessionBehavior" or "ArenaSimulationBehavior" or "ArenaSettlementBehavior");
    }

    [Fact]
    public void Agar_shared_code_does_not_reference_server_hotfix()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Shared/Shared.csproj")
            .DirectoryName!;
        var forbidden = new Regex(
            @"Lakona\.Game\.Server\.Hotfix|HotfixDispatch|HotfixState|HotfixBehaviorOf",
            RegexOptions.CultureInvariant);

        var violations = Directory.GetFiles(sampleRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(violations.Length == 0, $"Shared code must not reference server hotfix APIs: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_hotfix_does_not_reintroduce_process_local_session_registry()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var scannedRoots = new[]
        {
            Path.Combine(sampleRoot, "Server", "Hotfix"),
            Path.Combine(sampleRoot, "tests", "BusinessLogic.Tests"),
        };
        var thisFile = Path.GetFullPath(FindRepositoryFile("samples/Game.Unity.Agar/tests/BusinessLogic.Tests/AgarHotfixBoundaryTests.cs").FullName);
        var forbiddenTokens = new[]
        {
            "Player" + "Session" + "Registry",
            "Player" + "Session" + "Registration",
            "Get" + "Connection" + "(",
            "Get" + "By" + "Room" + "(",
            "Register" + "Control" + "(",
            "Player" + "Session" + "Registry.cs",
            "Player" + "Session" + "Registration.cs",
        };
        var violations = scannedRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(IsTextSourceFile)
            .Where(file => !string.Equals(Path.GetFullPath(file), thisFile, StringComparison.OrdinalIgnoreCase))
            .Select(file => new
            {
                File = file,
                Matches = forbiddenTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Agar hotfix must use actor-owned session state, not a process-local session registry: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_battle_service_uses_constructor_injection_without_allocating_submit_input_instances()
    {
        var battleServicePath = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Services/BattleService.cs");
        var text = File.ReadAllText(battleServicePath.FullName);

        Assert.DoesNotContain("Player" + "Session" + "Registry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPlayerIdByConnection", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Get" + "Connection" + "(", text, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSession", text, StringComparison.Ordinal);
        Assert.Contains("IsLocalRuntimeOwner", text, StringComparison.Ordinal);
        Assert.Contains("_localNode", text, StringComparison.Ordinal);
        Assert.Contains("_users", text, StringComparison.Ordinal);
        Assert.Contains("_rooms", text, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Services.GetRequiredService", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_hotfix_services_do_not_parse_node_identity_or_pick_arbitrary_runtime_nodes()
    {
        var servicesRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs")
            .DirectoryName!;
        var hotfixRoot = Directory.GetParent(servicesRoot)!.FullName;
        var forbiddenTokens = new[]
        {
            "Runtime" + "Gateway" + "Selector",
            "Runtime" + "Node" + "Identity",
            "Endpoint" + "Descriptor" + "Mapper",
            "Environment." + "MachineName",
            "Environment." + "ProcessId",
            "." + "AnyAsync" + "(",
            "Resolve" + "NodeId" + "("
        };

        var violations = Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                Matches = forbiddenTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Agar hotfix code must use framework node identity and deterministic runtime placement instead of sample-side identity parsing or arbitrary runtime selection: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Matchmaking_allocates_remote_rooms_through_battle_runtime_feature()
    {
        var matchmaking = File.ReadAllText(FindRepositoryFile(
            "samples/Game.Unity.Agar/Server/Hotfix/State/Matchmaking/MatchmakingBehavior.cs").FullName);

        Assert.Contains("BattleRuntimeRoomAllocation.FeatureName", matchmaking, StringComparison.Ordinal);
        Assert.Contains("IFeatureCommandClient", matchmaking, StringComparison.Ordinal);
        Assert.Contains("SendToNodeAsync<BattleRuntimeRoomAllocationRequest, BattleRuntimeRoomAllocationReply>", matchmaking, StringComparison.Ordinal);
        Assert.Contains("AllocateRemoteRoomAsync", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("IFeatureMessageTransport", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureMessageRequest", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureMessageReply", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remote(new NodeId(", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remote(", ExtractMethodBody(matchmaking, "CreateRoomAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimeConnectionMapper_is_only_a_client_dto_projection()
    {
        var mapper = File.ReadAllText(FindRepositoryFile(
            "samples/Game.Unity.Agar/Server/Hotfix/Services/RealtimeConnectionMapper.cs").FullName);

        Assert.Contains("ToRealtimeConnectionInfo", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IClusterNodeDiscovery", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("AnyAsync", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_services_do_not_route_actor_behavior_through_stable_state_store_bridges()
    {
        var servicesRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Services/PlayerService.cs")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            "IUserStateStore",
            "IPlayerSessionStateStore",
            "IMatchmakingStateStore",
            "IRoomStateStore",
            "ILeaderboardStateStore",
            "services.Sessions",
        };
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(servicesRoot, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            var matches = forbiddenTokens
                .Where(token => text.Contains(token, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length > 0)
            {
                violations.Add($"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}: {string.Join(", ", matches)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Hotfix services must dispatch actor behavior directly instead of routing through stable state-store bridges: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_app_does_not_define_stable_state_store_bridges_or_hand_written_hotfix_dispatch()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var appRoot = Path.Combine(sampleRoot, "Server", "App");
        var deletedBridge = Path.Combine(appRoot, "State", "StateStores.cs");
        var forbidden = new Regex(
            @"\bI(?:User|PlayerSession|Matchmaking|Room|Leaderboard)StateStore\b|HotfixDispatch\.Invoke\s*<|InvokeServiceAsync\s*<[^;]+,\s*string",
            RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderIgnoredSampleDirectory(sampleRoot, file))
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.False(File.Exists(deletedBridge), "Server/App/State/StateStores.cs should not exist.");
        Assert.True(
            violations.Length == 0,
            $"Server.App must not define stable state-store bridges or hand-written hotfix dispatch: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_sample_actor_hotfix_dispatch_is_limited_to_scripted_gameplay_extension_points()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(sampleRoot, "Shared/Gameplay/ArenaSimulation.cs")),
        };
        var hotfixDispatch = new Regex(@"HotfixDispatch\.Invoke\s*<", RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(sampleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsUnderIgnoredSampleDirectory(sampleRoot, file))
            .Where(file => !allowedFiles.Contains(Path.GetFullPath(file)))
            .Select(file => new
            {
                File = file,
                HasHotfixActorDispatch = hotfixDispatch.IsMatch(File.ReadAllText(file)),
            })
            .Where(result => result.HasHotfixActorDispatch)
            .Select(result => Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Actor hotfix dispatch is only allowed in Shared/Gameplay/ArenaSimulation.cs scripted gameplay extension points: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_hotfix_business_code_uses_generated_actor_selectors()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var hotfixRoot = Path.Combine(sampleRoot, "Server", "Hotfix");
        var violations = Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new { File = file, Text = File.ReadAllText(file) })
            .Where(result => result.Text.Contains(".AskAsync<", StringComparison.Ordinal) ||
                result.Text.Contains(".TellAsync<", StringComparison.Ordinal))
            .Select(result => Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Agar hotfix business code must use generated actor selectors, not raw AskAsync/TellAsync: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Stable_state_actors_do_not_declare_business_methods()
    {
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/State/Users/UserActor.cs")
            .Directory!.Parent!.FullName;
        var actorFiles = Directory.GetFiles(stateRoot, "*Actor.cs", SearchOption.AllDirectories);
        var forbidden = new Regex(
            @"^\s*(public|internal|private|protected)\s+(static\s+|override\s+|async\s+)*(Task|ValueTask|[\w<>]+)\s+\w+\s*\(",
            RegexOptions.Multiline);

        foreach (var file in actorFiles)
        {
            var text = File.ReadAllText(file);
            var matches = forbidden.Matches(text)
                .Where(match => !match.Value.Contains("OnActivateAsync", StringComparison.Ordinal))
                .Where(match => !match.Value.Contains("OnDeactivateAsync", StringComparison.Ordinal))
                .ToArray();

            Assert.True(matches.Length == 0, $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)} declares stable actor methods: {string.Join(", ", matches.Select(match => match.Value.Trim()))}");
        }
    }

    [Fact]
    public void Agar_server_app_does_not_contain_user_runtime_features_or_app_hotfix_adapters()
    {
        var appRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .DirectoryName!;
        var forbiddenRelativePaths = new[]
        {
            "Features/BattleRuntimeFeature.cs",
            "Features/DatabaseFeature.cs",
            "Features/StateStoreFeature.cs",
            "Features/MatchmakingFeature.cs",
            "Features/LeaderboardFeature.cs",
            "Hosting/" + "Matchmaking" + "Hosted" + "Service.cs",
            "Hotfix/Agar" + "Hotfix" + "Runtime" + "Events.cs",
            "Hotfix/AgarRuntimeContracts.cs",
            "Realtime/" + "Room" + "Runtime.cs",
            "Realtime/" + "Room" + "Runtime" + "Host.cs",
        };
        var existingForbiddenPaths = forbiddenRelativePaths
            .Where(path => File.Exists(Path.Combine(appRoot, path)))
            .ToArray();

        Assert.True(
            existingForbiddenPaths.Length == 0,
            $"Server.App must not contain user runtime Feature classes, App hotfix adapters, or runtime loops: {string.Join(", ", existingForbiddenPaths)}");

        var forbiddenTerms = new[]
        {
            "Server.App.Hotfix",
            "Agar" + "Hotfix" + "Runtime" + "Events",
            "IAgar" + "Runtime" + "Service",
            "Agar" + "Runtime" + "MethodIds",
            "Room" + "Runtime" + "Host",
            "Room" + "Runtime",
            "Matchmaking" + "Hosted" + "Service",
        };
        var forbidden = new Regex(
            @"\b(" + string.Join("|", forbiddenTerms.Select(Regex.Escape)) + @")\b",
            RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Server.App still references removed runtime boundary types: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_user_features_are_hotfix_descriptors()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var hotfixText = ReadAllTextFiles(hotfixRoot);
        var stateStoreFeature = File.ReadAllText(Path.Combine(hotfixRoot, "Features", "StateStoreFeatures.cs"));
        var stateStorePlacement = File.ReadAllText(Path.Combine(hotfixRoot, "Services", "StateStoreUserActorPlacement.cs"));
        var battleRuntimeFeature = File.ReadAllText(Path.Combine(hotfixRoot, "Features", "BattleRuntimeFeature.cs"));
        var battleRuntimePlacement = File.ReadAllText(Path.Combine(hotfixRoot, "Features", "BattleRuntimeRoomAllocation.cs"));

        Assert.DoesNotContain("[HotfixFeature(\"database\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(StateStoreUserActorPlacement.FeatureName)]", stateStoreFeature, StringComparison.Ordinal);
        Assert.Contains("public const string FeatureName = \"state-store\";", stateStorePlacement, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"matchmaking\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(\"leaderboard\")]", hotfixText, StringComparison.Ordinal);
        Assert.Contains("[HotfixFeature(BattleRuntimeRoomAllocation.FeatureName)]", battleRuntimeFeature, StringComparison.Ordinal);
        Assert.Contains("public const string FeatureName = \"battle-runtime\";", battleRuntimePlacement, StringComparison.Ordinal);
        Assert.Contains("ScheduleActorTick<MatchmakingActor>", hotfixText, StringComparison.Ordinal);
        Assert.Contains("ScheduleActiveActorTicks<RoomActor>", hotfixText, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_matchmaking_feature_owns_default_queue_actor_ticks()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var featureRoot = Path.Combine(sampleRoot, "Server", "Hotfix", "Features");

        var matchmakingFeature = File.ReadAllText(Path.Combine(featureRoot, "MatchmakingFeature.cs"));
        var battleRuntimeFeature = File.ReadAllText(Path.Combine(featureRoot, "BattleRuntimeFeature.cs"));

        Assert.False(Directory.Exists(Path.Combine(sampleRoot, "Server", "App", "Hosting")));
        Assert.Contains("EnsureLocalActor<MatchmakingActor>", matchmakingFeature, StringComparison.Ordinal);
        Assert.Contains("ScheduleActorTick<MatchmakingActor>", matchmakingFeature, StringComparison.Ordinal);
        Assert.Contains("\"default\"", matchmakingFeature, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleActorTick<MatchmakingActor>", battleRuntimeFeature, StringComparison.Ordinal);
    }

    [Fact]
    public void Matchmaking_shared_contracts_do_not_expose_server_only_diagnostics()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var sharedRoot = Path.Combine(sampleRoot, "Shared");

        Assert.False(
            File.Exists(Path.Combine(sharedRoot, "State", "MatchmakingContracts.cs")),
            "Shared/State/MatchmakingContracts.cs should not exist.");
        Assert.False(
            File.Exists(Path.Combine(sharedRoot, "State", "MatchmakingContracts.cs.meta")),
            "Shared/State/MatchmakingContracts.cs.meta should not exist.");
        Assert.False(
            File.Exists(Path.Combine(sampleRoot, "Server", "Hotfix", "State", "Matchmaking", "MatchmakingStatusSnapshot.cs")),
            "Server/Hotfix/State/Matchmaking/MatchmakingStatusSnapshot.cs should not exist.");

        var serverContracts = File.ReadAllText(Path.Combine(
            sampleRoot,
            "Server",
            "App",
            "Contracts",
            "MatchmakingActorContracts.cs"));
        var serverStateMatch = Regex.Match(
            serverContracts,
            @"public\s+sealed\s+class\s+MatchmakingState\s*\{(?<body>.*?)^\s*\}",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(serverStateMatch.Success, "Expected server-side MatchmakingState in Server/App/Contracts/MatchmakingActorContracts.cs.");

        var serverStateProperties = Regex.Matches(
                serverStateMatch.Groups["body"].Value,
                @"public\s+(?<type>[^{;]+?)\s+(?<name>\w+)\s*\{\s*get;\s*set;\s*\}",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(new[] { "DefaultRoomSize", "PendingTickets" }, serverStateProperties);

        var sharedText = ReadAllTextFiles(sharedRoot);
        var forbiddenDeclarations = new[]
        {
            "public sealed class MatchmakingStatusSnapshot",
            "public sealed class MatchmakingState",
            "public sealed class MatchmakingEnqueueRequest",
            "public sealed class MatchmakingEnqueueResult",
            "public sealed class MatchmakingCancelRequest",
            "public sealed class MatchmakingCancelResult",
            "public sealed class MatchmakingTickRequest",
            "public sealed class MatchmakingQueueTicket",
            "public sealed class RoomAssignment",
        };

        foreach (var declaration in forbiddenDeclarations)
        {
            Assert.DoesNotContain(declaration, sharedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Agar_uses_canonical_user_actor_ids()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var serverSourceRoots = new[]
        {
            Path.Combine(sampleRoot, "Server", "App"),
            Path.Combine(sampleRoot, "Server", "Hotfix"),
        };
        var forbidden = new Regex(@"session:", RegexOptions.CultureInvariant);
        var violations = serverSourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsUnderIgnoredSampleDirectory(sampleRoot, file))
            .Select(file => new
            {
                File = file,
                Matches = forbidden.Matches(File.ReadAllText(file)).Select(match => match.Value).Distinct().ToArray()
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File)}: {string.Join(", ", result.Matches)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Agar server source must use canonical actor ids without session: prefixes: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_shared_contracts_do_not_expose_actor_interfaces()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var sharedText = ReadAllTextFiles(Path.Combine(sampleRoot, "Shared"));
        var forbiddenDeclarations = new[]
        {
            "public interface IUserActor",
            "public interface IRoomActor",
            "public interface IUserSessionActor",
        };

        foreach (var declaration in forbiddenDeclarations)
        {
            Assert.DoesNotContain(declaration, sharedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Agar_unity_client_uses_framework_handshake_contracts()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var clientRoot = Path.Combine(sampleRoot, "Client");
        var handshakeShim = Path.Combine(clientRoot, "Assets", "Scripts", "Rpc", "GameHandshakeDtos.cs");
        var session = File.ReadAllText(Path.Combine(
            clientRoot,
            "Assets",
            "Scripts",
            "Gameplay",
            "DotArenaNetworkSession.cs"));
        var tester = File.ReadAllText(Path.Combine(
            clientRoot,
            "Assets",
            "Scripts",
            "Rpc",
            "Testing",
            "RpcConnectionTester.cs"));
        var controlDisconnectHandler = ExtractMethodBody(session, "HandleControlDisconnected");
        var realtimeDisconnectHandler = ExtractMethodBody(session, "HandleRealtimeDisconnected");
        var ensureRealtimeConnected = ExtractMethodBody(session, "EnsureRealtimeConnectedAsync");
        var testerDisconnectHandler = ExtractMethodBody(tester, "OnDisconnected");

        Assert.False(File.Exists(handshakeShim), "Unity client must use generated LakonaGameClient handshake orchestration.");
        Assert.Contains("LakonaGameClient? _controlConnection", session, StringComparison.Ordinal);
        Assert.Contains("LakonaGameClient? _realtimeConnection", session, StringComparison.Ordinal);
        Assert.Contains("new LakonaGameClient(", session, StringComparison.Ordinal);
        Assert.Contains("await _controlConnection.ConnectAsync", session, StringComparison.Ordinal);
        Assert.Contains("await _realtimeConnection.ConnectAsync", session, StringComparison.Ordinal);
        Assert.Contains("_controlConnection.Api.Shared.Login", session, StringComparison.Ordinal);
        Assert.Contains("_realtimeConnection.Api.Shared.Battle", session, StringComparison.Ordinal);
        Assert.DoesNotContain("RpcNotificationBindings", session, StringComparison.Ordinal);
        Assert.DoesNotContain("callbacks.Add", session, StringComparison.Ordinal);
        Assert.DoesNotContain("HandshakeAsync", session, StringComparison.Ordinal);
        Assert.DoesNotContain("GameClientHello", session, StringComparison.Ordinal);
        Assert.DoesNotContain("new RpcMethod<GameClientHello, GameServerHello>(0, 1)", session, StringComparison.Ordinal);
        Assert.Contains("private async Task DisposeControlAfterDisconnectAsync()", session, StringComparison.Ordinal);
        Assert.Contains("private async Task DisposeRealtimeAfterDisconnectAsync()", session, StringComparison.Ordinal);
        Assert.Contains("_ = DisposeControlAfterDisconnectAsync();", controlDisconnectHandler, StringComparison.Ordinal);
        Assert.Contains("_ = DisposeRealtimeAfterDisconnectAsync();", realtimeDisconnectHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_controlConnection = null;", controlDisconnectHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_realtimeConnection = null;", realtimeDisconnectHandler, StringComparison.Ordinal);
        Assert.Contains("catch", ensureRealtimeConnected, StringComparison.Ordinal);
        Assert.Contains("await DisposeRealtimeAsync().ConfigureAwait(false);", ensureRealtimeConnected, StringComparison.Ordinal);
        Assert.Contains("throw;", ensureRealtimeConnected, StringComparison.Ordinal);
        Assert.Contains("_ = CleanupAsync();", testerDisconnectHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_connection = null;", testerDisconnectHandler, StringComparison.Ordinal);
    }

    private static FileInfo FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return new FileInfo(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private static bool IsUnderIgnoredSampleDirectory(string sampleRoot, string file)
    {
        var parts = Path.GetRelativePath(sampleRoot, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var ignoredDirectories = new[]
        {
            "bin",
            "obj",
            "Obj",
            "Library",
            "Temp",
            "Logs",
            "UserSettings",
            "Build",
            "Builds",
            ".godot",
            ".import",
        };

        return parts.Any(part => ignoredDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)) ||
            parts.Length >= 2 &&
            string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parts[1], "Packages", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAllTextFiles(string root)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsTextSourceFile)
                     .Order(StringComparer.Ordinal))
        {
            builder.AppendLine(File.ReadAllText(path));
        }

        return builder.ToString();
    }

    private static bool IsTextSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".csproj" or ".json" or ".slnx" or ".props" or ".xml" or ".txt";
    }

    private static string ExtractMethodBody(string text, string methodName)
    {
        var marker = methodName + "(";
        var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Expected to find method '{methodName}'.");

        var openBrace = text.IndexOf('{', markerIndex);
        Assert.True(openBrace >= 0, $"Expected method '{methodName}' to have a body.");

        var depth = 0;
        for (var index = openBrace; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(openBrace, index - openBrace + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }
}
