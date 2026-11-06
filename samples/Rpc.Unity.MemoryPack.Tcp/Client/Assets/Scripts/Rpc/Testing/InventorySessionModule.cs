#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Client.Generated;

namespace Rpc.Testing
{
    internal sealed class InventorySessionModule : RpcClient.InventoryNotificationsBase
    {
        private readonly IConnectionSessionHost _host;
        private InventoryServiceClient? _service;

        public InventorySessionModule(IConnectionSessionHost host)
        {
            _host = host;
        }

        public void Attach(InventoryServiceClient service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public async ValueTask<int> LoadAsync(CancellationToken cancellationToken)
        {
            var revision = await GetService().GetRevisionAsync(new RevisionRequest(), cancellationToken);
            return revision.Revision;
        }

        public async ValueTask<int> PollAsync(CancellationToken cancellationToken)
        {
            var revision = await GetService().IncrRevision(new RevisionRequest(), cancellationToken);
            if (!_host.IsStopping)
                _host.UpdateInventoryRevision(revision.Revision);
            return revision.Revision;
        }

        public override void OnInventoryNotify(InventoryNotify notify)
        {
            _host.HandleNotification("inventory", notify.Message);
        }

        private InventoryServiceClient GetService()
        {
            return _service ?? throw new InvalidOperationException("Inventory service is not attached.");
        }
    }
}
