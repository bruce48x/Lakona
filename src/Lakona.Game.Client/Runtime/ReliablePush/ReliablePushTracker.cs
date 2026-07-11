using System;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class ReliablePushTracker
    {
        public long LastAppliedSequence { get; private set; }

        public bool IsContinuityLost { get; private set; }

        public ReliablePushApplyDecision Decide(long sequence)
        {
            if (sequence <= 0)
            {
                return new ReliablePushApplyDecision(sequence, shouldApply: true, shouldAck: false, isDuplicate: false);
            }

            if (IsContinuityLost)
            {
                return new ReliablePushApplyDecision(
                    sequence,
                    shouldApply: false,
                    shouldAck: false,
                    isDuplicate: false,
                    isGap: true);
            }

            if (sequence <= LastAppliedSequence)
            {
                return new ReliablePushApplyDecision(sequence, shouldApply: false, shouldAck: true, isDuplicate: true);
            }

            if (sequence != LastAppliedSequence + 1)
            {
                IsContinuityLost = true;
                return new ReliablePushApplyDecision(
                    sequence,
                    shouldApply: false,
                    shouldAck: false,
                    isDuplicate: false,
                    isGap: true);
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
            IsContinuityLost = false;
        }
    }
}
