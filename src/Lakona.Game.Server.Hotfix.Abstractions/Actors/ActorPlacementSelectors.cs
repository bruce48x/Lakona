using System.Text;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public static class ActorPlacementSelectors
{
    public static ActorHostCandidate Rendezvous<TKey>(ActorPlacementContext<TKey> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Candidates.Count == 0)
        {
            throw new InvalidOperationException("No actor host candidates are available.");
        }

        var key = context.Key?.ToString()
            ?? throw new ArgumentException("Actor placement key cannot convert to text.");
        ActorHostCandidate? selected = null;
        ulong selectedScore = 0;
        for (var i = 0; i < context.Candidates.Count; i++)
        {
            var candidate = context.Candidates[i];
            var score = Hash(key, candidate.NodeId);
            if (selected is null
                || score > selectedScore
                || score == selectedScore
                && string.CompareOrdinal(candidate.NodeId, selected.NodeId) < 0)
            {
                selected = candidate;
                selectedScore = score;
            }
        }

        return selected!;
    }

    private static ulong Hash(string key, string node)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in new[] { key, "\0", node })
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }
        }

        return hash;
    }
}
