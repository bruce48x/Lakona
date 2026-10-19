using System.Text.Json;
using FrameworkBenchmark.Lakona.Contracts;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FrameworkBenchmark.Lakona.Server;

public sealed class BenchmarkReadiness(
    IHostApplicationLifetime lifetime,
    IClusterMembership membership,
    IActorPlacementService placement,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var node = configuration["Lakona:Node:Id"]!;
        if (node == "frontdoor-1")
        {
            while (!new[] { "worker-1", "worker-2", "frontdoor-1" }.All(id =>
                membership.Current.Members.Any(member => member.Reference.Node.Value == id && member.State == ClusterMemberState.Active)))
            {
                await Task.Delay(50, timeout.Token);
            }
            for (var index = 0; index < BenchmarkRouting.TargetCount; index++)
            {
                var key = BenchmarkRouting.TargetKey(index);
                var result = await placement.PlaceAsync<BenchmarkActor, string>(key, ActorPlacementCreateMode.Ensure, timeout.Token);
                if (result.Owner.Value != BenchmarkRouting.Owner(key))
                    throw new InvalidOperationException($"Actor '{key}' was placed on unexpected owner '{result.Owner}'.");
            }
        }
        var endpoints = node == "frontdoor-1"
            ? new Dictionary<string, string> { ["client"] = $"ws://127.0.0.1:{configuration["Benchmark:ClientPort"]}/ws" }
            : new Dictionary<string, string>();
        await using var output = new StreamWriter(Console.OpenStandardOutput(), leaveOpen: true) { AutoFlush = true };
        await output.WriteLineAsync(JsonSerializer.Serialize(new { @event = "ready", role = node == "frontdoor-1" ? "frontdoor" : node, nodeId = node, endpoints }));
    }
}
