using System;
using System.Collections.Generic;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Rpc.Core;

namespace Server.App.Hotfix
{
    public interface IChatRuntimeService
    {
        [RpcMethod(ChatRuntimeMethodIds.SessionExpired)]
        ValueTask SessionExpiredAsync(ChatSessionExpiredRequest request);
    }

    internal sealed class ChatRuntimeRequiredServiceContracts : IHotfixRequiredServiceContracts
    {
        public IReadOnlyList<Type> ServiceContracts { get; } =
        [
            typeof(IChatRuntimeService)
        ];
    }

    public static class ChatRuntimeMethodIds
    {
        public const int SessionExpired = 1;
    }

    public sealed class ChatSessionExpiredRequest
    {
        public string ConnectionId { get; set; } = "";
    }
}
