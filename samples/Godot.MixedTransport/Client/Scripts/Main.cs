#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Rpc.Generated;
using Shared.Interfaces;
using Lakona.Rpc.Client;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.Tcp;

public partial class Main : Control
{
	private const string MoveLeftAction = "move_left";
	private const string MoveRightAction = "move_right";
	private const string MoveUpAction = "move_up";
	private const string MoveDownAction = "move_down";

	[Export] private string _host = "127.0.0.1";
	[Export] private int _tcpPort = 20000;
	[Export] private string _defaultAccount = "slime";

	private CenterContainer? _loginRoot;
	private LineEdit? _accountInput;
	private LineEdit? _passwordInput;
	private Button? _loginButton;
	private Label? _statusLabel;
	private Label? _hudLabel;

	private RpcClient? _authClient;
	private RpcClient? _battleClient;
	private CancellationTokenSource? _battleLoopCts;
	private readonly object _snapshotGate = new();
	private Vector2 _desiredDirection;
	private WorldSnapshotReply? _pendingSnapshot;
	private WorldSnapshotReply? _snapshot;
	private bool _isLoggingIn;

	public override void _Ready()
	{
		EnsureMovementBindings();
		SetProcess(true);
		BuildUi();
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		ApplyPendingSnapshot();
		UpdateDesiredDirection();
		if (_snapshot is not null)
		{
			UpdateHud();
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("101820"));

		if (_snapshot is null || _snapshot.Code != 0)
			return;

		var self = FindSelf(_snapshot);
		if (self is null)
			return;

		DrawArena(self, _snapshot);
	}

	public override void _ExitTree()
	{
		_ = ShutdownAsync();
	}

	private void BuildUi()
	{
		_loginRoot = new CenterContainer();
		_loginRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(_loginRoot);

		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(420, 240)
		};
		_loginRoot.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		panel.AddChild(margin);

