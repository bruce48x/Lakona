using Lakona.Game.Abstractions;

namespace Lakona.Game.Client.ReliablePush
{
    public readonly struct ReliablePushProcessResult
    {
        public ReliablePushProcessResult(
            ReliablePushApplyDecision decision,
            ReliablePushAckOutcome? acknowledgement)
        {
            Decision = decision;
            Acknowledgement = acknowledgement;
        }

        public ReliablePushApplyDecision Decision { get; }

        public ReliablePushAckOutcome? Acknowledgement { get; }
    }
}
