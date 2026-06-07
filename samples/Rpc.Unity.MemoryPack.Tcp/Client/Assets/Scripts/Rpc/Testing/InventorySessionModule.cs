#nullable enable

using System;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Rpc.Generated;
using Lakona.Rpc.Client;

namespace Rpc.Testing
{
    internal sealed class InventorySessionModule : RpcClient.InventoryNotificationsBase
    {
        private readonly IConnectionSessionHost _host;
        private IInventoryService? _service;

        public InventorySessionModule(IConnectionSessionHost host)
        {
            _host = host;
        }

        public void Attach(IInventoryService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public async ValueTask<int> LoadAsync()
        {
            var revision = await GetService().GetRevisionAsync(new RevisionRequest());
            return revision.Revision;
        }

        public async ValueTask<int> PollAsync()
        {
            var revision = await GetService().IncrRevision(new RevisionRequest());
            if (!_host.IsStopping)
                _host.UpdateInventoryRevision(revision.Revision);
            return revision.Revision;
        }

        public override void OnInventoryNotify(InventoryNotify notify)
        {
            if (_host.IsStopping)
                return;

            _host.UpdateLastMessage(notify.Message);
            _host.AppendLog($"Session[{_host.Index}] inventory push: {notify.Message}");
        }

        private IInventoryService GetService()
        {
            return _service ?? throw new InvalidOperationException("Inventory service is not attached.");
        }
    }
}
