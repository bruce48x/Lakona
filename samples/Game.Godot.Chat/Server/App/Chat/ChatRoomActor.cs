using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.App.Chat
{
    internal sealed class ChatRoomActor : Actor
    {
        private const int MaxRecentMessages = 100;
        private readonly Dictionary<string, (string Name, ILoginCallback LoginCallback, IChatCallback? ChatCallback)> _members = new();
        private readonly Queue<ChatMessage> _recentMessages = new();

        public ValueTask<LoginReply> LoginAsync(string connectionId, string playerName, ILoginCallback loginCallback)
        {
            var member = new ChatMember { Name = playerName };
            _members[connectionId] = (playerName, loginCallback, null);

            BroadcastLogin(callback => callback.OnUserJoined(member));

            return new ValueTask<LoginReply>(new LoginReply
            {
                Members = _members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                RecentMessages = _recentMessages.ToList()
            });
        }

        public void BindChatCallback(string connectionId, IChatCallback chatCallback)
        {
            if (_members.TryGetValue(connectionId, out var entry))
            {
                _members[connectionId] = (entry.Name, entry.LoginCallback, chatCallback);
            }
        }

        public ValueTask SendAsync(string connectionId, string text)
        {
            if (!_members.TryGetValue(connectionId, out var entry))
            {
                return default;
            }

            var msg = new ChatMessage
            {
                SenderName = entry.Name,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _recentMessages.Enqueue(msg);
            while (_recentMessages.Count > MaxRecentMessages)
            {
                _recentMessages.Dequeue();
            }

            BroadcastChat(callback => callback.OnMessageReceived(msg));
            return default;
        }

        public ValueTask LeaveAsync(string connectionId)
        {
            if (!_members.Remove(connectionId, out var entry))
            {
                return default;
            }

            BroadcastLogin(callback => callback.OnUserLeft(new ChatUserLeft { Name = entry.Name }));
            return default;
        }

        private void BroadcastLogin(Action<ILoginCallback> action)
        {
            foreach (var entry in _members.Values)
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

        private void BroadcastChat(Action<IChatCallback> action)
        {
            foreach (var entry in _members.Values)
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
