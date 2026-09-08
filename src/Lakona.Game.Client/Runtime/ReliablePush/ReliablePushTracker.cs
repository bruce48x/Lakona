using System;

namespace Lakona.Game.Client.ReliablePush
{
    public sealed class ReliablePushTracker
    {
        public long LastReceivedSequence { get; private set; }

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

            if (sequence <= LastReceivedSequence)
            {
                return new ReliablePushApplyDecision(sequence, shouldApply: false, shouldAck: true, isDuplicate: true);
            }

            if (sequence != LastReceivedSequence + 1)
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

        public void MarkReceived(long sequence)
        {
            if (sequence <= 0)
            {
                return;
            }

            LastReceivedSequence = Math.Max(LastReceivedSequence, sequence);
        }

        public void Reset()
        {
            LastReceivedSequence = 0;
            IsContinuityLost = false;
        }
    }
}
