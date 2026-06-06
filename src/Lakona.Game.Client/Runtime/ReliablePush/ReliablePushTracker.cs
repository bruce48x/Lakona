using System;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class ReliablePushTracker
    {
        public long LastAppliedSequence { get; private set; }

        public ReliablePushApplyDecision Decide(long sequence)
        {
            if (sequence <= 0)
            {
                return new ReliablePushApplyDecision(sequence, shouldApply: true, shouldAck: false, isDuplicate: false);
            }

            if (sequence <= LastAppliedSequence)
            {
                return new ReliablePushApplyDecision(sequence, shouldApply: false, shouldAck: true, isDuplicate: true);
            }

            return new ReliablePushApplyDecision(sequence, shouldApply: true, shouldAck: true, isDuplicate: false);
        }

        public void MarkApplied(long sequence)
        {
            if (sequence <= 0)
            {
                return;
            }

            LastAppliedSequence = Math.Max(LastAppliedSequence, sequence);
        }

        public void Reset()
        {
            LastAppliedSequence = 0;
        }
    }
}
