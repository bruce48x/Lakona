using System.Reflection;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

internal enum ActorLifecycleOperation
{
    Create,
    Ensure,
    Destroy
}

internal delegate ValueTask ActorLifecycleCreateDelegate(
    ActorHosting hosting,
    ActorId actorId,
    CancellationToken cancellationToken);

internal delegate ValueTask ActorLifecycleDestroyDelegate(
    ActorHosting hosting,
    ActorId actorId,
    NodeReference owner,
    ActorActivationId activationId,
    CancellationToken cancellationToken);

internal sealed class ActorLifecycleDispatchCatalog
{
    private static readonly MethodInfo CreateMethod = FindGeneric(
        nameof(ActorHosting.CreateAsync),
        BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo EnsureMethod = FindGeneric(
        nameof(ActorHosting.EnsureAsync),
        BindingFlags.Public | BindingFlags.Instance);
    private static readonly MethodInfo DestroyMethod = FindGeneric(
        nameof(ActorHosting.DestroyExactAsync),
        BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly IReadOnlyDictionary<string, ActorLifecycleDispatch> dispatches;

    public ActorLifecycleDispatchCatalog(IEnumerable<Type> actorTypes)
    {
        ArgumentNullException.ThrowIfNull(actorTypes);
        dispatches = actorTypes
            .Select(CreateDispatch)
            .ToDictionary(static dispatch => dispatch.Actor, StringComparer.Ordinal);
    }

    public bool TryResolve(string actor, out ActorLifecycleDispatch dispatch) =>
        dispatches.TryGetValue(actor, out dispatch!);

    private static ActorLifecycleDispatch CreateDispatch(Type actorType)
    {
        if (!typeof(IActor).IsAssignableFrom(actorType) || !actorType.IsClass)
        {
            throw new InvalidOperationException(
                $"Actor lifecycle type '{actorType.FullName}' is not an Actor class.");
        }

        return new ActorLifecycleDispatch(
            ActorNameResolver.Resolve(actorType),
            CreateMethod.MakeGenericMethod(actorType)
                .CreateDelegate<ActorLifecycleCreateDelegate>(),
            EnsureMethod.MakeGenericMethod(actorType)
                .CreateDelegate<ActorLifecycleCreateDelegate>(),
            DestroyMethod.MakeGenericMethod(actorType)
                .CreateDelegate<ActorLifecycleDestroyDelegate>());
    }

    private static MethodInfo FindGeneric(string name, BindingFlags flags) =>
        typeof(ActorHosting).GetMethods(flags).Single(candidate =>
            candidate.Name == name && candidate.IsGenericMethodDefinition);
}

internal sealed record ActorLifecycleDispatch(
    string Actor,
    ActorLifecycleCreateDelegate Create,
    ActorLifecycleCreateDelegate Ensure,
    ActorLifecycleDestroyDelegate Destroy)
{
    public ValueTask InvokeAsync(
        ActorHosting hosting,
        ActorLifecycleOperation operation,
        ActorLifecycleTarget target,
        CancellationToken cancellationToken) =>
        operation switch
        {
            ActorLifecycleOperation.Create => Create(
                hosting,
                target.ActorId,
                cancellationToken),
            ActorLifecycleOperation.Ensure => Ensure(
                hosting,
                target.ActorId,
                cancellationToken),
            ActorLifecycleOperation.Destroy => Destroy(
                hosting,
                target.ActorId,
                target.Owner,
                target.ActivationId,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "Unknown Actor lifecycle operation.")
        };
}
