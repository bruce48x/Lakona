namespace Lakona.Game.Client.ReliablePush
{
    public readonly struct ReliablePushApplyDecision
    {
        public ReliablePushApplyDecision(
            long sequence,
            bool shouldApply,
            bool shouldAck,
            bool isDuplicate,
            bool isGap = false)
        {
            Sequence = sequence;
            ShouldApply = shouldApply;
            ShouldAck = shouldAck;
            IsDuplicate = isDuplicate;
            IsGap = isGap;
        }

        public long Sequence { get; }

        public bool ShouldApply { get; }

        public bool ShouldAck { get; }

        public bool IsDuplicate { get; }

        public bool IsGap { get; }
    }
}
