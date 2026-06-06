using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class NodeEndpoint
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public NodeEndpoint(
            string address,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("Node endpoint address is required.", nameof(address));
            }

            Address = address;
            Metadata = metadata is null
                ? EmptyMetadata
                : new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        }

        public string Address { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
