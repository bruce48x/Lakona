using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureScannerTests
{
    [Fact]
    public void Scanner_discovers_hotfix_feature_declarations()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(BattleRuntimeFeature).Assembly, [
            typeof(BattleRuntimeFeature)
        ]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var feature = Assert.Single(result.Features);
        Assert.Equal("battle-runtime", feature.Name);
        Assert.Equal(typeof(BattleRuntimeFeature), feature.FeatureType);
    }

    [Fact]
    public void Scanner_captures_hotfix_feature_service_declarations()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(ServiceFeature).Assembly, [
            typeof(ServiceFeature)
        ]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var feature = Assert.Single(result.Features);
        Assert.False(feature.Discoverable);
        Assert.Equal("state", feature.Metadata["role"]);
        var descriptor = Assert.Single(feature.Services);
        Assert.Equal(typeof(ISampleHotfixService), descriptor.ServiceType);
        Assert.Equal(typeof(SampleHotfixService), descriptor.ImplementationType);
    }

    [Fact]
    public void Scanner_captures_hotfix_feature_command_declarations()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(CommandFeature).Assembly, [
            typeof(CommandFeature)
        ]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var feature = Assert.Single(result.Features);
        var command = Assert.Single(feature.Commands);
        Assert.Equal(typeof(StartMatchCommand), command.RequestType);
        Assert.Equal(typeof(StartMatchReply), command.ReplyType);
        Assert.Equal(101, command.CommandId);
        Assert.Equal("ExecuteAsync", command.MethodName);
    }

    [Fact]
    public void FeatureCommandCallCarriesRequestAndCommandContext()
    {
        var request = new StartMatchCommand("room-7");
        var commandId = FeatureCommandId.From(17);
        var sourceNode = new NodeId("gateway-1");
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(15);
        using var cts = new CancellationTokenSource();
        using var services = new ServiceCollection().BuildServiceProvider();

        var call = new HotfixFeatureCommandCall<StartMatchCommand>(
            request,
            "commands",
            commandId,
            "correlation-9",
            sourceNode,
            expiresAt,
            cts.Token,
            services);

        Assert.Same(request, call.Request);
        Assert.Equal("commands", call.FeatureName);
        Assert.Equal(commandId, call.CommandId);
        Assert.Equal("correlation-9", call.CorrelationId);
        Assert.Equal(sourceNode, call.SourceNode);
        Assert.Equal(expiresAt, call.ExpiresAt);
        Assert.Equal(cts.Token, call.CancellationToken);
        Assert.Same(services, call.Services);
    }

    [Fact]
    public void Scanner_rejects_feature_without_static_configure()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(MissingConfigureFeature).Assembly, [
            typeof(MissingConfigureFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("public static void Configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_rejects_old_instance_configure()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(OldInstanceConfigureFeature).Assembly, [
            typeof(OldInstanceConfigureFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("must use public static void Configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_rejects_static_configure_with_non_void_return()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(NonVoidConfigureFeature).Assembly, [
            typeof(NonVoidConfigureFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("public static void Configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_rejects_public_configure_overload_even_when_valid_configure_exists()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(MixedConfigureOverloadFeature).Assembly, [
            typeof(MixedConfigureOverloadFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(MixedConfigureOverloadFeature.Configure), StringComparison.Ordinal) &&
            diagnostic.Contains("public static void Configure", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_open_generic_feature()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(GenericFeature<>).Assembly, [
            typeof(GenericFeature<>)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("concrete class", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_accepts_optional_static_start_and_stop_hooks()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(LifecycleFeature).Assembly, [
            typeof(LifecycleFeature)
        ]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var lifecycle = Assert.Single(result.Features).Lifecycle;
        Assert.NotNull(lifecycle.StartMethod);
        Assert.NotNull(lifecycle.StopMethod);
        Assert.Equal(nameof(LifecycleFeature.StartAsync), lifecycle.StartMethod!.Name);
        Assert.Equal(nameof(LifecycleFeature.StopAsync), lifecycle.StopMethod!.Name);
    }

    [Theory]
    [InlineData(typeof(InstanceStartFeature), "StartAsync")]
    [InlineData(typeof(GenericStartFeature), "StartAsync")]
    [InlineData(typeof(WrongStartReturnFeature), "StartAsync")]
    [InlineData(typeof(WrongStartParameterFeature), "StartAsync")]
    [InlineData(typeof(InstanceStopFeature), "StopAsync")]
    [InlineData(typeof(GenericStopFeature), "StopAsync")]
    [InlineData(typeof(WrongStopReturnFeature), "StopAsync")]
    [InlineData(typeof(WrongStopParameterFeature), "StopAsync")]
    public void Scanner_rejects_invalid_start_and_stop_hooks(Type featureType, string hookName)
    {
        var result = HotfixBehaviorScanner.Scan(featureType.Assembly, [featureType]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains(hookName, StringComparison.Ordinal) &&
            diagnostic.Contains("public static ValueTask", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_public_lifecycle_overload_even_when_valid_hook_exists()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(MixedStartOverloadFeature).Assembly, [
            typeof(MixedStartOverloadFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(MixedStartOverloadFeature.StartAsync), StringComparison.Ordinal) &&
            diagnostic.Contains("public static ValueTask", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_public_on_reload_hook()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(OnReloadFeature).Assembly, [
            typeof(OnReloadFeature)
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("OnReload", StringComparison.Ordinal) &&
            diagnostic.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    [HotfixFeature("battle-runtime")]
    private sealed class BattleRuntimeFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }
    }

    private sealed class MatchmakingActor
    {
    }

    [HotfixFeature("state-store")]
    private sealed class ServiceFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
            context.Discoverable = false;
            context.Metadata["role"] = "state";
            context.Services.AddSingleton<ISampleHotfixService, SampleHotfixService>();
        }
    }

    private interface ISampleHotfixService
    {
    }

    private sealed class SampleHotfixService : ISampleHotfixService
    {
    }

    [HotfixFeature("commands")]
    private sealed class CommandFeature : HotfixGameFeature
    {
        public CommandFeature(RequiredRuntimeDependency dependency)
        {
            Dependency = dependency;
        }

        public RequiredRuntimeDependency Dependency { get; }

        public static void Configure(HotfixFeatureContext context)
        {
            context.HandleCommand<StartMatchCommand, StartMatchReply>("ExecuteAsync");
        }

        public ValueTask<StartMatchReply> ExecuteAsync(HotfixFeatureCommandCall<StartMatchCommand> call)
        {
            return new ValueTask<StartMatchReply>(new StartMatchReply(true));
        }
    }

    private sealed class RequiredRuntimeDependency
    {
    }

    [HotfixFeature("missing-configure")]
    private sealed class MissingConfigureFeature : HotfixGameFeature
    {
    }

    [HotfixFeature("old-configure")]
    private sealed class OldInstanceConfigureFeature : HotfixGameFeature
    {
        public void Configure(HotfixFeatureContext context)
        {
        }
    }

    [HotfixFeature("non-void-configure")]
    private sealed class NonVoidConfigureFeature : HotfixGameFeature
    {
        public static string Configure(HotfixFeatureContext context)
        {
            return "configured";
        }
    }

    [HotfixFeature("mixed-configure-overload")]
    private sealed class MixedConfigureOverloadFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
            _ = context;
        }

        public static void Configure(string value)
        {
            _ = value;
        }
    }

    [HotfixFeature("generic")]
    private sealed class GenericFeature<T> : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }
    }

    [HotfixFeature("lifecycle")]
    private sealed class LifecycleFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("instance-start")]
    private sealed class InstanceStartFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("generic-start")]
    private sealed class GenericStartFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StartAsync<T>(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("wrong-start-return")]
    private sealed class WrongStartReturnFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static Task StartAsync(HotfixFeatureStartCall call)
        {
            _ = call;
            return Task.CompletedTask;
        }
    }

    [HotfixFeature("wrong-start-parameter")]
    private sealed class WrongStartParameterFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StartAsync(HotfixFeatureStopCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("instance-stop")]
    private sealed class InstanceStopFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("generic-stop")]
    private sealed class GenericStopFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StopAsync<T>(HotfixFeatureStopCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("wrong-stop-return")]
    private sealed class WrongStopReturnFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static Task StopAsync(HotfixFeatureStopCall call)
        {
            _ = call;
            return Task.CompletedTask;
        }
    }

    [HotfixFeature("wrong-stop-parameter")]
    private sealed class WrongStopParameterFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StopAsync(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("mixed-start-overload")]
    private sealed class MixedStartOverloadFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }

        public static ValueTask StartAsync(HotfixFeatureStopCall call)
        {
            _ = call;
            return default;
        }
    }

    [HotfixFeature("on-reload")]
    private sealed class OnReloadFeature : HotfixGameFeature
    {
        public static void Configure(HotfixFeatureContext context)
        {
        }

        public static ValueTask OnReload(HotfixFeatureStartCall call)
        {
            _ = call;
            return default;
        }
    }

    [FeatureCommand(101)]
    private sealed record StartMatchCommand(string RoomId);

    private sealed record StartMatchReply(bool Accepted);
}
