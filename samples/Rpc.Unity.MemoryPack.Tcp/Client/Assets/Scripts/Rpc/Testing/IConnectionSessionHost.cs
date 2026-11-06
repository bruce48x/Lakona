#nullable enable

namespace Rpc.Testing
{
    internal interface IConnectionSessionHost
    {
        bool IsStopping { get; }

        void HandleNotification(string module, string message);
        void UpdatePlayerStep(int value);
        void UpdateInventoryRevision(int value);
        void UpdateQuestProgress(int value);
    }
}
