using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class StartupActorDescriptorCatalog
{
    private StartupActorDescriptor[] _descriptors;

    public StartupActorDescriptorCatalog(IEnumerable<StartupActorDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        _descriptors = Copy(descriptors);
    }

    public IReadOnlyList<StartupActorDescriptor> Snapshot() => Volatile.Read(ref _descriptors);

    public void Replace(IEnumerable<StartupActorDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        Volatile.Write(ref _descriptors, Copy(descriptors));
    }

    private static StartupActorDescriptor[] Copy(IEnumerable<StartupActorDescriptor> descriptors) =>
        descriptors.OrderBy(static descriptor => descriptor.Actor, StringComparer.Ordinal).ToArray();
}
