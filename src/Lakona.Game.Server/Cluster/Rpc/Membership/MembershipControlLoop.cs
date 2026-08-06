using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipControlLoop
    {
        private readonly IMembershipControlRound round;
        private readonly QuorumProofTracker proofTracker;
        private readonly IClusterAuthorityListener listener;
        private readonly IMembershipControlDelay delay;
        private readonly Random random;
        private readonly MembershipControlLoopOptions options;
        private readonly ILogger logger;
        private bool authorityAvailable;

        public MembershipControlLoop(
            IMembershipControlRound round,
            QuorumProofTracker proofTracker,
            IClusterAuthorityListener listener,
            IMembershipControlDelay delay,
            Random random,
            MembershipControlLoopOptions options,
            ILogger? logger = null)
        {
            this.round = round ?? throw new ArgumentNullException(nameof(round));
            this.proofTracker = proofTracker ?? throw new ArgumentNullException(nameof(proofTracker));
            this.listener = listener ?? throw new ArgumentNullException(nameof(listener));
            this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? NullLogger.Instance;
            this.options.Validate();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var retryCap = options.MinimumRetryDelay;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TimeSpan nextDelay;
                    try
                    {
                        await ExecuteRoundWithAuthorityDeadlineAsync(cancellationToken).ConfigureAwait(false);
                        retryCap = options.MinimumRetryDelay;
                        nextDelay = options.HeartbeatInterval;
                    }
                    catch (TerminalMembershipException exception)
                    {
                        logger.LogError(
                            exception,
                            "Membership authority control loop failed terminally.");
                        throw;
                    }
                    catch (ClusterAuthorityFencingException exception)
                    {
                        logger.LogError(
                            exception,
                            "Membership authority control loop detected local fencing.");
                        throw;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        listener.OnTransientFailure(exception);
                        nextDelay = ApplyFullJitter(retryCap);
                        retryCap = DoubleCapped(retryCap, options.MaximumRetryDelay);
                        logger.LogWarning(
                            exception,
                            "Membership authority round failed transiently; retrying in {RetryDelay}.",
                            nextDelay);
                    }

                    await SynchronizeAuthorityAsync(cancellationToken).ConfigureAwait(false);
                    await DelayUntilNextRoundAsync(nextDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task ExecuteRoundWithAuthorityDeadlineAsync(
            CancellationToken cancellationToken)
        {
            var roundTask = round.ExecuteAsync(cancellationToken).AsTask();
            if (roundTask.IsCompleted)
            {
                await roundTask.ConfigureAwait(false);
                return;
            }

            while (authorityAvailable && !roundTask.IsCompleted)
            {
                var remaining = proofTracker.RemainingAuthority;
                if (remaining <= TimeSpan.Zero)
                {
                    await SynchronizeAuthorityAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }

                using (var deadlineCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var deadlineTask = delay
                        .DelayAsync(remaining, deadlineCancellation.Token)
                        .AsTask();
                    var completed = await Task
                        .WhenAny(roundTask, deadlineTask)
                        .ConfigureAwait(false);
                    if (ReferenceEquals(completed, roundTask))
                    {
                        deadlineCancellation.Cancel();
                        try
                        {
                            await deadlineTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (deadlineCancellation.IsCancellationRequested)
                        {
                        }

                        await roundTask.ConfigureAwait(false);
                        return;
                    }

                    await deadlineTask.ConfigureAwait(false);
                }

                await SynchronizeAuthorityAsync(cancellationToken).ConfigureAwait(false);
            }

            await roundTask.ConfigureAwait(false);
        }

        private async ValueTask DelayUntilNextRoundAsync(
            TimeSpan totalDelay,
            CancellationToken cancellationToken)
        {
            var remainingDelay = totalDelay;
            while (remainingDelay > TimeSpan.Zero)
            {
                var segment = remainingDelay;
                if (authorityAvailable)
                {
                    var authorityRemaining = proofTracker.RemainingAuthority;
                    if (authorityRemaining <= TimeSpan.Zero)
                    {
                        await SynchronizeAuthorityAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (authorityRemaining < segment)
                    {
                        segment = authorityRemaining;
                    }
                }

                await delay.DelayAsync(segment, cancellationToken).ConfigureAwait(false);
                remainingDelay -= segment;
                await SynchronizeAuthorityAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask SynchronizeAuthorityAsync(CancellationToken cancellationToken)
        {
            var current = proofTracker.HasAuthority;
            if (current == authorityAvailable)
            {
                return;
            }

            authorityAvailable = current;
            if (current)
            {
                authorityAvailable = false;
                try
                {
                    await listener.OnAuthorityAvailableAsync(cancellationToken).ConfigureAwait(false);
                    authorityAvailable = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TerminalMembershipException)
                {
                    throw;
                }
                catch (ClusterAuthorityFencingException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    listener.OnTransientFailure(exception);
                }
            }
            else
            {
                await listener.OnAuthorityLostAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private TimeSpan ApplyFullJitter(TimeSpan cap)
        {
            var sample = random.NextDouble();
            if (sample <= 0)
            {
                return TimeSpan.FromTicks(Math.Min(
                    cap.Ticks,
                    TimeSpan.TicksPerMillisecond));
            }

            if (sample >= 1)
            {
                return cap;
            }

            return TimeSpan.FromTicks((long)(cap.Ticks * sample));
        }

        private static TimeSpan DoubleCapped(TimeSpan current, TimeSpan maximum)
        {
            if (current >= maximum || current.Ticks > maximum.Ticks / 2)
            {
                return maximum;
            }

            var doubled = TimeSpan.FromTicks(current.Ticks * 2);
            return doubled < maximum ? doubled : maximum;
        }
    }
}