		var stack = new VBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center
		};
		stack.AddThemeConstantOverride("separation", 12);
		margin.AddChild(stack);

		stack.AddChild(new Label
		{
			Text = "TCP Login -> KCP Battle",
			HorizontalAlignment = HorizontalAlignment.Center
		});

		stack.AddChild(new Label
		{
			Text = "Login over TCP, receive a server-assigned conv, then enter the arena over KCP.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center
		});

		_accountInput = new LineEdit
		{
			PlaceholderText = "Account",
			Text = _defaultAccount
		};
		stack.AddChild(_accountInput);

		_passwordInput = new LineEdit
		{
			PlaceholderText = "Password",
			Secret = true,
			Text = "demo"
		};
		stack.AddChild(_passwordInput);

		_loginButton = new Button
		{
			Text = "Login And Enter Battle"
		};
		_loginButton.Pressed += BeginLogin;
		stack.AddChild(_loginButton);

		_statusLabel = new Label
		{
			Text = "Ready.",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		stack.AddChild(_statusLabel);

		_hudLabel = new Label
		{
			Position = new Vector2(16, 16),
			Visible = false
		};
		AddChild(_hudLabel);
	}

	private async void BeginLogin()
	{
		if (_isLoggingIn)
			return;

		_isLoggingIn = true;
		SetStatus("Logging in over TCP...");
		SetLoginEnabled(false);

		try
		{
			var account = _accountInput?.Text?.Trim();
			if (string.IsNullOrWhiteSpace(account))
				account = "guest";

			await DisposeAuthClientAsync();
			_authClient = new RpcClient(new RpcClientOptions(
				new TcpTransport(_host, _tcpPort),
				new MemoryPackRpcSerializer()));

			await _authClient.ConnectAsync();
			var loginReply = await _authClient.Api.Shared.Auth.LoginAsync(new LoginRequest
			{
				Account = account,
				Password = _passwordInput?.Text ?? string.Empty
			});

			if (loginReply.Code != 0)
				throw new InvalidOperationException(loginReply.Message);

			SetStatus($"TCP login ok. conv={loginReply.Conv}, switching to KCP...");
			var battleHost = string.IsNullOrWhiteSpace(loginReply.KcpHost) ? _host : loginReply.KcpHost;
			await ConnectBattleAsync(battleHost, loginReply.KcpPort, loginReply.Conv, loginReply.Token);
		}
		catch (Exception ex)
		{
			SetStatus($"Login failed: {ex.Message}");
			await ShutdownBattleAsync();
			SetLoginEnabled(true);
		}
		finally
		{
			_isLoggingIn = false;
		}
	}

	private async Task ConnectBattleAsync(string host, int kcpPort, uint conv, string token)
	{
		await ShutdownBattleAsync();

		var callbacks = new RpcClient.RpcNotificationBindings();
		callbacks.Add(new BattleCallbacks(this));
		_battleClient = new RpcClient(new RpcClientOptions(
			new KcpTransport(host, kcpPort, conv),
			new MemoryPackRpcSerializer()), callbacks);

		await _battleClient.ConnectAsync();
		var joinReply = await _battleClient.Api.Shared.Battle.JoinAsync(new BattleJoinRequest
		{
			Token = token
		});

		if (joinReply.Code != 0)
			throw new InvalidOperationException(joinReply.Message);

		_loginRoot!.Visible = false;
		_hudLabel!.Visible = true;
		SetStatus(joinReply.Message);

		_battleLoopCts = new CancellationTokenSource();
		_ = RunInputLoopAsync(_battleLoopCts.Token);
	}

	private async Task RunInputLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _battleClient is not null)
		{
			try
			{
				await _battleClient.Api.Shared.Battle.UpdateInputAsync(new PlayerInputRequest
				{
					DirectionX = _desiredDirection.X,
					DirectionY = _desiredDirection.Y
				});
			}
			catch (Exception ex)
			{
				SetStatus($"Battle input loop stopped: {ex.Message}");
				await ShutdownBattleAsync();
				SetLoginEnabled(true);
				return;
			}

			await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
		}
	}

	private void UpdateDesiredDirection()
	{
		var direction = new Vector2(
			Input.GetActionStrength(MoveRightAction) - Input.GetActionStrength(MoveLeftAction),
			Input.GetActionStrength(MoveDownAction) - Input.GetActionStrength(MoveUpAction));

		if (direction.LengthSquared() > 1f)
			direction = direction.Normalized();

		_desiredDirection = direction;
	}

	private static void EnsureMovementBindings()
	{
		EnsureActionKey(MoveLeftAction, Key.A);
		EnsureActionKey(MoveLeftAction, Key.Left);
		EnsureActionKey(MoveRightAction, Key.D);
		EnsureActionKey(MoveRightAction, Key.Right);
		EnsureActionKey(MoveUpAction, Key.W);
		EnsureActionKey(MoveUpAction, Key.Up);
		EnsureActionKey(MoveDownAction, Key.S);
		EnsureActionKey(MoveDownAction, Key.Down);
	}

	private static void EnsureActionKey(string actionName, Key key)
	{
		if (!InputMap.HasAction(actionName))
			InputMap.AddAction(actionName);

		foreach (var existingEvent in InputMap.ActionGetEvents(actionName))
		{
			if (existingEvent is InputEventKey existingKey && existingKey.Keycode == key)
				return;
		}

		InputMap.ActionAddEvent(actionName, new InputEventKey
		{
			Keycode = key
		});
	}

	private void ApplyPendingSnapshot()
	{
		lock (_snapshotGate)
		{
			if (_pendingSnapshot is null)
				return;

			_snapshot = _pendingSnapshot;
			_pendingSnapshot = null;
		}
	}

	private void ReceiveSnapshot(WorldSnapshotReply snapshot)
	{
		lock (_snapshotGate)
		{
			_pendingSnapshot = snapshot;
		}
	}

	private void DrawArena(PlayerBlobState self, WorldSnapshotReply snapshot)
	{
		var viewportCenter = Size / 2f;
		var camera = new Vector2(self.X, self.Y);

		for (var x = 0f; x <= snapshot.WorldWidth; x += 160f)
		{
			var start = WorldToScreen(new Vector2(x, 0f), camera, viewportCenter);
			var end = WorldToScreen(new Vector2(x, snapshot.WorldHeight), camera, viewportCenter);
			DrawLine(start, end, new Color("1f3847"), 1f);
		}

		for (var y = 0f; y <= snapshot.WorldHeight; y += 160f)
		{
			var start = WorldToScreen(new Vector2(0f, y), camera, viewportCenter);
			var end = WorldToScreen(new Vector2(snapshot.WorldWidth, y), camera, viewportCenter);
			DrawLine(start, end, new Color("1f3847"), 1f);
		}

		var topLeft = WorldToScreen(Vector2.Zero, camera, viewportCenter);
		var bottomRight = WorldToScreen(new Vector2(snapshot.WorldWidth, snapshot.WorldHeight), camera, viewportCenter);
		DrawRect(new Rect2(topLeft, bottomRight - topLeft), new Color(0, 0, 0, 0), false, 3f, true);

		foreach (var food in snapshot.Foods)
		{
			DrawCircle(
				WorldToScreen(new Vector2(food.X, food.Y), camera, viewportCenter),
				food.Radius,
				new Color("4dd599"));
		}

		foreach (var player in snapshot.Players)
		{
			var screen = WorldToScreen(new Vector2(player.X, player.Y), camera, viewportCenter);
			var color = player.IsSelf ? new Color("7ee081") : new Color("f6b042");
			DrawCircle(screen, player.Radius, color);
			DrawString(
				ThemeDB.FallbackFont,
				screen + new Vector2(-player.Radius, -player.Radius - 8f),
				$"{player.Name} {MathF.Round(player.Mass)}",
				HorizontalAlignment.Left,
				-1,
				14,
				Colors.White);
		}
	}

	private void UpdateHud()
	{
		if (_hudLabel is null || _snapshot is null)
			return;

		var self = FindSelf(_snapshot);
		if (self is null)
			return;

		_hudLabel.Text = $"Mass: {MathF.Round(self.Mass)}  Radius: {MathF.Round(self.Radius)}  Tick: {_snapshot.Tick}\nUse WASD or arrow keys to move.";
	}

	private static PlayerBlobState? FindSelf(WorldSnapshotReply snapshot)
	{
		foreach (var player in snapshot.Players)
		{
			if (player.IsSelf)
				return player;
		}

		return null;
	}

	private static Vector2 WorldToScreen(Vector2 world, Vector2 camera, Vector2 viewportCenter)
	{
		return world - camera + viewportCenter;
	}

	private void SetStatus(string message)
	{
		if (_statusLabel is not null)
			_statusLabel.Text = message;
	}

	private void SetLoginEnabled(bool enabled)
	{
		if (_loginButton is not null)
			_loginButton.Disabled = !enabled;

		if (_accountInput is not null)
			_accountInput.Editable = enabled;

		if (_passwordInput is not null)
			_passwordInput.Editable = enabled;
	}

	private async Task ShutdownBattleAsync()
	{
		_battleLoopCts?.Cancel();
		_battleLoopCts?.Dispose();
		_battleLoopCts = null;

		if (_battleClient is not null)
		{
			await _battleClient.DisposeAsync();
			_battleClient = null;
		}

		lock (_snapshotGate)
		{
			_pendingSnapshot = null;
		}

		_snapshot = null;
		if (_loginRoot is not null)
			_loginRoot.Visible = true;
		if (_hudLabel is not null)
			_hudLabel.Visible = false;
	}

	private async Task DisposeAuthClientAsync()
	{
		if (_authClient is not null)
		{
			await _authClient.DisposeAsync();
			_authClient = null;
		}
	}

	private async Task ShutdownAsync()
	{
		await ShutdownBattleAsync();
		await DisposeAuthClientAsync();
	}

	private sealed class BattleCallbacks : RpcClient.BattleNotificationsBase
	{
		private readonly Main _owner;

		public BattleCallbacks(Main owner)
		{
			_owner = owner;
		}

		public override void OnSnapshot(WorldSnapshotReply snapshot)
		{
			_owner.ReceiveSnapshot(snapshot);
		}
	}
}
