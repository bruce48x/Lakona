using System;
using System.Collections.Generic;

namespace Lakona.Rpc.Core
{
    /// <summary>Controls typed RPC body snapshots independently of the Trace log level.</summary>
    public class RpcPayloadLoggingOptions
    {
        /// <summary>Member and dictionary key names always masked in snapshots.</summary>
        public static IReadOnlyList<string> DefaultRedactedMembers { get; } = Array.AsReadOnly(new[]
        {
            "Password", "LoginToken", "Token", "AccessToken", "RefreshToken", "ResumeTicket", "Secret", "Payload"
        });

        /// <summary>Enables bounded, redacted payload snapshots. Defaults to false.</summary>
        public bool IncludePayload { get; init; }

        /// <summary>Maximum snapshot length, bounded to 64 through 65536 characters.</summary>
        public int MaxPayloadCharacters { get; init; } = 4096;

        /// <summary>Case-insensitive member names to mask in addition to the built-in names.</summary>
        public IReadOnlyList<string> RedactedMembers { get; init; } = DefaultRedactedMembers;
    }
}
