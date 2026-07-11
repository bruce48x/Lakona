using System;
using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Client;

public sealed class GameSessionRecoveryRejectedException : InvalidOperationException
{
    public GameSessionRecoveryRejectedException(GameSessionRecoveryStatus status, string? reason)
        : base(reason ?? $"Game session recovery failed: {status}.")
    {
        Status = status;
    }

    public GameSessionRecoveryStatus Status { get; }
}
