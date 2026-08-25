namespace Lakona.Game.Server.Actors;

internal enum ActorLifecycleOperation
{
    Create,
    Ensure,
    Destroy
}

internal sealed class ActorLifecycleDispatchCatalog
{
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
            actorType);
    }
}

internal sealed record ActorLifecycleDispatch(
    string Actor,
    Type ActorType)
{
    public ValueTask InvokeAsync(
        ActorActivationCatalog activationCatalog,
        ActorLifecycleOperation operation,
        ActorLifecycleTarget target,
        CancellationToken cancellationToken) =>
        operation switch
        {
            ActorLifecycleOperation.Create => activationCatalog.ActivateExactAsync(
                ActorType,
                target,
                ActorPlacementCreateMode.Create,
                cancellationToken),
            ActorLifecycleOperation.Ensure => activationCatalog.ActivateExactAsync(
                ActorType,
                target,
                ActorPlacementCreateMode.Ensure,
                cancellationToken),
            ActorLifecycleOperation.Destroy => activationCatalog.DestroyExactAsync(
                ActorType,
                target,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "Unknown Actor lifecycle operation.")
        };
}
