#nullable enable

namespace Rpc.Testing
{
    internal interface IConnectionSessionHost
    {
        int Index { get; }
        bool IsStopping { get; }

        void AppendLog(string message);
        void UpdateLastMessage(string message);
        void UpdatePlayerStep(int value);
        void UpdateInventoryRevision(int value);
        void UpdateQuestProgress(int value);
        void UpdateState(string state);
    }
}
