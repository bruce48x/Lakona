using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal interface IMembershipControlRound
    {
        ValueTask ExecuteAsync(CancellationToken cancellationToken);
    }

    internal interface IMembershipControlDelay
    {
        ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal sealed class TerminalMembershipException : Exception
    {
        public TerminalMembershipException(string message)
            : base(message)
        {
        }

        public TerminalMembershipException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class TimeProviderMembershipControlDelay : IMembershipControlDelay
    {
        private readonly TimeProvider timeProvider;

        public TimeProviderMembershipControlDelay(TimeProvider timeProvider)
        {
            this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
