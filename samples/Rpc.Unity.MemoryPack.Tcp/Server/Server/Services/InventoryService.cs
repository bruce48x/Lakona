using Game.Rpc.Contracts;

namespace Samples.Server.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryNotifications _notifications;
    private int _revision;
    private bool _announced;

    public InventoryService(IInventoryNotifications notifications)
    {
        _notifications = notifications;
    }

    public ValueTask<RevisionReply> GetRevisionAsync(RevisionRequest req)
    {
        if (!_announced)
        {
            _announced = true;
            _notifications.OnInventoryNotify(new InventoryNotify
            {
                Message = "Inventory ready."
            });
        }

        return new ValueTask<RevisionReply>(new RevisionReply
        {
            Revision = _revision
        });
    }

    public ValueTask<RevisionReply> IncrRevision(RevisionRequest req)
    {
        _revision++;
        _notifications.OnInventoryNotify(new InventoryNotify
        {
            Message = $"Inventory revision => {_revision}"
        });
        return new ValueTask<RevisionReply>(new RevisionReply
        {
            Revision = _revision
        });
    }
}
