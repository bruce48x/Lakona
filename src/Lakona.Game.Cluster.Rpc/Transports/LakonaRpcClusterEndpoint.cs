using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcClusterEndpoint
    {
        public ULinkRpcClusterEndpoint(
            string scheme,
            string host,
            int port,
            string path = "")
        {
            if (string.IsNullOrWhiteSpace(scheme))
            {
                throw new ArgumentException("Endpoint scheme is required.", nameof(scheme));
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Endpoint host is required.", nameof(host));
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "Endpoint port must be between 1 and 65535.");
            }

            Scheme = scheme;
            Host = host;
            Port = port;
            Path = path ?? string.Empty;
        }

        public string Scheme { get; }

        public string Host { get; }

        public int Port { get; }

        public string Path { get; }

        public static ULinkRpcClusterEndpoint FromRouteLocation(RouteLocation location)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            return Parse(location.Endpoint.Address);
        }

        public static ULinkRpcClusterEndpoint Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new FormatException("Cluster endpoint address is required.");
            }

            if (address.Contains("://") &&
                Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                if (uri.Port <= 0)
                {
                    throw new FormatException($"Cluster endpoint '{address}' must include a port.");
                }

                return new ULinkRpcClusterEndpoint(
                    NormalizeScheme(uri.Scheme),
                    uri.Host,
                    uri.Port,
                    uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath);
            }

            var separator = address.LastIndexOf(':');
            if (separator <= 0 || separator == address.Length - 1)
            {
                throw new FormatException($"Cluster endpoint '{address}' must be 'host:port' or '<scheme>://host:port'.");
            }

            var host = address.Substring(0, separator);
            var portText = address.Substring(separator + 1);
            if (!int.TryParse(portText, out var port))
            {
                throw new FormatException($"Cluster endpoint '{address}' has an invalid port.");
            }

            return new ULinkRpcClusterEndpoint("tcp", host, port);
        }

        private static string NormalizeScheme(string scheme)
        {
            if (string.Equals(scheme, "ws", StringComparison.OrdinalIgnoreCase))
            {
                return "websocket";
            }

            return scheme.ToLowerInvariant();
        }
    }
}
