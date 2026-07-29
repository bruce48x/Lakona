using System.Net;
using System.Net.Sockets;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Http;
using Lakona.Game.Server.Management;
using Lakona.Game.Server.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Management;

public sealed class LakonaAspNetCoreHttpHostTests
{
    [Fact]
    public async Task Kestrel_serves_management_health_on_the_declared_listener()
    {
        var port = GetFreePort();
        await using var app = BuildApplication(
            new LakonaGameRuntimeOptions
            {
                Health = new LakonaHealthOptions { Enabled = true, RequireLoopback = true },
                Management = new LakonaManagementOptions
                {
                    Http = new LakonaManagementHttpOptions
                    {
                        Host = "127.0.0.1",
                        Port = port
                    }
                }
            });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = await http.GetStringAsync(
                $"http://127.0.0.1:{port}/_lakona/health/live",
                TestContext.Current.CancellationToken);

            Assert.Contains("\"status\": \"ok\"", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Kestrel_propagates_management_listener_bind_failure()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        try
        {
            var port = ((IPEndPoint)blocker.LocalEndpoint).Port;
            await using var app = BuildApplication(
                new LakonaGameRuntimeOptions
                {
                    Health = new LakonaHealthOptions { Enabled = true },
                    Management = new LakonaManagementOptions
                    {
                        Http = new LakonaManagementHttpOptions
                        {
                            Host = "127.0.0.1",
                            Port = port
                        }
                    }
                });

            await Assert.ThrowsAnyAsync<IOException>(() =>
                app.StartAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public async Task Disabled_http_does_not_open_an_ambient_aspnet_listener()
    {
        await using var app = BuildApplication(new LakonaGameRuntimeOptions());

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Empty(app.Urls);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Application_routes_are_isolated_by_physical_listener_and_use_one_hotfix_lease()
    {
        var operationsPort = GetFreePort();
        var paymentsPort = GetFreePort();
        var managementPort = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions { Enabled = true },
            Management = new LakonaManagementOptions
            {
                Http = new LakonaManagementHttpOptions
                {
                    Host = "127.0.0.1",
                    Port = managementPort
                }
            },
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = operationsPort,
                        Services = ["operations"]
                    },
                    new LakonaHttpListenerOptions
                    {
                        Id = "payments",
                        Host = "127.0.0.1",
                        Port = paymentsPort,
                        Services = ["payment-webhooks"]
                    }
                ]
            }
        };
        var accessor = new RecordingHotfixRuntimeAccessor();
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor("operations", "POST", "/shared"));
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor("payment-webhooks", "POST", "/shared"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var operationsRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{operationsPort}/shared")
            {
                Content = new StringContent("ops")
            };
            operationsRequest.Headers.Host = $"127.0.0.1:{paymentsPort}";
            using var operationsResponse = await http.SendAsync(
                operationsRequest,
                TestContext.Current.CancellationToken);
            using var paymentsResponse = await http.PostAsync(
                $"http://127.0.0.1:{paymentsPort}/shared",
                new StringContent("pay"),
                TestContext.Current.CancellationToken);
            using var managementOnApplication = await http.GetAsync(
                $"http://127.0.0.1:{operationsPort}/_lakona/health/live",
                TestContext.Current.CancellationToken);

            Assert.Equal(
                "OperationsContract:ops",
                await operationsResponse.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken));
            Assert.Equal(
                "PaymentContract:pay",
                await paymentsResponse.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.NotFound, managementOnApplication.StatusCode);
            Assert.Equal(2, accessor.AcquireCount);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Listener_selection_precedes_route_precedence()
    {
        var parameterPort = GetFreePort();
        var literalPort = GetFreePort();
        var runtime = CreateTwoListenerRuntime(parameterPort, literalPort);
        var accessor = new RecordingHotfixRuntimeAccessor();
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "operations",
                    "POST",
                    "/items/{id}"));
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "payment-webhooks",
                    "POST",
                    "/items/new"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync(
                $"http://127.0.0.1:{parameterPort}/items/new",
                new StringContent("parameter-listener"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                "OperationsContract:parameter-listener",
                await response.Content.ReadAsStringAsync(
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Differently_cased_routes_are_isolated_by_listener()
    {
        var upperPort = GetFreePort();
        var lowerPort = GetFreePort();
        var runtime = CreateTwoListenerRuntime(upperPort, lowerPort);
        var accessor = new RecordingHotfixRuntimeAccessor();
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor("operations", "POST", "/Case"));
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor("payment-webhooks", "POST", "/case"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var upper = await http.PostAsync(
                $"http://127.0.0.1:{upperPort}/case",
                new StringContent("upper"),
                TestContext.Current.CancellationToken);
            using var lower = await http.PostAsync(
                $"http://127.0.0.1:{lowerPort}/Case",
                new StringContent("lower"),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                "OperationsContract:upper",
                await upper.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(
                "PaymentContract:lower",
                await lower.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Reserved_management_prefix_requires_a_complete_path_segment()
    {
        var port = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = port,
                        Services = ["operations"]
                    }
                ]
            }
        };
        var accessor = new RecordingHotfixRuntimeAccessor();
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "operations",
                    "POST",
                    "/_lakonax"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/_lakonax",
                new StringContent("allowed"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Cooperative_request_deadline_returns_gateway_timeout()
    {
        var port = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = port,
                        Services = ["operations"],
                        RequestTimeoutSeconds = 1
                    }
                ]
            }
        };
        var accessor = new RecordingHotfixRuntimeAccessor(
            new CancellationAwareHotfixServiceInvoker());
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "operations",
                    "POST",
                    "/deadline"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/deadline",
                new StringContent("wait"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal(1, accessor.AcquireCount);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Closed_distributed_admission_returns_service_unavailable_before_hotfix_dispatch()
    {
        var port = GetFreePort();
        var managementPort = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions { Enabled = true },
            Management = new LakonaManagementOptions
            {
                Http = new LakonaManagementHttpOptions
                {
                    Host = "127.0.0.1",
                    Port = managementPort
                }
            },
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "payments",
                        Host = "127.0.0.1",
                        Port = port,
                        Services = ["payment-webhooks"]
                    }
                ]
            }
        };
        var accessor = new RecordingHotfixRuntimeAccessor();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(
                new DistributedWorkAdmissionGate());
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "payment-webhooks",
                    "POST",
                    "/payments"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/payments",
                new StringContent("pay"),
                TestContext.Current.CancellationToken);
            using var health = await http.GetAsync(
                $"http://127.0.0.1:{managementPort}/_lakona/health/live",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal(0, accessor.AcquireCount);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Request_body_limit_rejects_before_hotfix_dispatch()
    {
        var port = GetFreePort();
        var runtime = new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "payments",
                        Host = "127.0.0.1",
                        Port = port,
                        Services = ["payment-webhooks"],
                        MaximumBodyBytes = 3
                    }
                ]
            }
        };
        var accessor = new RecordingHotfixRuntimeAccessor();
        var gate = new DistributedWorkAdmissionGate();
        gate.Open();
        await using var app = BuildApplication(runtime, services =>
        {
            services.AddSingleton<IHotfixRuntimeAccessor>(accessor);
            services.AddSingleton<IDistributedWorkAdmissionGate>(gate);
            services.AddSingleton(
                new HotfixHttpEndpointDescriptor(
                    "payment-webhooks",
                    "POST",
                    "/payments"));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/payments",
                new StringContent("four"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal(0, accessor.AcquireCount);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Changed_http_manifest_is_rejected_during_candidate_validation()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = GetFreePort(),
                        Services = ["operations"]
                    }
                ]
            }
        };
        var registry = new LakonaApplicationHttpEndpointRegistry(runtime);
        var invoker = new RecordingHotfixServiceInvoker();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var empty = new HotfixRuntimeSnapshot(invoker, services);
        var initial = new HotfixRuntimeSnapshot(
            invoker,
            services,
            [new HotfixHttpEndpointDescriptor("operations", "GET", "/users/{account}")]);
        await using var publication = await registry.PrepareAsync(
            empty,
            initial,
            TestContext.Current.CancellationToken);
        await publication.ActivateAsync(TestContext.Current.CancellationToken);

        var changed = new HotfixRuntimeSnapshot(
            invoker,
            services,
            [new HotfixHttpEndpointDescriptor("operations", "GET", "/accounts/{account}")]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.ValidateAsync(
                initial,
                changed,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            "requires a process restart",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_http_manifest_rejects_unknown_configured_service()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = GetFreePort(),
                        Services = ["missing"]
                    }
                ]
            }
        };
        var registry = new LakonaApplicationHttpEndpointRegistry(runtime);
        var invoker = new RecordingHotfixServiceInvoker();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var empty = new HotfixRuntimeSnapshot(invoker, services);
        var candidate = new HotfixRuntimeSnapshot(
            invoker,
            services,
            [new HotfixHttpEndpointDescriptor("operations", "GET", "/users/{account}")]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.PrepareAsync(
                empty,
                candidate,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            "references unknown service 'missing'",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static WebApplication BuildApplication(
        LakonaGameRuntimeOptions runtime,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var observability = LakonaObservabilityOptions.Defaults();
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(observability);
        builder.Services.AddSingleton<ILakonaHealthHttpRoute>(LakonaHealthHttpRoutes.Live());
        configure?.Invoke(builder.Services);
        LakonaHttpHosting.Configure(builder, runtime, observability);

        var app = builder.Build();
        LakonaHttpHosting.Map(app);
        if (runtime.Http.Listeners.Count != 0)
        {
            var accessor = app.Services.GetRequiredService<IHotfixRuntimeAccessor>();
            var manifest = app.Services
                .GetServices<HotfixHttpEndpointDescriptor>()
                .OrderBy(static endpoint => endpoint.Service, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static endpoint => endpoint.Method, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static endpoint => endpoint.RoutePattern, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var candidate = new HotfixRuntimeSnapshot(
                accessor.Current.Invoker,
                accessor.Current.Services,
                manifest);
            var registry = app.Services
                .GetRequiredService<LakonaApplicationHttpEndpointRegistry>();
            var transaction = registry
                .PrepareAsync(accessor.Current, candidate)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            transaction
                .ActivateAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            transaction
                .CommitAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            transaction
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        return app;
    }

    private static LakonaGameRuntimeOptions CreateTwoListenerRuntime(
        int operationsPort,
        int paymentsPort)
    {
        return new LakonaGameRuntimeOptions
        {
            Http = new LakonaHttpOptions
            {
                Listeners =
                [
                    new LakonaHttpListenerOptions
                    {
                        Id = "operations",
                        Host = "127.0.0.1",
                        Port = operationsPort,
                        Services = ["operations"]
                    },
                    new LakonaHttpListenerOptions
                    {
                        Id = "payments",
                        Host = "127.0.0.1",
                        Port = paymentsPort,
                        Services = ["payment-webhooks"]
                    }
                ]
            }
        };
    }

    private sealed class RecordingHotfixRuntimeAccessor : IHotfixRuntimeAccessor
    {
        private readonly HotfixRuntimeSnapshot snapshot;
        private int acquireCount;

        public RecordingHotfixRuntimeAccessor(IHotfixServiceInvoker? invoker = null)
        {
            snapshot = new HotfixRuntimeSnapshot(
                invoker ?? new RecordingHotfixServiceInvoker(),
                new ServiceCollection().BuildServiceProvider());
        }

        public int AcquireCount => Volatile.Read(ref acquireCount);

        public HotfixRuntimeSnapshot Current => snapshot;

        public HotfixRuntimeSnapshotLease AcquireCurrent()
        {
            Interlocked.Increment(ref acquireCount);
            return snapshot.AcquireLease();
        }
    }

    private sealed class RecordingHotfixServiceInvoker : IHotfixServiceInvoker
    {
        public ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(
            int endpointSlot,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            var call = Assert.IsType<LakonaHttpCall>(arg);
            var body = System.Text.Encoding.UTF8.GetString(call.Request.RawBody.Span);
            var service = endpointSlot switch
            {
                0 => "OperationsContract",
                1 => "PaymentContract",
                _ => throw new InvalidOperationException(
                    $"Unexpected test endpoint slot {endpointSlot}.")
            };
            var response = LakonaHttpResponse.Text($"{service}:{body}");
            return new ValueTask<TResult>((TResult)(object)response);
        }

        public ValueTask InvokeAsync<TContract, TArg>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            var call = Assert.IsType<LakonaHttpCall>(arg);
            var body = System.Text.Encoding.UTF8.GetString(call.Request.RawBody.Span);
            var response = LakonaHttpResponse.Text($"{typeof(TContract).Name}:{body}");
            return new ValueTask<TResult>((TResult)(object)response);
        }

    }

    private sealed class CancellationAwareHotfixServiceInvoker : IHotfixServiceInvoker
    {
        public async ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(
            int endpointSlot,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request deadline was not observed.");
        }

        public ValueTask InvokeAsync<TContract, TArg>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
            int methodId,
            TArg arg,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request deadline was not observed.");
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
}
