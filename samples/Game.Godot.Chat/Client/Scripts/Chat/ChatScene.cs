using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Shared.Contracts.Chat;
using Client.Chat;
using Client.Login;

namespace Client.Chat
{
    public partial class ChatScene : Control
    {
        private readonly CancellationTokenSource _cts = new();
        private LoginClient? _loginClient;
        private ChatClient? _client;
        private LineEdit? _messageField;
        private Button? _sendButton;
        private RichTextLabel? _messageLog;
        private Label? _onlineCount;
        private bool _isSending;

        public override void _Ready()
        {
            _messageField = GetNode<LineEdit>("%MessageField");
            _sendButton = GetNode<Button>("%SendButton");
            _messageLog = GetNode<RichTextLabel>("%MessageLog");
            _onlineCount = GetNode<Label>("%OnlineCount");

            _messageField.TextSubmitted += _ => OnSendPressed();
            _sendButton.Pressed += OnSendPressed;

            var session = GetNode<ChatSession>("/root/ChatSession");
            var loginClient = session.LoginClient;
            var loginReply = session.LoginReply;

            if (loginClient == null)
            {
                AppendSystemMessage("Session expired. Please return to login.");
                SetSendBusy(true);
                return;
            }

            _loginClient = loginClient;
            _client = new ChatClient(loginClient);

            _client.OnMessageReceived += msg => CallDeferred(nameof(AppendMessageDeferred), msg.SenderName, msg.Text);
            loginClient.OnUserJoined += member => CallDeferred(nameof(AppendSystemMessageDeferred), $"{member.Name} joined.");
            loginClient.OnUserLeft += memberName => CallDeferred(nameof(AppendSystemMessageDeferred), $"{memberName} left.");
            loginClient.OnDisconnected += () => CallDeferred(nameof(AppendSystemMessageDeferred), "Disconnected from server.");

            if (loginReply != null)
            {
                AppendSystemMessage($"Connected. {loginReply.Members.Count} online.");
                SetOnlineCount(loginReply.Members.Count);

                foreach (var msg in loginReply.RecentMessages)
                {
                    AppendMessageText(msg.SenderName, msg.Text);
                }
            }

            SetSendBusy(true);
            _ = BindChatAsync();
        }

        private async void OnSendPressed()
        {
            if (_isSending)
            {
                return;
            }

            if (_loginClient == null || !_loginClient.IsConnected)
            {
                AppendSystemMessage("Not connected.");
                return;
            }

            if (_client == null)
            {
                AppendSystemMessage("Chat not ready.");
                return;
            }

            var text = _messageField?.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            SetSendBusy(true);
            try
            {
                await _client.SendAsync(text);
                if (_messageField != null)
                {
                    _messageField.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                AppendSystemMessage($"Send failed: {ex.Message}");
            }
            finally
            {
                SetSendBusy(false);
            }
        }

        public void AppendMessageDeferred(string senderName, string text)
        {
            AppendMessageText(senderName, text);
        }

        public void AppendSystemMessageDeferred(string text)
        {
            AppendSystemMessage(text);
        }

        private void AppendMessageText(string senderName, string text)
        {
            AppendLine($"[{senderName}]: {text}");
        }

        private void AppendSystemMessage(string text)
        {
            AppendLine($"* {text}");
        }

        private void AppendLine(string text)
        {
            _messageLog?.AppendText(text + System.Environment.NewLine);
        }

        private void SetOnlineCount(int count)
        {
            if (_onlineCount != null)
            {
                _onlineCount.Text = $"ONLINE: {count}";
            }
        }

        private void SetSendBusy(bool isBusy)
        {
            _isSending = isBusy;
            if (_sendButton != null)
            {
                _sendButton.Disabled = isBusy;
                _sendButton.Text = isBusy ? "SENDING..." : "SEND";
            }
        }

        private async Task BindChatAsync()
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                await _client.BindAsync();
                CallDeferred(nameof(SetSendBusyDeferred), false);
            }
            catch (Exception ex)
            {
                CallDeferred(nameof(AppendSystemMessageDeferred), $"Chat bind failed: {ex.Message}");
            }
        }

        public void SetSendBusyDeferred(bool isBusy)
        {
            SetSendBusy(isBusy);
        }

        public override void _ExitTree()
        {
            _cts.Cancel();
            var session = GetNode<ChatSession>("/root/ChatSession");
            if (session?.LoginClient is not null)
            {
                _ = session.LoginClient.DisposeAsync();
            }

            _cts.Dispose();
        }
    }
}
