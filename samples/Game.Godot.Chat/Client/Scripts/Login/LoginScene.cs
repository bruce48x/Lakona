using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Shared.Contracts.Chat;
using Client.Chat;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.WebSocket;

namespace Client.Login
{
    public partial class LoginScene : Control
    {
        [Export] private string _serverHost = "127.0.0.1";
        [Export] private int _serverPort = 20000;
        [Export] private string _serverPath = "/ws";

        private readonly CancellationTokenSource _cts = new();
        private LineEdit? _nameField;
        private Button? _connectButton;
        private Label? _statusLabel;
        private bool _isConnecting;

        public override void _Ready()
        {
            _nameField = GetNode<LineEdit>("%NameField");
            _nameField.TextSubmitted += _ => OnConnectPressed();

            _connectButton = GetNode<Button>("%ConnectButton");
            _connectButton.Pressed += OnConnectPressed;

            _statusLabel = GetNode<Label>("%StatusLabel");

            SetBusy(false);
        }

        private async void OnConnectPressed()
        {
            if (_isConnecting)
            {
                return;
            }

            var name = _nameField?.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("Enter a name.");
                _nameField?.GrabFocus();
                return;
            }

            SetBusy(true);
            SetStatus("Connecting...");

            var client = new LoginClient(CreateRpcClientOptions());
            client.OnDisconnected += () => GD.Print("Disconnected from server.");

            try
            {
                await client.ConnectAsync(_cts.Token);
                var reply = await client.LoginAsync(name);
                var session = GetNode<ChatSession>("/root/ChatSession");
                session.LoginClient = client;
                session.LoginReply = reply;
                GetTree().ChangeSceneToFile("res://Chat.tscn");
            }
            catch (Exception ex)
            {
                SetStatus($"Connection failed: {ex.Message}");
                await client.DisposeAsync();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = text;
            }
        }

        private void SetBusy(bool isBusy)
        {
            _isConnecting = isBusy;
            if (_connectButton != null)
            {
                _connectButton.Disabled = isBusy;
                _connectButton.Text = isBusy ? "CONNECTING..." : "CONNECT";
            }
        }

        private RpcClientOptions CreateRpcClientOptions()
        {
            return new RpcClientOptions(
                new WsTransport($"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}"),
                new MemoryPackRpcSerializer())
                .UseSecurity(ConfigureTransportSecurity);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private static void ConfigureTransportSecurity(TransportSecurityConfig security)
        {
            security.EnableCompression = false;
            security.CompressionThresholdBytes = 1024;
            security.EnableEncryption = false;
            security.EncryptionKeyBase64 = null;
        }

        public override void _ExitTree()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
