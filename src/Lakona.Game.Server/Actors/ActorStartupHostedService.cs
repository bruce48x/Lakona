using System.Reflection;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorStartupHostedService(
    ActorHosting actorHosting,
    IServiceProvider services,
    LakonaGameRuntimeOptions runtimeOptions,
    ILogger<ActorStartupHostedService>? logger = null) : IHostedService
{
    private static readonly MethodInfo EnsureMethod = typeof(ActorHosting)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(static method => method.Name == nameof(ActorHosting.EnsureAsync) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters() is [{ ParameterType: { } first }, { ParameterType: { } second }] &&
            first == typeof(ActorId) &&
            second == typeof(CancellationToken));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = runtimeOptions.StartupActors;
        if (configured.Count == 0)
        {
            return;
        }

        var hotfixRuntime = services.GetService<IHotfixRuntimeAccessor>();
        if (hotfixRuntime is null)
        {
            throw new InvalidOperationException(
                "Lakona:StartupActors requires a hotfix runtime accessor.");
        }

        using var lease = hotfixRuntime.AcquireCurrent();
        var declarations = lease.Snapshot.ActorStartups.ToDictionary(
            static startup => startup.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var startup in configured)
        {
            if (!declarations.TryGetValue(startup.Name, out var declaration))
            {
                throw new InvalidOperationException(
                    $"Lakona:StartupActors contains unknown actor startup '{startup.Name}'.");
            }

            var plan = declaration.CreatePlan(new ActorStartupContext(startup.Name, startup.Options));
            foreach (var actor in plan.Actors)
            {
                await EnsureStartupActorAsync(startup.Name, actor, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async ValueTask EnsureStartupActorAsync(
        string startupName,
        ActorStartupInstance actor,
        CancellationToken cancellationToken)
    {
        if (!typeof(IActor).IsAssignableFrom(actor.ActorType))
        {
            throw new InvalidOperationException(
                $"Actor startup '{startupName}' returned '{actor.ActorType.FullName}', which does not implement {nameof(IActor)}.");
        }

        var actorId = ToActorId(startupName, actor.ActorId);
        var task = (ValueTask)EnsureMethod
            .MakeGenericMethod(actor.ActorType)
            .Invoke(actorHosting, [actorId, cancellationToken])!;
        await task.ConfigureAwait(false);
        logger?.LogInformation(
            "Startup actor {ActorType} {ActorId} ensured for {StartupActor}.",
            actor.ActorType.FullName,
            actorId.Value,
            startupName);
    }

    private static ActorId ToActorId(string startupName, object value)
    {
        return value switch
        {
            ActorId actorId => actorId,
            string text => ActorId.From(text),
            null => throw new InvalidOperationException(
                $"Actor startup '{startupName}' returned a null actor id."),
            _ => throw new InvalidOperationException(
                $"Actor startup '{startupName}' returned actor id type '{value.GetType().FullName}'. Use ActorId or string actor ids.")
        };
    }
}
