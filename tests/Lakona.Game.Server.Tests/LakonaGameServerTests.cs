using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class LakonaGameServerTests
{
    [Fact]
    public void Cluster_contracts_are_owned_by_game_server_assembly()
    {
        Assert.Same(typeof(LakonaGameServer).Assembly, typeof(NodeId).Assembly);
    }

    [Fact]
    public void Public_game_server_entry_point_name_is_not_reused_by_runtime_implementation()
    {
        var publicTypesNamedLakonaGameServer = typeof(ILakonaGameServer)
            .Assembly
            .GetExportedTypes()
            .Where(static type => type.Name == "LakonaGameServer")
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Lakona.Game.Server.Hosting.LakonaGameServer"],
            publicTypesNamedLakonaGameServer);
    }

    [Fact]
    public void Hotfix_admin_options_default_debug_watcher_to_off_when_unconfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var method = typeof(Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper).GetMethod(
            "CreateDefaultHotfixAdminOptions",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var options = Assert.IsType<HotfixAdminOptions>(
            method.Invoke(null, [configuration, AppContext.BaseDirectory, "test-build"]));

        Assert.Equal("Off", options.DebugWatcher);
    }

    [Fact]
    public void Default_hotfix_registers_file_watcher_when_debug_watcher_is_on()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaDefaultHotfixWatcherTests",
            Guid.NewGuid().ToString("N"));

        ConfigureDefaultHotfix(services, baseDirectory, "On");

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(HotfixFileWatcherHostedService));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HotfixFileWatcherOptions>>().Value;
        Assert.Equal(Path.Combine(baseDirectory, "hotfix"), options.Directory);
        Assert.Equal("reload.signal", options.Filter);
    }

    [Fact]
    public void Default_hotfix_does_not_register_file_watcher_when_debug_watcher_is_off()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaDefaultHotfixWatcherTests",
            Guid.NewGuid().ToString("N"));

        ConfigureDefaultHotfix(services, baseDirectory, "Off");

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(HotfixFileWatcherHostedService));
    }

    [Fact]
    public void Public_game_server_contract_does_not_expose_reliable_push_protocol_methods()
    {
        var methodNames = typeof(ILakonaGameServer)
            .GetMethods()
            .Select(static method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("PublishReliablePushAsync", methodNames);
        Assert.DoesNotContain("ReplayReliablePushAsync", methodNames);
        Assert.DoesNotContain("AckReliablePushAsync", methodNames);
    }

    [Fact]
    public void Public_game_server_contract_requires_every_operation()
    {
        Assert.All(
            typeof(ILakonaGameServer).GetMethods(),
            static method => Assert.True(
                method.IsAbstract,
                $"{method.Name} must be implemented explicitly."));
    }

    [Fact]
    public void Session_contracts_do_not_expose_callback_storage_methods()
    {
        var gameServerMethods = typeof(ILakonaGameServer).GetMethods();
        var registryMethods = typeof(IGameSessionRegistry).GetMethods();

        Assert.DoesNotContain(gameServerMethods, static method => method.IsGenericMethod);
        Assert.DoesNotContain(gameServerMethods, static method => method.Name is "BindCurrentSessionAsync" or "GetCallbackAsync");
        Assert.DoesNotContain(registryMethods, static method => method.IsGenericMethod);
        Assert.DoesNotContain(
            registryMethods,
            static method => method.Name.Contains("Callback", StringComparison.Ordinal));
    }

    [Fact]
    public void Public_client_notification_api_does_not_expose_reliable_push_delivery_controls()
    {
        var targetType = typeof(ClientNotificationTarget<>);
        var method = Assert.Single(targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));

        Assert.True(targetType.IsValueType);
        Assert.Equal("EnqueueGenerated", method.Name);
        Assert.Equal(typeof(ClientNotificationStatus), method.ReturnType);
        Assert.DoesNotContain(
            method.GetParameters(),
            static parameter => parameter.ParameterType == typeof(CancellationToken));
        Assert.DoesNotContain(
            method.GetParameters(),
            static parameter => parameter.ParameterType.FullName?.Contains(
                "ClientNotificationIntent",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Client_notifications_expose_only_generated_command_creation()
    {
        var assembly = typeof(ILakonaGameServer).Assembly;

        Assert.Null(assembly.GetType("Lakona.Game.Server.Sessions.ClientNotificationRelay"));
        Assert.Null(assembly.GetType("Lakona.Game.Server.Sessions.IClientNotificationRelay"));
        Assert.Empty(typeof(ClientNotificationCommandFactory).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Null(assembly.GetType("Lakona.Game.Cluster.Rpc.ClientNotificationArgument"));
    }

    [Fact]
    public void Server_assembly_does_not_retain_unimplemented_lifecycle_remnants()
    {
        var assembly = typeof(ILakonaGameServer).Assembly;

        Assert.Null(assembly.GetType("Lakona.Game.Server.Sessions.ReconnectStatus"));
        Assert.Null(assembly.GetType("Lakona.Game.Server.LocalAdmin.LakonaLocalAdminRequestTracker"));
    }

    [Fact]
    public void Hotfix_surface_does_not_expose_string_dispatched_state_calls()
    {
        var serverAssembly = typeof(ILakonaGameServer).Assembly;
        var abstractionsAssembly = typeof(HotfixBehaviorOfAttribute).Assembly;
        var stringDispatchMethods = typeof(HotfixDispatch)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name is "CreateKey" or "Invoke" or "InvokeValueTaskAsync")
            .ToArray();

        Assert.Null(serverAssembly.GetType("Lakona.Game.Server.Hotfix.HotfixCall`1"));
        Assert.Null(abstractionsAssembly.GetType(
            "Lakona.Game.Server.Hotfix.Abstractions.HotfixStateAttribute"));
        Assert.Empty(stringDispatchMethods);
    }

    [Fact]
    public void Startup_actor_registration_exposes_only_the_typed_model()
    {
        var legacyRegistration = typeof(ActorHostBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == nameof(ActorHostBuilder.RegisterStartup))
            .Where(static method => !method.IsGenericMethod)
            .ToArray();
        var declarationProperties = typeof(ActorStartupDeclaration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(legacyRegistration);
        Assert.DoesNotContain("IsLegacy", declarationProperties);
        Assert.DoesNotContain("Name", declarationProperties);
        Assert.DoesNotContain("CreatePlan", declarationProperties);
        Assert.Null(typeof(ActorHostBuilder).Assembly.GetType(
            "Lakona.Game.Server.Hotfix.Abstractions.ActorStartupContext"));
        Assert.Null(typeof(ActorHostBuilder).Assembly.GetType(
            "Lakona.Game.Server.Hotfix.Abstractions.ActorStartupPlan"));
        Assert.Null(typeof(ActorHostBuilder).Assembly.GetType(
            "Lakona.Game.Server.Hotfix.Abstractions.ActorStartupInstance"));
    }

    [Fact]
    public async Task Selecting_a_typed_client_notification_target_does_not_allocate()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerSessions();
        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<IClientNotifications>();
        var session = new GameSessionKey("owner", "session");

        _ = notifications.ForSession<ITestNotificationCallback>(session);
        var before = GC.GetAllocatedBytesForCurrentThread();
        ClientNotificationTarget<ITestNotificationCallback> target = default;
        for (var i = 0; i < 10_000; i++)
        {
            target = notifications.ForSession<ITestNotificationCallback>(session);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Public_server_api_does_not_export_reliable_push_control_services()
    {
        var forbiddenTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Lakona.Game.Server.ReliablePush.IReliablePushOutbox",
            "Lakona.Game.Server.ReliablePush.IReliablePushAckService",
            "Lakona.Game.Server.ReliablePush.ReliablePushDeliver",
            "Lakona.Game.Server.ReliablePush.ReliablePushRecord",
            "Lakona.Game.Server.ReliablePush.ReliablePushSessionOwnerKey",
            "Lakona.Game.Server.Sessions.ClientNotificationIntent",
            "Lakona.Game.Server.Sessions.ClientNotificationDelivery",
            "Lakona.Game.Server.Sessions.ClientNotificationPublishResult",
            "Lakona.Game.Server.Sessions.ClientNotificationAcceptance",
            "Lakona.Game.Server.Sessions.ClientNotificationRelay",
            "Lakona.Game.Server.Sessions.IClientNotificationRelay"
        };
        var exported = typeof(ILakonaGameServer)
            .Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(forbiddenTypes.Intersect(exported, StringComparer.Ordinal));
    }

    [Fact]
    public void AddServices_CanUseHostConfiguration()
    {
        var hostBuilder = Host.CreateApplicationBuilder([]);
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Marker"] = "configured"
        });
        var serverBuilder = new LakonaGameServerBuilder(hostBuilder);

        serverBuilder.AddServices((services, configuration) =>
            services.AddSingleton(new ConfiguredValue(configuration["Marker"] ?? "")));
        serverBuilder.ApplyToHostBuilder();

        using var provider = hostBuilder.Services.BuildServiceProvider();
        var value = provider.GetRequiredService<ConfiguredValue>();

        Assert.Equal("configured", value.Value);
    }

    [Fact]
    public void Runtime_options_binding_uses_concrete_observability_defaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.CreateRuntimeOptionsForTesting(configuration);

        Assert.False(options.Observability.LocalAdmin.EffectiveEnabled);
    }

    [Fact]
    public void Full_startup_runtime_options_apply_user_configuration_before_logging_options_are_resolved()
    {
        var options = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.CreateFullStartupRuntimeOptionsForTesting(
            [],
            server =>
            {
                server.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Lakona:Observability:Logging:MinimumLevel"] = "Warning",
                        ["Lakona:Observability:Logging:Console:Enabled"] = "false"
                    }));
            });

        Assert.Equal(LogLevel.Warning, options.Observability.Logging.MinimumLevel);
        Assert.Equal("Warning", options.Observability.Logging.MinimumLevelRaw);
        Assert.False(options.Observability.Logging.Console.Enabled);
    }

    [Fact]
    public async Task Full_startup_builds_one_authoritative_runtime_graph()
    {
        EnsureDevelopmentHotfixAssemblyExists();
        var capabilityFactoryCalls = 0;

        using var host = await Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.BuildAsyncForTesting(
            [],
            server =>
            {
                server.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Lakona:Endpoints:0:Transport"] = "websocket",
                        ["Lakona:Endpoints:0:Serializer"] = "json",
                        ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                        ["Lakona:Endpoints:0:Port"] = "20000",
                        ["Lakona:Endpoints:0:Path"] = "/ws",
                        ["Lakona:Hotfix:DebugWatcher"] = "On",
                        ["Lakona:Observability:Tracing:Export:Enabled"] = "true"
                    }));
                server.AddServices(services =>
                    services.AddSingleton<ILakonaObservabilityCapability>(_ =>
                    {
                        capabilityFactoryCalls++;
                        return new OpenTelemetryObservabilityCapability();
                    }));
            },
            []);

        Assert.Single(host.Services.GetServices<LakonaGameRuntimeOptions>());
        Assert.Single(host.Services.GetServices<ILakonaObservabilityCapability>());
        Assert.Equal(1, capabilityFactoryCalls);
    }

    [Fact]
    public void RunAsync_source_does_not_handle_cli_health_check_arguments()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Lakona.Game.Server",
            "Hosting",
            "LakonaGameServer.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("--readiness-check", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--liveness-check", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameReadinessProbe.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaGameLivenessProbe.Run", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAsync_facade_delegates_build_and_runtime_lifecycle()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Lakona.Game.Server",
            "Hosting",
            "LakonaGameServer.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LakonaGameServerBootstrapper", source, StringComparison.Ordinal);
        Assert.Contains("LakonaGameServerRunner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadInitialHotfixAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Services", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LakonaModuleDiscovery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("modules.StartAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessContext_CollectsObservabilityCapabilitiesFromUserServices()
    {
        EnsureDevelopmentHotfixAssemblyExists();

        var context = await Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.CreateReadinessContextForTesting(
            [],
            server =>
            {
                server.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Lakona:Endpoints:0:Transport"] = "websocket",
                        ["Lakona:Endpoints:0:Serializer"] = "json",
                        ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                        ["Lakona:Endpoints:0:Port"] = "20000",
                        ["Lakona:Endpoints:0:Path"] = "/ws",
                        ["Lakona:Hotfix:DebugWatcher"] = "On",
                        ["Lakona:Observability:Tracing:Export:Enabled"] = "true"
                    }));
                server.AddServices(services =>
                    services.AddSingleton<ILakonaObservabilityCapability>(
                        new OpenTelemetryObservabilityCapability()));
            });

        Assert.True(context.ObservabilityCapabilities.OpenTelemetryIntegrationRegistered);

        var snapshot = new LakonaGameReadinessEvaluator(
            context.RuntimeOptions,
            context.ClusterOptions,
            context.ObservabilityCapabilities,
            new LakonaHealthReadinessState(context.HotfixAssemblyPath),
            CreateRuntimeValidator()).Evaluate();

        Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "LAKONA134");
    }

    [Fact]
    public async Task Startup_validation_accepts_debug_watcher_current_directory_hotfix_layout()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaStartupValidationTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(baseDirectory, "hotfix");
            Directory.CreateDirectory(hotfixRoot);
            File.WriteAllText(Path.Combine(hotfixRoot, "Server.Hotfix.dll"), "");
            Assert.False(File.Exists(Path.Combine(hotfixRoot, "current.txt")));

            var error = await Record.ExceptionAsync(() =>
                Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.ValidateStartupRuntimeForTesting(
                    [],
                    server =>
                    {
                        server.ConfigureAppConfiguration(configuration =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Lakona:Endpoints:0:Transport"] = "websocket",
                                ["Lakona:Endpoints:0:Serializer"] = "json",
                                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                                ["Lakona:Endpoints:0:Port"] = "20000",
                                ["Lakona:Endpoints:0:Path"] = "/ws",
                                ["Lakona:Hotfix:DebugWatcher"] = "On"
                            }));
                    },
                    baseDirectory));

            Assert.Null(error);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Readiness_context_accepts_default_hotfix_version_pointer_layout()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaReadinessContextTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(baseDirectory, "hotfix");
            var version = "2026.06.30.1";
            var versionDirectory = Path.Combine(hotfixRoot, "versions", version);
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(Path.Combine(hotfixRoot, "current.txt"), version);
            File.WriteAllText(Path.Combine(versionDirectory, "Server.Hotfix.dll"), "");
            Assert.False(File.Exists(Path.Combine(hotfixRoot, "Server.Hotfix.dll")));

            var context = await Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.CreateReadinessContextForTesting(
                [],
                server =>
                {
                    server.ConfigureAppConfiguration(configuration =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Lakona:Endpoints:0:Transport"] = "websocket",
                            ["Lakona:Endpoints:0:Serializer"] = "json",
                            ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                            ["Lakona:Endpoints:0:Port"] = "20000",
                            ["Lakona:Endpoints:0:Path"] = "/ws"
                        }));
                },
                baseDirectory);

            var snapshot = new LakonaGameReadinessEvaluator(
                context.RuntimeOptions,
                context.ClusterOptions,
                context.ObservabilityCapabilities,
                new LakonaHealthReadinessState(context.HotfixAssemblyPath),
                CreateRuntimeValidator()).Evaluate();

            Assert.True(snapshot.Succeeded);
            Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "LAKONA071");
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Readiness_context_rejects_invalid_default_hotfix_pointer_with_stale_debug_dll()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaReadinessContextTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(baseDirectory, "hotfix");
            Directory.CreateDirectory(hotfixRoot);
            File.WriteAllText(Path.Combine(hotfixRoot, "current.txt"), "..");
            File.WriteAllText(Path.Combine(hotfixRoot, "Server.Hotfix.dll"), "");

            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.CreateReadinessContextForTesting(
                    [],
                    server =>
                    {
                        server.ConfigureAppConfiguration(configuration =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Lakona:Endpoints:0:Transport"] = "websocket",
                                ["Lakona:Endpoints:0:Serializer"] = "json",
                                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                                ["Lakona:Endpoints:0:Port"] = "20000",
                                ["Lakona:Endpoints:0:Path"] = "/ws"
                            }));
                    },
                    baseDirectory));

            Assert.Contains("path", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Startup_validation_fails_before_host_build_when_observability_capability_is_missing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "lakona-startup-validation-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixPath = Path.Combine(baseDirectory, "hotfix", "Server.Hotfix.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(hotfixPath)!);
            File.WriteAllText(hotfixPath, "");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.ValidateStartupRuntimeForTesting(
                    [],
                    server =>
                    {
                        server.ConfigureAppConfiguration(configuration =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Lakona:Endpoints:0:Transport"] = "websocket",
                                ["Lakona:Endpoints:0:Serializer"] = "json",
                                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                                ["Lakona:Endpoints:0:Port"] = "20000",
                                ["Lakona:Endpoints:0:Path"] = "/ws",
                                ["Lakona:Hotfix:DebugWatcher"] = "On",
                                ["Lakona:Observability:Tracing:Export:Enabled"] = "true"
                            }));
                    },
                    baseDirectory));

            Assert.Contains("LAKONA134", error.Message, StringComparison.Ordinal);
            Assert.Contains("Trace export is enabled but no OpenTelemetry integration is registered.", error.Message, StringComparison.Ordinal);
            Assert.Contains("1 startup validation error", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Startup_validation_accepts_default_hotfix_version_pointer_layout()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaStartupValidationTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(baseDirectory, "hotfix");
            var version = "2026.06.30.1";
            var versionDirectory = Path.Combine(hotfixRoot, "versions", version);
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(Path.Combine(hotfixRoot, "current.txt"), version);
            File.WriteAllText(Path.Combine(versionDirectory, "Server.Hotfix.dll"), "");
            Assert.False(File.Exists(Path.Combine(hotfixRoot, "Server.Hotfix.dll")));

            var error = await Record.ExceptionAsync(() =>
                Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.ValidateStartupRuntimeForTesting(
                    [],
                    server =>
                    {
                        server.ConfigureAppConfiguration(configuration =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["Lakona:Endpoints:0:Transport"] = "websocket",
                                ["Lakona:Endpoints:0:Serializer"] = "json",
                                ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                                ["Lakona:Endpoints:0:Port"] = "20000",
                                ["Lakona:Endpoints:0:Path"] = "/ws"
                            }));
                    },
                    baseDirectory));

            Assert.Null(error);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Startup_validation_rejects_invalid_default_hotfix_pointer_with_stale_debug_dll()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "LakonaStartupValidationTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(baseDirectory, "hotfix");
            Directory.CreateDirectory(hotfixRoot);
            File.WriteAllText(Path.Combine(hotfixRoot, "current.txt"), "..");
            File.WriteAllText(Path.Combine(hotfixRoot, "Server.Hotfix.dll"), "");

            var error = await Record.ExceptionAsync(() =>
                Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.ValidateStartupRuntimeForTesting(
                    [],
                    server =>
                    {
                        server.ConfigureAppConfiguration(configuration =>
                            configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Lakona:Endpoints:0:Transport"] = "websocket",
                            ["Lakona:Endpoints:0:Serializer"] = "json",
                            ["Lakona:Endpoints:0:Host"] = "127.0.0.1",
                            ["Lakona:Endpoints:0:Port"] = "20000",
                            ["Lakona:Endpoints:0:Path"] = "/ws"
                        }));
                    },
                    baseDirectory));

            Assert.NotNull(error);
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ClusterEndpointConfigurationRegistersClusterRpcServer()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001"
            }
        };
        services.AddSingleton(runtime);
        services.AddSingleton(runtime.ToClusterOptions());
        services.AddSingleton<INodeDirectory, InMemoryNodeDirectory>();

        services.AddLakonaGameClusterEndpoint();

        var configurator = Assert.Single(services, service =>
            service.ServiceType == typeof(IRpcServerConfigurator));
        var instance = Assert.IsType<LakonaClusterRpcServerConfigurator>(
            configurator.ImplementationInstance);
        Assert.Equal("cluster", instance.Transport);
    }

    [Fact]
    public async Task InitialHotfixLoad_Throws_WhenReloadFails()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLogging();
        services.AddSingleton<IHotfixManager>(new FailingHotfixManager());
        await using var provider = services.BuildServiceProvider();

        var hotfix = provider.GetRequiredService<IHotfixManager>();
        var result = await hotfix.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(HotfixReloadStatus.Failed, result.Status);
        Assert.Contains("Server.Hotfix.dll", result.RequestedPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_hotfix_host_assemblies_are_discovered_from_required_contracts()
    {
        var names = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.GetDefaultHotfixHostAssemblyNames(
            [typeof(IConfiguration)]);

        Assert.Contains(typeof(IConfiguration).Assembly.GetName().Name!, names);
        Assert.DoesNotContain("Shared", names);
        Assert.DoesNotContain("Server.App", names);
        Assert.DoesNotContain("State.Contracts", names);
        Assert.Contains(Assembly.GetEntryAssembly()!.GetName().Name!, names);
        Assert.Contains(typeof(ILakonaGameServer).Assembly.GetName().Name!, names);
    }

    [Fact]
    public void AddLakonaGameServer_registers_default_framework_services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "test-node"
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<Lakona.Game.Server.Actors.IActorRuntime>());
        Assert.NotNull(provider.GetService<Lakona.Game.Server.Sessions.IGameSessionRegistry>());
        Assert.NotNull(provider.GetService<ReliablePushOptions>());
    }

    [Fact]
    public void Game_server_does_not_expose_actor_message_recording()
    {
        var assembly = typeof(ILakonaGameServer).Assembly;

        Assert.Null(assembly.GetType("Lakona.Game.Server.Diagnostics.IMessageLogStore"));
        Assert.Null(assembly.GetType("Lakona.Game.Server.Diagnostics.InMemoryMessageLogStore"));
        Assert.Null(assembly.GetType("Lakona.Game.Server.Diagnostics.MessageReplayer"));
    }

    [Fact]
    public void AddLakonaGameServer_registers_actor_hosting_lifecycle_api()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "test-node"
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<ActorHosting>());
    }

    [Fact]
    public void Generated_registration_discovery_registers_explicit_assemblies()
    {
        GeneratedRegistrationProbe.Reset();
        var services = new ServiceCollection().AddTestEndpointRuntimes();

        LakonaGameGeneratedServiceRegistrationDiscovery.RegisterDiscovered(
            services,
            [typeof(GeneratedRegistrationProbe.Registration).Assembly]);

        Assert.True(GeneratedRegistrationProbe.Registered);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GeneratedRegistrationProbe));
    }

    [Fact]
    public async Task AddLakonaGameServer_registers_client_notifications_after_notification_api_exists()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "test-node"
            })
            .Build();

        await using var provider = new ServiceCollection()
            .AddTestEndpointRuntimes()
            .AddLakonaGameServer(configuration)
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IClientNotifications>());
        Assert.Null(typeof(ILakonaGameServer).Assembly.GetType("Lakona.Game.Server.Sessions.IClientSessionIndex"));
        Assert.Null(typeof(ILakonaGameServer).Assembly.GetType("Lakona.Game.Server.Sessions.InMemoryClientSessionIndex"));
    }

    [Fact]
    public void DiscoverHotfixRequiredServiceContracts_finds_provider_types()
    {
        var contracts = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.DiscoverHotfixRequiredServiceContractsForTesting([
            typeof(GeneratedRequiredContractsForTest).Assembly
        ]);

        Assert.Contains(typeof(GeneratedRequiredServiceForTest), contracts);
    }

    [Fact]
    public void DiscoverHotfixRequiredServiceContractProviders_finds_provider_types_for_di_registration()
    {
        var providers = Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.DiscoverHotfixRequiredServiceContractProvidersForTesting([
            typeof(GeneratedRequiredContractsForTest).Assembly
        ]);

        Assert.Contains(typeof(GeneratedRequiredContractsForTest), providers);
    }

    [Fact]
    public async Task Default_hotfix_source_resolves_current_version_pointer()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaDefaultHotfixSourceTests", Guid.NewGuid().ToString("N"));
        try
        {
            var hotfixRoot = Path.Combine(root, "hotfix");
            var versionRoot = Path.Combine(hotfixRoot, "versions", "v2");
            Directory.CreateDirectory(versionRoot);
            var assemblyPath = Path.Combine(versionRoot, "Server.Hotfix.dll");
            await File.WriteAllTextAsync(Path.Combine(hotfixRoot, "current.txt"), "v2", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(assemblyPath, "dll", TestContext.Current.CancellationToken);

            var services = new ServiceCollection().AddTestEndpointRuntimes();
            Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper.ConfigureDefaultHotfixForTesting(
                services,
                root,
                buildTag: "test");
            using var provider = services.BuildServiceProvider();

            var source = Assert.IsType<VersionPointerHotfixAssemblySource>(
                provider.GetRequiredService<IHotfixAssemblySource>());
            var resolved = await source.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(assemblyPath, resolved.AssemblyPath);
            Assert.Equal("v2", resolved.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClientNotificationsPublishesReplaysAndAcknowledgesReliablePush()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var notifications = provider.GetRequiredService<IClientNotifications>();
        var reliablePush = provider.GetRequiredService<IReliablePushRuntime>();
        var callback = new TestCallback();
        provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>()
            .Set("connection-a", true);
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            provider.GetRequiredService<IGameSessionRegistry>(),
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "connection-a",
            callback);

        var publish = notifications
            .ForSession<ITestNotificationCallback>(session)
            .EnqueueGenerated(
                1,
                1,
                nameof(ITestNotificationCallback.NotifyAsync),
                "payload");
        await ((ClientNotificationCommandRouter)provider.GetRequiredService<IClientNotificationCommandRouter>())
            .WaitForIdleAsync(session, TestContext.Current.CancellationToken);

        await reliablePush.ReplayPendingAsync(session, TestContext.Current.CancellationToken);

        var outcome = await reliablePush.AckAsync(
            session,
            session,
            1,
            TestContext.Current.CancellationToken);
        await reliablePush.ReplayPendingAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, publish);
        Assert.Equal([ "payload", "payload" ], callback.Delivered);
        Assert.Equal(ReliablePushAckStatus.Accepted, outcome.Status);
    }

    [Fact]
    public async Task ClientNotificationsPublishesReplayableIntentThroughSessionCallback()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var notifications = provider.GetRequiredService<IClientNotifications>();
        var reliablePush = provider.GetRequiredService<IReliablePushRuntime>();
        var callback = new TestCallback();
        provider.GetRequiredService<GameConnectionDeliveryPolicyRegistry>()
            .Set("connection-a", true);
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            provider.GetRequiredService<IGameSessionRegistry>(),
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "connection-a",
            callback);

        var publish = notifications
            .ForSession<ITestNotificationCallback>(session)
            .EnqueueGenerated(
                1,
                1,
                nameof(ITestNotificationCallback.NotifyAsync),
                "payload");
        await ((ClientNotificationCommandRouter)provider.GetRequiredService<IClientNotificationCommandRouter>())
            .WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        await reliablePush.ReplayPendingAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(ClientNotificationStatus.Accepted, publish);
        Assert.Equal([ "payload", "payload" ], callback.Delivered);
    }

    [Fact]
    public void SessionTerminationNoticeCarriesFixedFrameworkReasonWithoutSessionIdentity()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 4, 1, 2, 3, TimeSpan.Zero);

        var notice = new SessionTerminationNotice(
            SessionTerminationReason.ReplacedByNewLogin,
            "This account logged in elsewhere.",
            issuedAt);

        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, notice.Reason);
        Assert.Equal("This account logged in elsewhere.", notice.Message);
        Assert.Equal(issuedAt, notice.IssuedAt);
    }

    [Fact]
    public async Task TerminateSessionClosesConnectionAndPreservesResumeOutcome()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var closer = new RecordingConnectionCloser();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            provider.GetRequiredService<IGameSessionRegistry>(),
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "connection-a",
            callback);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.ReplacedByNewLogin,
            message: "Duplicate login.",
            cancellationToken: TestContext.Current.CancellationToken);
        var resume = await server.ResumeSessionAsync(
            new GameSessionResumeRequest(session),
            "connection-b",
            TestContext.Current.CancellationToken);

        Assert.NotNull(callback.Notice);
        Assert.Equal(SessionTerminationReason.ReplacedByNewLogin, callback.Notice.Reason);
        Assert.Equal("Duplicate login.", callback.Notice.Message);
        var closed = Assert.Single(closer.Closed);
        Assert.Equal(session, closed.Session);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.Same(callback.Notice, closed.Notice);
        Assert.Equal(SessionResumeStatus.Terminated, resume.Status);
        Assert.Same(callback.Notice, resume.Termination);
    }

    [Fact]
    public async Task TerminateSessionClosesConnectionWhenNotificationTimesOut()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var closer = new RecordingConnectionCloser();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new HangingTerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            provider.GetRequiredService<IGameSessionRegistry>(),
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "connection-a",
            callback);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            options: new SessionTerminationOptions
            {
                NotifyTimeout = TimeSpan.FromMilliseconds(10)
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var closed = Assert.Single(closer.Closed);
        Assert.Equal(session, closed.Session);
        Assert.Equal("connection-a", closed.ConnectionId);
        Assert.NotNull(callback.Notice);
        Assert.Same(callback.Notice, closed.Notice);
    }

    [Fact]
    public async Task TerminateSessionPublishesLifecycleHookWithLiveCallbackAndContainsHandlerFailures()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var closer = new RecordingConnectionCloser();
        var throwingHandler = new ThrowingLifecycleHandler();
        var recordingHandler = new RecordingLifecycleHandler();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddSingleton<IGameSessionLifecycleHandler>(throwingHandler);
        services.AddSingleton<IGameSessionLifecycleHandler>(recordingHandler);
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var callback = new TerminationCallback();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);
        await using var connection = new TestCallbackConnection(
            provider.GetRequiredService<IGameSessionRegistry>(),
            provider.GetRequiredService<GameFrameworkConnectionRegistry>(),
            provider.GetRequiredService<GameSessionCallbackProxyRegistry>(),
            "connection-a",
            callback);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(throwingHandler.WasCalled);
        Assert.Single(recordingHandler.Terminated);
        Assert.Equal(session, recordingHandler.Terminated[0].Session);
        Assert.NotNull(callback.Notice);
        Assert.Single(closer.Closed);
    }

    [Fact]
    public async Task TerminateSessionPublishesLifecycleHookWithoutLiveCallback()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var recordingHandler = new RecordingLifecycleHandler();
        services.AddSingleton<IGameSessionLifecycleHandler>(recordingHandler);
        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = await server.StartSessionAsync(
            "player-a",
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: TestContext.Current.CancellationToken);

        var context = Assert.Single(recordingHandler.Terminated);
        Assert.Equal(session, context.Session);
        Assert.Equal(SessionTerminationReason.Policy, context.Notice.Reason);
    }

    [Fact]
    public async Task Termination_cleanup_ignores_caller_cancellation_after_terminal_commit()
    {
        using var cancellation = new CancellationTokenSource();
        var closer = new CancellationObservingConnectionCloser();
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton<IGameSessionConnectionCloser>(closer);
        services.AddSingleton<IGameSessionLifecycleHandler>(
            new CancelingTerminationHandler(cancellation));
        services.AddSingleton<IGameSessionEstablishedNotifier, NoopGameSessionEstablishedNotifier>();
        services.AddLakonaGameServer();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<ILakonaGameServer>();
        var session = await server.StartSessionAsync(
            "player-a",
            "connection-a",
            TestContext.Current.CancellationToken);

        await server.TerminateSessionAsync(
            session,
            SessionTerminationReason.Policy,
            cancellationToken: cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(closer.CancellationWasRequested);
    }

    private interface ITestNotificationCallback
    {
        ValueTask NotifyAsync(string payload);
    }

    private sealed class TestCallback : ITestNotificationCallback, IRpcNotificationDispatchTarget
    {
        public List<string> Delivered { get; } = new();

        public ValueTask NotifyAsync(string payload)
        {
            Delivered.Add(payload);
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            Delivered.Add(JsonSerializer.Deserialize<string>(payload.Span)!);
            return default;
        }

        public ValueTask DispatchNotificationAsync(
            string methodName,
            object?[] arguments,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void EnsureDevelopmentHotfixAssemblyExists()
    {
        var hotfixPath = Path.Combine(AppContext.BaseDirectory, "hotfix", "Server.Hotfix.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(hotfixPath)!);
        File.WriteAllText(hotfixPath, "");
    }

    private static void ConfigureDefaultHotfix(
        IServiceCollection services,
        string baseDirectory,
        string debugWatcher)
    {
        var method = typeof(Lakona.Game.Server.Hosting.LakonaGameServerBootstrapper).GetMethod(
            "ConfigureDefaultHotfix",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(
            null,
            [
                services,
                baseDirectory,
                new HotfixAdminOptions
                {
                    DebugWatcher = debugWatcher,
                    HotfixRoot = Path.Combine(baseDirectory, "hotfix"),
                    BuildTag = "test"
                }
            ]);
    }

    private static LakonaGameRuntimeValidator CreateRuntimeValidator()
    {
        return new LakonaGameRuntimeValidator(
        [
            new NodeIdentityRule(),
            new EndpointRule(),
            new ClusterEndpointRule(),
            new HotfixSourceRule(),
            new HeartbeatRule(),
            new ActorHostConfigurationRule(),
            new ObservabilityRule()
        ]);
    }

    private sealed record ConfiguredValue(string Value);

    private sealed class GeneratedRegistrationProbe
    {
        public static bool Registered { get; private set; }

        public static void Reset()
        {
            Registered = false;
        }

        public sealed class Registration : ILakonaGameGeneratedServiceRegistration
        {
            public void Register(IServiceCollection services)
            {
                Registered = true;
                services.TryAddSingleton<GeneratedRegistrationProbe>();
            }
        }
    }

    private sealed class GeneratedRequiredContractsForTest :
        Lakona.Game.Server.Hotfix.Abstractions.IHotfixRequiredServiceContracts
    {
        public IReadOnlyList<Type> ServiceContracts { get; } =
        [
            typeof(GeneratedRequiredServiceForTest)
        ];
    }

    private interface GeneratedRequiredServiceForTest
    {
    }

    private sealed class FixedHotfixRuntimeAccessor : IHotfixRuntimeAccessor
    {
        public FixedHotfixRuntimeAccessor(IServiceProvider services)
        {
            Current = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(), services);
        }

        public HotfixRuntimeSnapshot Current { get; }
    }

    private sealed class TerminationCallback : ILakonaGameSessionCallback
    {
        public SessionTerminationNotice? Notice { get; private set; }

        public ValueTask OnSessionTerminatedAsync(
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Notice = notice;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HangingTerminationCallback : ILakonaGameSessionCallback
    {
        public SessionTerminationNotice? Notice { get; private set; }

        public ValueTask OnSessionTerminatedAsync(
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Notice = notice;
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }
    }

    private sealed class RecordingConnectionCloser : IGameSessionConnectionCloser
    {
        public List<(GameSessionKey Session, string ConnectionId, SessionTerminationNotice Notice)> Closed { get; } = new();

        public ValueTask CloseConnectionAsync(
            GameSessionKey session,
            string connectionId,
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            Closed.Add((session, connectionId, notice));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationObservingConnectionCloser : IGameSessionConnectionCloser
    {
        public bool CancellationWasRequested { get; private set; }

        public ValueTask CloseConnectionAsync(
            GameSessionKey session,
            string connectionId,
            SessionTerminationNotice notice,
            CancellationToken cancellationToken = default)
        {
            CancellationWasRequested = cancellationToken.IsCancellationRequested;
            return default;
        }
    }

    private sealed class CancelingTerminationHandler(CancellationTokenSource cancellation)
        : IGameSessionLifecycleHandler
    {
        public ValueTask OnConnectionOpenedAsync(
            GameConnectionContext context,
            CancellationToken cancellationToken = default) => default;

        public ValueTask OnSessionBoundAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default) => default;

        public ValueTask OnSessionDisconnectedAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default) => default;

        public ValueTask OnSessionExpiredAsync(
            GameSessionBindingContext context,
            CancellationToken cancellationToken = default) => default;

        public ValueTask OnSessionTerminatedAsync(
            GameSessionTerminationContext context,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return default;
        }
    }

    private sealed class ThrowingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public bool WasCalled { get; private set; }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class RecordingLifecycleHandler : IGameSessionLifecycleHandler
    {
        public List<GameSessionTerminationContext> Terminated { get; } = [];

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            Terminated.Add(context);
            return default;
        }
    }

    internal sealed class FailingHotfixManager : IHotfixManager
    {
        public event EventHandler<HotfixReloadResult>? Reloaded
        {
            add { }
            remove { }
        }

        public HotfixSnapshot Current => new(
            Version: null,
            SourcePath: "",
            LoadedAtUtc: null,
            DispatchTableVersion: 0,
            Methods: [],
            LastReloadStatus: HotfixReloadStatus.Failed,
            LastFailureMessage: null,
            LastFailureExceptionType: null);

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return ReloadAsync(cancellationToken);
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            Lakona.Game.Server.Hotfix.Loading.IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return ValidateAsync(cancellationToken);
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            var result = new HotfixReloadResult(
                Status: HotfixReloadStatus.Failed,
                Current: Current,
                RequestedVersion: "1",
                RequestedPath: @"C:\app\hotfix\Server.Hotfix.dll",
                Diagnostics: ["missing assembly"],
                ErrorMessage: "Reload failed");
            return ValueTask.FromResult(result);
        }
    }
}
