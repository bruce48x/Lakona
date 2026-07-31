using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class MembershipVoteRequest
    {
        public MembershipVoteRequest(
            NodeReference source,
            NodeReference target,
            long term,
            MembershipViewId view,
            long lastLogIndex,
            long lastLogTerm)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Vote term must be positive.");
            }

            if (lastLogIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lastLogIndex));
            }

            if (lastLogTerm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lastLogTerm));
            }

            Term = term;
            View = view;
            LastLogIndex = lastLogIndex;
            LastLogTerm = lastLogTerm;
        }

        public NodeReference Source { get; }

        public NodeReference Target { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public long LastLogIndex { get; }

        public long LastLogTerm { get; }
    }

    internal enum MembershipVoteRejection
    {
        None = 0,
        IdentityMismatch = 1,
        ViewMismatch = 2,
        CandidateNotVoter = 3,
        StaleTerm = 4,
        CandidateLogBehind = 5,
        AlreadyVoted = 6
    }

    internal sealed class MembershipVoteResponse
    {
        public MembershipVoteResponse(
            long term,
            bool granted,
            MembershipVoteRejection rejection)
        {
            Term = term;
            Granted = granted;
            Rejection = rejection;
        }

        public long Term { get; }

        public bool Granted { get; }

        public MembershipVoteRejection Rejection { get; }
    }

    internal sealed class MembershipVoteReply
    {
        public MembershipVoteReply(
            NodeReference source,
            NodeReference target,
            long term,
            MembershipViewId view,
            bool granted,
            MembershipVoteRejection rejection = MembershipVoteRejection.None)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Vote reply term must be positive.");
            }

            Term = term;
            View = view;
            Granted = granted;
            Rejection = rejection;
        }

        public NodeReference Source { get; }

        public NodeReference Target { get; }

        public long Term { get; }

        public MembershipViewId View { get; }

        public bool Granted { get; }

        public MembershipVoteRejection Rejection { get; }
    }

    internal enum MembershipElectionRole
    {
        Follower = 0,
        Candidate = 1,
        Leader = 2
    }

    internal sealed class MembershipElectionCampaign
    {
        public MembershipElectionCampaign(
            long term,
            IReadOnlyList<MembershipVoteRequest> requests)
        {
            Term = term;
            Requests = new ReadOnlyCollection<MembershipVoteRequest>(
                new List<MembershipVoteRequest>(requests));
        }

        public long Term { get; }

        public IReadOnlyList<MembershipVoteRequest> Requests { get; }
    }

    internal sealed class MembershipElectionState
    {
        private readonly object gate;
        private readonly NodeReference local;
        private readonly IClusterMembership membership;
        private readonly MembershipReplicatedLog log;
        private readonly HashSet<NodeReference> grantedVotes = new HashSet<NodeReference>();
        private NodeReference? votedFor;

        public MembershipElectionState(
            NodeReference local,
            IClusterMembership membership,
            MembershipReplicatedLog log)
        {
            this.local = local ?? throw new ArgumentNullException(nameof(local));
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            gate = this.log.SyncRoot;
        }

        public long CurrentTerm { get; private set; }

        public MembershipElectionRole Role { get; private set; }

        public MembershipElectionCampaign StartElection()
        {
            lock (gate)
            {
                var snapshot = membership.Current;
                if (!snapshot.TryGetMember(local, out var localMember)
                    || localMember is null
                    || !CanCampaign(localMember))
                {
                    throw new InvalidOperationException(
                        "Only the current local voter incarnation can start an election.");
                }

                if (CurrentTerm == long.MaxValue)
                {
                    throw new TerminalMembershipException("Consensus term is exhausted.");
                }

                CurrentTerm++;
                Role = MembershipElectionRole.Candidate;
                votedFor = local;
                grantedVotes.Clear();
                grantedVotes.Add(local);

                var requests = new List<MembershipVoteRequest>();
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    if (!member.IsVoter || member.Reference == local)
                    {
                        continue;
                    }

                    requests.Add(new MembershipVoteRequest(
                        local,
                        member.Reference,
                        CurrentTerm,
                        snapshot.View,
                        log.LastIndex,
                        log.LastTerm));
                }

                if (HasMajority(snapshot))
                {
                    Role = MembershipElectionRole.Leader;
                }

                return new MembershipElectionCampaign(CurrentTerm, requests);
            }
        }

        public bool RecordVote(MembershipVoteReply reply)
        {
            if (reply is null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            lock (gate)
            {
                var snapshot = membership.Current;
                if (reply.Target != local
                    || reply.Source.Cluster != snapshot.Cluster
                    || !snapshot.TryGetMember(reply.Source, out var voter)
                    || voter is null
                    || !voter.IsVoter)
                {
                    return false;
                }

                if (reply.Term > CurrentTerm)
                {
                    CurrentTerm = reply.Term;
                    Role = MembershipElectionRole.Follower;
                    votedFor = null;
                    grantedVotes.Clear();
                    return false;
                }

                if (Role != MembershipElectionRole.Candidate
                    || reply.Term != CurrentTerm
                    || reply.View != snapshot.View
                    || !reply.Granted
                    || !grantedVotes.Add(reply.Source))
                {
                    return false;
                }

                if (!HasMajority(snapshot))
                {
                    return false;
                }

                Role = MembershipElectionRole.Leader;
                return true;
            }
        }

        public bool ObserveLeader(long term)
        {
            if (term <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(term), "Leader term must be positive.");
            }

            lock (gate)
            {
                if (term < CurrentTerm)
                {
                    return false;
                }

                if (term > CurrentTerm)
                {
                    CurrentTerm = term;
                    votedFor = null;
                    grantedVotes.Clear();
                }

                Role = MembershipElectionRole.Follower;
                return true;
            }
        }

        public MembershipVoteResponse RequestVote(MembershipVoteRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (gate)
            {
                var snapshot = membership.Current;
                if (snapshot.Cluster != local.Cluster
                    || request.Source.Cluster != snapshot.Cluster
                    || request.Target != local
                    || !snapshot.TryGetMember(local, out _))
                {
                    return Reject(MembershipVoteRejection.IdentityMismatch);
                }

                if (!snapshot.TryGetMember(request.Source, out var candidate)
                    || candidate is null
                    || !candidate.IsVoter
                    || candidate.State == ClusterMemberState.Draining
                    || candidate.State == ClusterMemberState.Fenced)
                {
                    return Reject(MembershipVoteRejection.CandidateNotVoter);
                }

                if (request.View != snapshot.View)
                {
                    return Reject(MembershipVoteRejection.ViewMismatch);
                }

                if (request.Term < CurrentTerm)
                {
                    return Reject(MembershipVoteRejection.StaleTerm);
                }

                if (request.Term > CurrentTerm)
                {
                    CurrentTerm = request.Term;
                    Role = MembershipElectionRole.Follower;
                    votedFor = null;
                    grantedVotes.Clear();
                }

                if (request.LastLogTerm < log.LastTerm
                    || request.LastLogTerm == log.LastTerm
                    && request.LastLogIndex < log.LastIndex)
                {
                    return Reject(MembershipVoteRejection.CandidateLogBehind);
                }

                if (votedFor is not null && votedFor != request.Source)
                {
                    return Reject(MembershipVoteRejection.AlreadyVoted);
                }

                votedFor = request.Source;
                return new MembershipVoteResponse(
                    CurrentTerm,
                    granted: true,
                    MembershipVoteRejection.None);
            }
        }

        private MembershipVoteResponse Reject(MembershipVoteRejection rejection)
        {
            return new MembershipVoteResponse(CurrentTerm, granted: false, rejection);
        }

        private bool HasMajority(ClusterMembershipSnapshot snapshot)
        {
            var voters = 0;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                if (snapshot.Members[i].IsVoter)
                {
                    voters++;
                }
            }

            return grantedVotes.Count >= voters / 2 + 1;
        }

        private static bool CanCampaign(ClusterMember member)
        {
            return member.IsVoter
                && member.State != ClusterMemberState.Draining
                && member.State != ClusterMemberState.Fenced;
        }
    }
}
