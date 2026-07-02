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
        public static ValueTask<LoginReply> LoginAsync(
            this ChatRoomActor self,
            ChatRoomLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var member = new ChatMember { Name = request.PlayerName };
            self.Members[request.ConnectionId] = new ChatRoomMember(request.PlayerName, request.LoginCallback, null);

            BroadcastLogin(self, callback => callback.OnUserJoined(member));

            return new ValueTask<LoginReply>(new LoginReply
            {
                Members = self.Members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                RecentMessages = self.RecentMessages.ToList()
            });
        }

        public static ValueTask BindChatAsync(
            this ChatRoomActor self,
            ChatRoomBindRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (self.Members.TryGetValue(request.ConnectionId, out var entry))
            {
                self.Members[request.ConnectionId] = entry with { ChatCallback = request.ChatCallback };
            }

            return default;
        }

        public static ValueTask SendAsync(
            this ChatRoomActor self,
            ChatRoomSendRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (!self.Members.TryGetValue(request.ConnectionId, out var entry))
            {
                return default;
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

            BroadcastChat(self, callback => callback.OnMessageReceived(msg));
            return default;
        }

        public static ValueTask LeaveAsync(
            this ChatRoomActor self,
            ChatRoomLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (!self.Members.Remove(request.ConnectionId, out var entry))
            {
                return default;
            }

            BroadcastLogin(self, callback => callback.OnUserLeft(new ChatUserLeft { Name = entry.Name }));
            return default;
        }

        private static void BroadcastLogin(ChatRoomActor self, Action<ILoginCallback> action)
        {
            foreach (var entry in self.Members.Values)
            {
                try
                {
                    action(entry.LoginCallback);
                }
                catch (Exception)
                {
                    // Callback exceptions are ignored so one stale client does not prevent other clients from receiving room events.
                }
            }
        }

        private static void BroadcastChat(ChatRoomActor self, Action<IChatCallback> action)
        {
            foreach (var entry in self.Members.Values)
            {
                if (entry.ChatCallback is null)
                {
                    continue;
                }

                try
                {
                    action(entry.ChatCallback);
                }
                catch (Exception)
                {
                    // Callback exceptions are ignored so one stale client does not prevent other clients from receiving room events.
                }
            }
        }
    }
}
