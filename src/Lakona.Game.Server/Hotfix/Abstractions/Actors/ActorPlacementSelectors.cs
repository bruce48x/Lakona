using System.Text;

namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Provides built-in selectors for initial Actor placement.
/// </summary>
public static class ActorPlacementSelectors
{
    /// <summary>
    /// Selects the candidate with the highest rendezvous-hash score for the supplied Actor key.
    /// </summary>
    /// <typeparam name="TKey">The Actor key type.</typeparam>
    /// <param name="context">The placement candidates and Actor key.</param>
    /// <returns>The selected host candidate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The Actor key cannot be converted to text.</exception>
    /// <exception cref="InvalidOperationException">No host candidate is available.</exception>
    public static ActorHostCandidate Rendezvous<TKey>(ActorPlacementContext<TKey> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SelectRendezvous(
            context.Candidates,
            context.Key,
            static candidate => candidate.NodeId,
            "No actor host candidates are available.",
            "Actor placement key cannot convert to text.");
    }

    internal static StartupActorCandidate StartupRendezvous<TKey>(StartupActorSelectionContext<TKey> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SelectRendezvous(
            context.Candidates,
            context.Key,
            static candidate => candidate.NodeId,
            "No Startup Actor candidates are available.",
            "Startup Actor selection key cannot convert to text.");
    }

    private static TCandidate SelectRendezvous<TKey, TCandidate>(
        IReadOnlyList<TCandidate> candidates,
        TKey keyValue,
        Func<TCandidate, string> getNodeId,
        string noCandidatesMessage,
        string invalidKeyMessage)
        where TCandidate : class
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(noCandidatesMessage);
        }

        var key = keyValue?.ToString()
            ?? throw new ArgumentException(invalidKeyMessage);
        TCandidate? selected = null;
        ulong selectedScore = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var nodeId = getNodeId(candidate);
            var score = Hash(key, nodeId);
            if (selected is null
                || score > selectedScore
                || score == selectedScore
                && string.CompareOrdinal(nodeId, getNodeId(selected)) < 0)
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
