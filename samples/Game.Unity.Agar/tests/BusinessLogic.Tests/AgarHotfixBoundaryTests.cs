using System.Text.RegularExpressions;
using Xunit;

namespace BusinessLogic.Tests;

public sealed class AgarHotfixBoundaryTests
{
    [Fact]
    public void Login_service_dispatches_one_combined_user_actor_call()
    {
        var loginService = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Users/LoginService.cs").FullName);
        var userBehavior = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Users/UserBehavior.cs").FullName);
        var userContracts = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Users/UserActorContracts.cs").FullName);
        var sessionContracts = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Sessions/PlayerSessionContracts.cs").FullName);

        Assert.Contains("static behavior => behavior.LoginAndAttachAsync", loginService, StringComparison.Ordinal);
        Assert.Contains("[ActorMethod(\"login-and-attach\")]", userBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("static behavior => behavior.LoginAsync", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain("static behavior => behavior.AttachAsync", loginService, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(loginService, @"\.CallAsync\(").Cast<Match>());
        Assert.Contains("RollbackLoginSessionAsync", loginService, StringComparison.Ordinal);
        Assert.Contains(".TerminateSessionAsync(", loginService, StringComparison.Ordinal);
        Assert.DoesNotContain(" LoginAsync(this UserActor", userBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain(" AttachAsync(this UserActor", userBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("UserLoginRequest", userContracts, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerSessionAttachRequest", sessionContracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_hotfix_has_one_partial_behavior_per_actor()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var behaviorPattern = new Regex(
            @"\[HotfixBehaviorOf\(typeof\((?<target>\w+)\)\)\]\s*(?<access>public|internal)\s+sealed\s+(?<partial>partial\s+)?class\s+(?<behavior>\w+Behavior)",
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
        var battleServicePath = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Rooms/BattleService.cs");
        var text = File.ReadAllText(battleServicePath.FullName);

        Assert.DoesNotContain("Player" + "Session" + "Registry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPlayerIdByConnection", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Get" + "Connection" + "(", text, StringComparison.Ordinal);
        Assert.Contains("call.CurrentSession", text, StringComparison.Ordinal);
        Assert.Contains("IsLocalRuntimeOwner", text, StringComparison.Ordinal);
        Assert.Contains("_localNode", text, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RoomActors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Services.GetRequiredService", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_hotfix_service_code_does_not_resolve_loggers_through_logger_factory()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var violations = Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                Text = File.ReadAllText(file)
            })
            .Where(result =>
                result.Text.Contains("ILoggerFactory", StringComparison.Ordinal) ||
                result.Text.Contains("CreateLogger<", StringComparison.Ordinal))
            .Select(result => Path.GetRelativePath(Directory.GetCurrentDirectory(), result.File))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Hotfix service code should use typed ILogger<T> dependencies instead of resolving ILoggerFactory per call: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_hotfix_services_do_not_build_ad_hoc_dependency_containers_from_calls()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            "AgarServiceDependencies",
            "AgarLifecycleDependencies",
            ".From(call)",
            ".From(call.Services)",
            "CreateDependencies("
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
            $"Hotfix services should use constructor injection instead of call-derived dependency containers: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_hotfix_does_not_bypass_framework_actor_placement_with_membership_access()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            "IClusterMembership",
            "GetService<IClusterMembership>",
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
            $"Agar hotfix code must use framework actor placement instead of accessing cluster membership: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Agar_hotfix_services_do_not_parse_node_identity_or_pick_arbitrary_runtime_nodes()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
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
    public void Matchmaking_allocates_rooms_through_actor_placement()
    {
        var matchmaking = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Matchmaking/MatchmakingBehavior.cs",
            "MatchmakingBehavior*.cs");

        Assert.Contains("private async ValueTask<RoomSettlementResult> AllocateRoomAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("var placement = _actors.Place<RoomActor>(roomId)", matchmaking, StringComparison.Ordinal);
        Assert.Contains("await placement.CreateAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("await placement.DestroyAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.CreateAsync", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("static behavior => behavior.StartAsync", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("BattleRuntimeRoomAllocation", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "ture", "CommandClient"), matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO.Hashing", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("I", "Fea", "ture", "MessageTransport"), matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Fea", "ture", "MessageRequest"), matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Fea", "ture", "MessageReply"), matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remote(new NodeId(", matchmaking, StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimeConnectionMapper_is_only_a_client_dto_projection()
    {
        var mapper = File.ReadAllText(FindRepositoryFile(
            "samples/Game.Unity.Agar/Server/Hotfix/Sessions/RealtimeConnectionMapper.cs").FullName);

        Assert.Contains("ToRealtimeConnectionInfo", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IClusterMembership", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("AnyAsync", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_services_do_not_route_actor_behavior_through_stable_state_store_bridges()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
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

        foreach (var file in Directory.GetFiles(hotfixRoot, "*.cs", SearchOption.AllDirectories))
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
    public void State_store_actor_placement_does_not_pre_register_or_rollback_remote_routes_from_caller()
    {
        var loginService = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Users/LoginService.cs").FullName);
        var playerService = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Players/PlayerService.cs").FullName);

        foreach (var service in new[] { loginService, playerService })
        {
            Assert.DoesNotContain("RegisterAsync(actorId, ownerNode", service, StringComparison.Ordinal);
            Assert.DoesNotContain("UnregisterAsync(actorId, ownerNode", service, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Agar_hotfix_business_code_does_not_manually_update_actor_directory_cache()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            "IActorDirectoryCache",
            "directoryCache.Set",
            "directoryCache.Remove",
            ".GetRequiredService<IActorDirectoryCache>()",
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
            $"Agar hotfix business code must let ActorActivationCatalog and generated selectors own directory cache updates: {string.Join("; ", violations)}");
    }

    [Fact]
    public void State_store_actor_placement_does_not_keep_legacy_ensure_actor_protocol_names()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var forbiddenTokens = new[]
        {
            string.Concat("Ensure", "User", "Actor"),
            string.Concat("Ensure", "Leaderboard", "Actor"),
            string.Concat("Ensure", "Actor", "Reply"),
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
            $"State-store actor placement must use current Create*Actor protocol names: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Battle_runtime_room_actor_creation_uses_actor_placement()
    {
        var matchmaking = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Matchmaking/MatchmakingBehavior.cs",
            "MatchmakingBehavior*.cs");
        var roomBehavior = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Rooms/RoomBehavior.cs",
            "RoomBehavior*.cs");
        Assert.Contains("var placement = _actors.Place<RoomActor>(roomId)", matchmaking, StringComparison.Ordinal);
        Assert.Contains("await placement.CreateAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("await placement.DestroyAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.CreateAsync", matchmaking, StringComparison.Ordinal);
        Assert.DoesNotContain("static behavior => behavior.StartAsync", matchmaking, StringComparison.Ordinal);
        Assert.Contains("self.Context.RequestDeactivation()", roomBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("BattleRuntime", "Fea", "ture"), matchmaking, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_startup_declares_actor_hosts_without_removed_handlers()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var startup = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Hotfix", "HotfixStartup.cs"));
        var removedRoot = Path.Combine(sampleRoot, "Server", "Hotfix", string.Concat("Fea", "tures"));

        Assert.Contains("RegisterStartup<", startup, StringComparison.Ordinal);
        Assert.Contains("RegisterStartup<MatchmakingActor, MatchmakingQueueId>", startup, StringComparison.Ordinal);
        Assert.Contains("RegisterStartup<LeaderboardActor, LeaderboardId>", startup, StringComparison.Ordinal);
        Assert.Contains("RegisterPlacement<UserActor, UserId>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterPlacement<RoomActor", startup, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(removedRoot, string.Concat("StateStore", "Fea", "tures.cs"))));
        Assert.False(File.Exists(Path.Combine(removedRoot, string.Concat("BattleRuntime", "Fea", "ture.cs"))));
    }

    [Fact]
    public void Agar_app_does_not_define_stable_state_store_bridges_or_hand_written_hotfix_dispatch()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var appRoot = Path.Combine(sampleRoot, "Server", "App");
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
            .Where(result => !result.File.EndsWith("MatchmakingTimerCallbacks.cs", StringComparison.OrdinalIgnoreCase))
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
        var stateRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Users/UserActor.cs")
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
    public void Agar_server_app_does_not_contain_user_runtime_components_or_app_hotfix_adapters()
    {
        var appRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .DirectoryName!;
        var forbiddenRelativePaths = new[]
        {
            string.Concat("Fea", "tures/BattleRuntime", "Fea", "ture.cs"),
            string.Concat("Fea", "tures/Database", "Fea", "ture.cs"),
            string.Concat("Fea", "tures/StateStore", "Fea", "ture.cs"),
            string.Concat("Fea", "tures/Matchmaking", "Fea", "ture.cs"),
            string.Concat("Fea", "tures/Leaderboard", "Fea", "ture.cs"),
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
            $"Server.App must not contain removed user runtime classes, App hotfix adapters, or runtime loops: {string.Join(", ", existingForbiddenPaths)}");

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
    public void Agar_actor_hosts_are_declared_in_hotfix_startup()
    {
        var hotfixRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Server.Hotfix.csproj")
            .DirectoryName!;
        var startup = File.ReadAllText(Path.Combine(hotfixRoot, "HotfixStartup.cs"));
        var fixedScheduleCall = string.Concat("Schedule", "ActorTick<MatchmakingActor>");
        var activeScheduleCall = string.Concat("Schedule", "ActiveActorTicks<RoomActor>");

        Assert.Contains("RegisterStartup<", startup, StringComparison.Ordinal);
        Assert.Contains("RegisterPlacement<UserActor, UserId>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterPlacement<RoomActor", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Fea", "ture"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(fixedScheduleCall, ReadAllTextFiles(hotfixRoot), StringComparison.Ordinal);
        Assert.DoesNotContain(activeScheduleCall, ReadAllTextFiles(hotfixRoot), StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_startup_owns_default_queue_actor_creation()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var startup = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Hotfix", "HotfixStartup.cs"));
        var fixedScheduleCall = string.Concat("Schedule", "ActorTick<MatchmakingActor>");
        var activeScheduleCall = string.Concat("Schedule", "ActiveActorTicks<RoomActor>");

        Assert.False(Directory.Exists(Path.Combine(sampleRoot, "Server", "App", "Hosting")));
        Assert.Contains("RegisterStartup<MatchmakingActor, MatchmakingQueueId>", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Ensure", "Local", "Actor"), startup, StringComparison.Ordinal);
        Assert.DoesNotContain(fixedScheduleCall, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(activeScheduleCall, startup, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_matchmaking_and_room_actors_own_lakona_timer_ids()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var matchmakingActor = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "Matchmaking", "MatchmakingActor.cs"));
        var matchmakingBehavior = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Matchmaking/MatchmakingBehavior.cs",
            "MatchmakingBehavior*.cs");
        var roomActor = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "Rooms", "RoomActor.cs"));
        var roomBehavior = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Rooms/RoomBehavior.cs",
            "RoomBehavior*.cs");
        var timerKeysPath = Path.Combine(sampleRoot, "Server", "Hotfix", string.Concat("Fea", "tures"), string.Concat("Fea", "tureTimerKeys.cs"));

        Assert.False(File.Exists(timerKeysPath));

        Assert.Contains("internal TimerId MatchmakingTimerId", matchmakingActor, StringComparison.Ordinal);
        Assert.Contains("[ActorStart]", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("[ActorStop]", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(matchmakingBehavior, @"\[ActorIgnore\]").Count);
        Assert.Contains("StartTimerAsync", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("StopTimerAsync", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("EnsureMatchmakingTimerAsync", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("DestroyMatchmakingTimerAsync", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("CreatePeriodicTimerAsync(", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("static (MatchmakingTimerCallbacks callbacks) => callbacks.TickAsync", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("DestroyTimerAsync(timerId, CancellationToken.None)", matchmakingBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("IsMissingLakonaTimerScope", matchmakingBehavior, StringComparison.Ordinal);

        Assert.Contains("internal TimerId FrameRelayTimerId", roomActor, StringComparison.Ordinal);
        Assert.Contains("EnsureFrameRelayTimerAsync", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("DestroyFrameRelayTimerAsync", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("CreatePeriodicTimerAsync(", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("static (BattleRuntimeTimerCallbacks callbacks) => callbacks.TickAsync", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("new FrameRelayTimerArgs { RoomId = roomId }", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("await DestroyFrameRelayTimerAsync(self).ConfigureAwait(false);", roomBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("IsMissingLakonaTimerScope", roomBehavior, StringComparison.Ordinal);
        Assert.Contains("DestroyTimerAsync(timerId, CancellationToken.None)", roomBehavior, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_runtime_allocation_command_file_is_removed()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;

        Assert.False(File.Exists(Path.Combine(
            sampleRoot,
            "Server",
            "Hotfix",
            string.Concat("Fea", "tures"),
            "BattleRuntimeRoomAllocation.cs")));
    }

    [Fact]
    public void Agar_timer_callbacks_dispatch_stable_tick_messages_and_public_behavior_methods()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var matchmakingCallbacks = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Hotfix", "Matchmaking", "MatchmakingTimerCallbacks.cs"));
        var battleRuntimeCallbacks = File.ReadAllText(Path.Combine(sampleRoot, "Server", "Hotfix", "Rooms", "BattleRuntimeTimerCallbacks.cs"));
        var matchmakingMessages = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "Matchmaking", "MatchmakingActorContracts.cs"));
        var roomMessages = File.ReadAllText(Path.Combine(sampleRoot, "Server", "App", "Rooms", "RoomActorContracts.cs"));
        var matchmakingBehavior = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Matchmaking/MatchmakingBehavior.cs",
            "MatchmakingBehavior*.cs");
        var roomBehavior = ReadPartialType(
            "samples/Game.Unity.Agar/Server/Hotfix/Rooms/RoomBehavior.cs",
            "RoomBehavior*.cs");

        Assert.Contains("public sealed partial class MatchmakingTickRequest", matchmakingMessages, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class RoomFrameRequest", roomMessages, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask RunTickAsync(MatchmakingActor self, MatchmakingTickRequest request", matchmakingBehavior, StringComparison.Ordinal);
        Assert.Contains("public ValueTask RunFrameAsync(RoomActor self, RoomFrameRequest request", roomBehavior, StringComparison.Ordinal);

        Assert.Contains("TimerTick<MatchmakingTimerArgs>", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("tick.Services", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.Contains("LocalExact<MatchmakingActor>(ActorId.From(tick.Args.OwnerActorId))", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.Contains("PostAsync(", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.RunTickAsync", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.Contains("ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("TellAsync<MatchmakingActor>", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("TryTell<MatchmakingActor>", matchmakingCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorId.From(\"default\")", matchmakingCallbacks, StringComparison.Ordinal);

        Assert.Contains("TimerTick<FrameRelayTimerArgs>", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("tick.Services", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("Local<RoomActor>(new RoomId(tick.Args.RoomId))", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("PostAsync(", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.RunFrameAsync", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("new RoomFrameRequest", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("LogDebug", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.Contains("ObservedAtUtc = tick.ObservedAtUtc.UtcDateTime", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("GetActiveActorIds(typeof(RoomActor))", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("break;", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("throw", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("TryTell<RoomActor>", battleRuntimeCallbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorTellResult.Accepted", battleRuntimeCallbacks, StringComparison.Ordinal);
    }

    [Fact]
    public void Agar_docs_do_not_describe_timer_migration_as_future_work()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var docsText = ReadAllTextFiles(Path.Combine(sampleRoot, "docs")) + File.ReadAllText(Path.Combine(sampleRoot, "README.md"));

        Assert.DoesNotContain("后续 LakonaTimer migration 接回", docsText, StringComparison.Ordinal);
        Assert.DoesNotContain("旧 ActorTick 已删除", docsText, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_does_not_contain_server_only_state_contracts()
    {
        var sampleRoot = FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Program.cs")
            .Directory!.Parent!.Parent!.FullName;
        var sharedStateRoot = Path.Combine(sampleRoot, "Shared", "State");

        var sharedStateFiles = Directory.Exists(sharedStateRoot)
            ? Directory.EnumerateFiles(sharedStateRoot, "*.cs", SearchOption.AllDirectories).ToArray()
            : [];

        Assert.Empty(sharedStateFiles);
        Assert.True(File.Exists(Path.Combine(sampleRoot, "Server", "App", "Routing", "GatewayEndpointDescriptor.cs")));
        Assert.True(File.Exists(Path.Combine(sampleRoot, "Server", "App", "Sessions", "PlayerSessionContracts.cs")));
        Assert.True(File.Exists(Path.Combine(sampleRoot, "Server", "App", "Rooms", "RoomContracts.cs")));
        Assert.True(File.Exists(Path.Combine(sampleRoot, "Server", "App", "Leaderboard", "LeaderboardStateContracts.cs")));
        Assert.True(File.Exists(Path.Combine(sampleRoot, "Server", "App", "Users", "UserStateContracts.cs")));
    }

    [Fact]
    public void Leaderboard_entry_contract_has_one_shared_definition()
    {
        var sharedContracts = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Shared/Interfaces/IPlayerService.cs").FullName);
        var serverContracts = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/App/Leaderboard/LeaderboardStateContracts.cs").FullName);
        var rankingPolicy = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Leaderboard/LeaderboardRankingPolicy.cs").FullName);
        var playerService = File.ReadAllText(
            FindRepositoryFile("samples/Game.Unity.Agar/Server/Hotfix/Players/PlayerService.cs").FullName);

        Assert.Contains("class LeaderboardEntry", sharedContracts, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaderboardEntrySnapshot", serverContracts, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaderboardEntrySnapshot", rankingPolicy, StringComparison.Ordinal);
        Assert.Contains("List<LeaderboardEntry> Entries", serverContracts, StringComparison.Ordinal);
        Assert.Contains("Entries = snapshot.Entries", playerService, StringComparison.Ordinal);
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
            File.Exists(Path.Combine(sampleRoot, "Server", "Hotfix", "Matchmaking", "MatchmakingStatusSnapshot.cs")),
            "Server/Hotfix/Matchmaking/MatchmakingStatusSnapshot.cs should not exist.");

        var serverContracts = File.ReadAllText(Path.Combine(
            sampleRoot,
            "Server",
            "App",
            "Matchmaking",
            "MatchmakingActorContracts.cs"));
        var serverStateMatch = Regex.Match(
            serverContracts,
            @"public\s+sealed\s+class\s+MatchmakingState\s*\{(?<body>.*?)^\s*\}",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(serverStateMatch.Success, "Expected server-side MatchmakingState in Server/App/Matchmaking/MatchmakingActorContracts.cs.");

        var serverStateProperties = Regex.Matches(
                serverStateMatch.Groups["body"].Value,
                @"public\s+(?<type>[^{;]+?)\s+(?<name>\w+)\s*\{\s*get;\s*set;\s*\}",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(new[] { "PendingTickets" }, serverStateProperties);

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

    private static string ReadPartialType(string anchorRelativePath, string searchPattern)
    {
        var directory = FindRepositoryFile(anchorRelativePath).Directory!;
        return string.Join(
            Environment.NewLine,
            directory
                .GetFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .Select(file => File.ReadAllText(file.FullName)));
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
