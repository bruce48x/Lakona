namespace Lakona.Game.Client.ReliablePush
{
    public readonly struct ReliablePushApplyDecision
    {
        public ReliablePushApplyDecision(long sequence, bool shouldApply, bool shouldAck, bool isDuplicate)
        {
            Sequence = sequence;
            ShouldApply = shouldApply;
            ShouldAck = shouldAck;
            IsDuplicate = isDuplicate;
        }

        public long Sequence { get; }

        public bool ShouldApply { get; }

        public bool ShouldAck { get; }

        public bool IsDuplicate { get; }
    }
}
