namespace Lakona.Game.Server.Actors;

public sealed class ActorHostDescriptorCatalog
{
    private readonly Dictionary<string, ActorHostDescriptor> _byActor;

    public ActorHostDescriptorCatalog(IEnumerable<ActorHostDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        _byActor = descriptors.ToDictionary(
            static descriptor => descriptor.Actor,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string actor, out ActorHostDescriptor descriptor)
    {
        return _byActor.TryGetValue(actor, out descriptor!);
    }
}
