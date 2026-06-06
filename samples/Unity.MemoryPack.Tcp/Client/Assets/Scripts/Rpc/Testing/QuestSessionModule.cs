#nullable enable

using System;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Rpc.Generated;
using Lakona.Rpc.Client;

namespace Rpc.Testing
{
    internal sealed class QuestSessionModule : RpcClient.QuestNotificationsBase
    {
        private readonly IConnectionSessionHost _host;
        private IQuestService? _service;

        public QuestSessionModule(IConnectionSessionHost host)
        {
            _host = host;
        }

        public void Attach(IQuestService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public async ValueTask<int> LoadAsync()
        {
            var progress = await GetService().GetProgressAsync(new ProgressRequest());
            return progress.Progress;
        }

        public async ValueTask<int> PollAsync()
        {
            var progress = await GetService().IncrProgress(new ProgressRequest());
            if (!_host.IsStopping)
                _host.UpdateQuestProgress(progress.Progress);
            return progress.Progress;
        }

        public override void OnQuestNotify(QuestNotify notify)
        {
            if (_host.IsStopping)
                return;

            _host.UpdateLastMessage(notify.Message);
            _host.AppendLog($"Session[{_host.Index}] quest push: {notify.Message}");
        }

        private IQuestService GetService()
        {
            return _service ?? throw new InvalidOperationException("Quest service is not attached.");
        }
    }
}
