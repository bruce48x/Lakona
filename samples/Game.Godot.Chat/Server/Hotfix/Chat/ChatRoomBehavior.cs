using System;
using System.Linq;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixBehaviorOf(typeof(ChatRoomActor))]
    internal static partial class ChatRoomBehavior
    {
        public static ValueTask<ChatRoomLoginResult> LoginAsync(
            this ChatRoomActor self,
            ChatRoomLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var member = new ChatMember { Name = request.PlayerName };
            self.Members[request.Session] = new ChatRoomMember(request.PlayerName);

            return new ValueTask<ChatRoomLoginResult>(new ChatRoomLoginResult
            {
                Reply = new LoginReply
                {
                    Members = self.Members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                    RecentMessages = self.RecentMessages.ToList()
                },
                Recipients = self.Members.Keys.ToArray()
            });
        }

        public static ValueTask<ChatRoomSendResult?> SendAsync(
            this ChatRoomActor self,
            ChatRoomSendRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (!self.Members.TryGetValue(request.Session, out var entry))
            {
                return new ValueTask<ChatRoomSendResult?>((ChatRoomSendResult?)null);
            }

            var msg = new ChatMessage
            {
                SenderName = entry.Name,
                Text = request.Text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            self.RecentMessages.Enqueue(msg);
            while (self.RecentMessages.Count > ChatRoomActor.MaxRecentMessages)
            {
                self.RecentMessages.Dequeue();
            }

            return new ValueTask<ChatRoomSendResult?>(new ChatRoomSendResult
            {
                Message = msg,
                Recipients = self.Members.Keys.ToArray()
            });
        }

        public static ValueTask<ChatRoomLeaveResult?> LeaveAsync(
            this ChatRoomActor self,
            ChatRoomLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (!self.Members.Remove(request.Session, out var entry))
            {
                return new ValueTask<ChatRoomLeaveResult?>((ChatRoomLeaveResult?)null);
            }

            return new ValueTask<ChatRoomLeaveResult?>(new ChatRoomLeaveResult
            {
                Name = entry.Name,
                Recipients = self.Members.Keys.ToArray()
            });
        }
    }
}
