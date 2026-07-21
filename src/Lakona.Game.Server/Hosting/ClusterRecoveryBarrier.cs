namespace Lakona.Game.Server.Hosting;

internal sealed class ClusterRecoveryBarrier
{
    private readonly IReadOnlyList<IClusterRecoveryParticipant> participants;

    public ClusterRecoveryBarrier(IEnumerable<IClusterRecoveryParticipant> participants)
    {
        if (participants is null)
        {
            throw new ArgumentNullException(nameof(participants));
        }

        var ordered = participants
            .Select((participant, index) => new OrderedParticipant(
                participant ?? throw new ArgumentException(
                    "Cluster recovery participant cannot be null.",
                    nameof(participants)),
                index))
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(ordered[i].Participant.Name))
            {
                throw new ArgumentException(
                    "Cluster recovery participant name is required.",
                    nameof(participants));
            }
        }

        ordered.Sort(static (left, right) =>
        {
            var order = left.Participant.Order.CompareTo(right.Participant.Order);
            return order != 0 ? order : left.RegistrationIndex.CompareTo(right.RegistrationIndex);
        });
        this.participants = ordered.Select(item => item.Participant).ToArray();
    }

    public async ValueTask RecoverAsync(
        ClusterRecoveryContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        for (var i = 0; i < participants.Count; i++)
        {
            var participant = participants[i];
            try
            {
                await participant.RecoverAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ClusterRecoveryException(participant.Name, exception);
            }
        }
    }

    private sealed class OrderedParticipant
    {
        public OrderedParticipant(IClusterRecoveryParticipant participant, int registrationIndex)
        {
            Participant = participant;
            RegistrationIndex = registrationIndex;
        }

        public IClusterRecoveryParticipant Participant { get; }

        public int RegistrationIndex { get; }
    }
}
