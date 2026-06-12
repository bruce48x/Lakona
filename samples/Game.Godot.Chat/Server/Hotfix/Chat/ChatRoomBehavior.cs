using System;
using System.Linq;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixBehaviorOf(typeof(ChatRoomActor))]
    internal static class ChatRoomBehavior
    {
        public static ValueTask<LoginReply> LoginAsync(
            this ChatRoomActor self,
            string connectionId,
            string playerName,
            ILoginCallback loginCallback)
        {
            var member = new ChatMember { Name = playerName };
            self.Members[connectionId] = new ChatRoomMember(playerName, loginCallback, null);

            BroadcastLogin(self, callback => callback.OnUserJoined(member));

            return new ValueTask<LoginReply>(new LoginReply
            {
                Members = self.Members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                RecentMessages = self.RecentMessages.ToList()
            });
        }

        public static void BindChatCallback(this ChatRoomActor self, string connectionId, IChatCallback chatCallback)
        {
            if (self.Members.TryGetValue(connectionId, out var entry))
            {
                self.Members[connectionId] = entry with { ChatCallback = chatCallback };
            }
        }

        public static ValueTask SendAsync(this ChatRoomActor self, string connectionId, string text)
        {
            if (!self.Members.TryGetValue(connectionId, out var entry))
            {
                return default;
            }

            var msg = new ChatMessage
            {
                SenderName = entry.Name,
                Text = text,
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

        public static ValueTask LeaveAsync(this ChatRoomActor self, string connectionId)
        {
            if (!self.Members.Remove(connectionId, out var entry))
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
                catch
                {
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
                catch
                {
                }
            }
        }
    }
}
