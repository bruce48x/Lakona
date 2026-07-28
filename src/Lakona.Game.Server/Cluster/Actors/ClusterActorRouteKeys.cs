using System;

namespace Lakona.Game.Cluster
{
    public static class ClusterActorRouteKeys
    {
        private const string Prefix = "actor:";

        public static RouteKey ForActor(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                throw new ArgumentException("Actor id is required.", nameof(actorId));
            }

            return new RouteKey(Prefix + actorId);
        }

        public static RouteKey ForReply(NodeId nodeId)
    {
        return new RouteKey($"actor-reply:{nodeId}");
    }

    public static bool TryGetActorId(RouteKey route, out string actorId)
        {
            var value = route.Value;
            if (value.StartsWith(Prefix, StringComparison.Ordinal) &&
                value.Length > Prefix.Length)
            {
                actorId = value.Substring(Prefix.Length);
                return true;
            }

            actorId = string.Empty;
            return false;
        }
    }
}
