using Lakona.Game.Server.Hosting;

namespace Lakona.Game.Server.Actors;

// The submitting call owns rejection; the accepted callback owns completion.
// Callback completion may race with the submission returning (including inline calls).
internal sealed class ActorWorkAdmission(IDistributedWorkAdmissionGate? gate, DistributedWorkAdmission token)
{
    private int accepted;
    private int completed;

    internal void Accept() => Volatile.Write(ref accepted, 1);

    internal void Complete()
    {
        if (Interlocked.Exchange(ref completed, 1) == 0 && token.IsAdmitted)
            gate!.Exit(token);
    }

    internal void CompleteIfRejected()
    {
        if (Volatile.Read(ref accepted) == 0) Complete();
    }
}
