namespace Lakona.Game.Server.Hotfix;

public interface IHotfixRuntimePublicationParticipant
{
    ValueTask BeforePublishAsync(HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
    {
        return default;
    }

    ValueTask RollbackPublishAsync(HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
    {
        return default;
    }

    ValueTask AfterPublishAsync(HotfixRuntimeSnapshot previous, HotfixRuntimeSnapshot current, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
