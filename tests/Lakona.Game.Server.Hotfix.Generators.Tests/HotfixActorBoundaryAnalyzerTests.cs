using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixActorBoundaryAnalyzerTests
{
    [Fact]
    public async Task Reports_invalid_lakona_project_role()
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(
            "public sealed class ProjectMarker { }",
            "ServerAp");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LKNHOTFIX048", diagnostic.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "LakonaProjectRole 'ServerAp' is invalid. Expected 'ServerApp' or 'Hotfix'.",
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ServerApp")]
    [InlineData("serverapp")]
    [InlineData("Hotfix")]
    [InlineData("hotfix")]
    public async Task Allows_supported_lakona_project_roles(string projectRole)
    {
        var diagnostics = await AnalyzerTestHost.RunProjectRoleAsync(
            "public sealed class ProjectMarker { }",
            projectRole);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX048");
    }

    [Fact]
    public async Task Allows_static_direct_hotfix_method_selector()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class Behavior
            {
                public int Run(string request) => request.Length;
            }

            public static class Calls
            {
                public static void Call([HotfixMethodSelector] Func<Behavior, Func<string, int>> selector) { }

                public static void Use()
                {
                    Call(static behavior => behavior.Run);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_capturing_hotfix_method_selector()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class Behavior
            {
                public int Run(string request) => request.Length;
            }

            public static class Calls
            {
                public static void Call([HotfixMethodSelector] Func<Behavior, Func<string, int>> selector) { }

                public static void Use()
                {
                    Call(behavior => behavior.Run);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LKNHOTFIX040", diagnostic.Id);
    }

    [Fact]
    public async Task Reports_indirect_hotfix_method_selector()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class Behavior
            {
                public int Run(string request) => request.Length;
                public int Stop(string request) => 0;
            }

            public static class Calls
            {
                public static bool UseRun { get; set; }
                public static void Call([HotfixMethodSelector] Func<Behavior, Func<string, int>> selector) { }

                public static void Use()
                {
                    Call(static behavior => UseRun ? behavior.Run : behavior.Stop);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LKNHOTFIX040", diagnostic.Id);
    }

    [Fact]
    public async Task Reports_actor_business_methods()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class UserActor : Actor
            {
                public Task<int> LoginAsync(string password)
                {
                    return Task.FromResult(1);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LKNHOTFIX011", diagnostic.Id);
    }

    [Fact]
    public async Task Allows_state_and_lifecycle_hooks()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class RoomActor : Actor
            {
                internal readonly Dictionary<string, string> Members = new();

                protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }

                protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_private_and_static_helpers_on_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;

            public sealed class MatchmakingActor : Actor
            {
                private static int NormalizeRoomSize(int size)
                {
                    return size <= 0 ? 4 : size;
                }

                private int GetScore()
                {
                    return 10;
                }
            }
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("LKNHOTFIX011", diagnostic.Id));
    }

    [Fact]
    public async Task Reports_business_methods_on_generic_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public readonly record struct UserId(string Value);
            public sealed class UserActor : Actor<UserId>
            {
                public Task<int> LoginAsync(string password)
                {
                    return Task.FromResult(1);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("LKNHOTFIX011", diagnostic.Id);
    }

    [Fact]
    public async Task Reports_non_actor_hotfix_behavior_target()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class ArenaSimulation
            {
            }

            [HotfixBehaviorOf(typeof(ArenaSimulation))]
            public static partial class ArenaSimulationBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX017");
        Assert.Contains("ArenaSimulation", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_non_generic_actor_hotfix_behavior_target()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class LegacyActor : Actor
            {
            }

            [HotfixBehaviorOf(typeof(LegacyActor))]
            public static partial class LegacyBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX017");
        Assert.Contains("LegacyActor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_valid_hotfix_behavior_for_generic_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct UserId(string Value);
            public sealed class UserActor : Actor<UserId>
            {
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_duplicate_hotfix_behavior_for_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct UserId(string Value);
            public sealed class UserActor : Actor<UserId>
            {
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class PlayerSessionBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX018");
        Assert.Contains("UserActor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_duplicate_hotfix_behavior_without_name_mismatch()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct UserId(string Value);
            public sealed class UserActor : Actor<UserId>
            {
            }

            namespace First
            {
                [HotfixBehaviorOf(typeof(UserActor))]
                public sealed partial class UserBehavior
                {
                }
            }

            namespace Second
            {
                [HotfixBehaviorOf(typeof(UserActor))]
                public sealed partial class UserBehavior
                {
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX018");
        Assert.Contains("UserActor", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, item => item.Id == "LKNHOTFIX020");
    }

    [Fact]
    public async Task Reports_behavior_that_is_not_sealed_partial()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct RoomId(string Value);
            public sealed class RoomActor : Actor<RoomId>
            {
            }

            [HotfixBehaviorOf(typeof(RoomActor))]
            public class RoomBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX019");
        Assert.Contains("RoomBehavior", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_behavior_name_that_does_not_match_actor_prefix()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct MatchmakingQueueId(string Value);
            public sealed class MatchmakingActor : Actor<MatchmakingQueueId>
            {
            }

            [HotfixBehaviorOf(typeof(MatchmakingActor))]
            public sealed partial class MatchmakingQueueBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX020");
        Assert.Contains("MatchmakingBehavior", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_matching_behavior_to_access_non_public_actor_state_across_assemblies()
    {
        var app = CreateActorStateReference();
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Game.App;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixBehaviorOf(typeof(RoomActor))]
            internal sealed partial class RoomBehavior
            {
                public void Update(RoomActor self)
                {
                    self.Members++;
                    self.Name = "room";
                    _ = RoomActor.MaxMembers;
                }
            }
            """, app);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_other_hotfix_type_accessing_non_public_actor_state()
    {
        var app = CreateActorStateReference();
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Game.App;

            internal static class RoomService
            {
                public static void Update(RoomActor room)
                {
                    room.Members++;
                    room.Name = "room";
                    _ = RoomActor.MaxMembers;
                }
            }
            """, app);

        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("LKNHOTFIX031", diagnostic.Id));
    }

    [Fact]
    public async Task Reports_behavior_for_different_actor_accessing_non_public_actor_state()
    {
        var app = CreateActorStateReference();
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Game.App;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixBehaviorOf(typeof(UserActor))]
            internal sealed partial class UserBehavior
            {
                public void Update(RoomActor room)
                {
                    room.Members++;
                }
            }
            """, app);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX031");
        Assert.Contains("RoomActor.Members", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_any_type_to_access_public_actor_state()
    {
        var app = CreateActorStateReference();
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Game.App;

            internal static class RoomService
            {
                public static void Read(RoomActor room)
                {
                    _ = room.PublicScore;
                }
            }
            """, app);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_actor_to_access_its_own_non_public_state()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;

            internal sealed class RoomActor : Actor<string>
            {
                internal int Members;
                internal int Snapshot => Members;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_hotfix_module_to_capture_constructor_dependencies()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }
            public interface IDependency { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService
            {
                private readonly IDependency _dependency;

                public LoginService(IDependency dependency)
                {
                    _dependency = dependency;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_mutable_hotfix_module_field()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService
            {
                private int _counter;
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX032");
        Assert.Contains("_counter", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_readonly_hotfix_module_owned_collection()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Collections.Generic;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService
            {
                private readonly List<int> _cache = new();
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX032");
        Assert.Contains("_cache", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_writable_auto_property_on_hotfix_module()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService
            {
                private int Counter { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX032");
        Assert.Contains("Counter", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_data_carrier_that_is_not_a_generation_module()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            public sealed class TimerArgs
            {
                public int Counter { get; init; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_readonly_primary_constructor_dependency()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }
            public interface IDependency { void Use(); }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService(IDependency dependency)
            {
                public void Run() => dependency.Use();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_mutated_primary_constructor_dependency()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }
            public interface IDependency { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService(IDependency dependency)
            {
                public void Reset()
                {
                    dependency = null!;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "LKNHOTFIX032");
        Assert.Contains("dependency", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_unsealed_hotfix_service_module()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }

            [HotfixService(typeof(ILoginService))]
            public class LoginService
            {
            }
            """);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX035");
    }

    [Fact]
    public async Task Reports_static_hotfix_service_entry_method()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface ILoginService { }

            [HotfixService(typeof(ILoginService))]
            public sealed class LoginService
            {
                public static void Login() { }
            }
            """);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX036");
    }

    [Fact]
    public async Task Reports_concrete_type_without_role_in_hotfix_project()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            public sealed class Payload
            {
                public int Counter { get; set; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, static item => item.Id == "LKNHOTFIX037");
        Assert.Contains("Payload", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_unclassified_abstract_class_in_hotfix_project()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            internal abstract class StatefulBase
            {
                protected int Counter;
            }
            """);

        Assert.Contains(diagnostics, static item => item.Id == "LKNHOTFIX037");
    }

    [Fact]
    public async Task Allows_dependency_only_hotfix_component()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public interface INotifications { }

            [HotfixComponent]
            internal sealed class RoomNotifier
            {
                private readonly INotifications _notifications;

                public RoomNotifier(INotifications notifications)
                {
                    _notifications = notifications;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_owned_data_on_hotfix_component()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixComponent]
            internal sealed class RoomNotifier
            {
                private int _counter;
            }
            """);

        Assert.Contains(diagnostics, static item => item.Id == "LKNHOTFIX032");
    }

    [Fact]
    public async Task Reports_static_state_in_hotfix_utility()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            internal static class MatchmakingPolicy
            {
                public static readonly object Cache = new();
            }
            """);

        var diagnostic = Assert.Single(diagnostics, static item => item.Id == "LKNHOTFIX038");
        Assert.Contains("Cache", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_pure_static_hotfix_utility()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            internal static class MatchmakingPolicy
            {
                public const int Capacity = 16;
                public static int Double(int value) => value * 2;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_invalid_hotfix_component_shape()
    {
        var diagnostics = await AnalyzerTestHost.RunHotfixProjectAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            [HotfixComponent]
            internal class RoomNotifier
            {
            }
            """);

        Assert.Contains(diagnostics, static item => item.Id == "LKNHOTFIX039");
    }

    private static Microsoft.CodeAnalysis.MetadataReference CreateActorStateReference()
    {
        return AnalyzerTestHost.CreateReference("Game.App", """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("AnalyzerTest")]

            namespace Game.App
            {
                internal sealed class RoomActor : Actor<string>
                {
                    internal const int MaxMembers = 100;
                    internal int Members;
                    internal string Name { get; set; } = string.Empty;
                    public int PublicScore { get; set; }
                }

                internal sealed class UserActor : Actor<string>
                {
                }
            }
            """);
    }
}
