using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    /// <summary>
    /// Receives transitions of locally proven quorum authority.
    /// </summary>
    public interface IClusterAuthorityListener
    {
        ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken);

        ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken);

        void OnTransientFailure(Exception exception);
    }

    /// <summary>
    /// Signals that an authority transition cannot be recovered by retrying.
    /// </summary>
    public sealed class ClusterAuthorityFencingException : InvalidOperationException
    {
        public ClusterAuthorityFencingException(string message)
            : base(message)
        {
        }

        public ClusterAuthorityFencingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
