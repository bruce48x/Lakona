#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Unity.MMO.Client.Rpc;
using Shared.Interfaces;
using UnityEngine;

namespace Game.Unity.MMO.Client
{
    public sealed class MmoGame : MonoBehaviour, IWorldCallback
    {
        private readonly object _snapshotLock = new object();
        private readonly Dictionary<string, EntityView> _views = new Dictionary<string, EntityView>(StringComparer.Ordinal);
        private MmoNetworkSession? _network;
        private WorldSnapshot? _pendingSnapshot;
        private string _characterName = "Hero";
        private string _characterId = "";
        private string _status = "Start the server, then enter the world.";
        private long _sequence;
        private float _nextCommandAt;
        private CancellationTokenSource? _lifetime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<MmoGame>() == null) new GameObject("MMO Sample").AddComponent<MmoGame>();
        }

        private void Awake()
        {
            _lifetime = new CancellationTokenSource();
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 12f;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                camera.backgroundColor = new Color(0.04f, 0.08f, 0.12f);
            }
        }

        private async void OnDestroy()
        {
            _lifetime?.Cancel();
            if (_network is not null) await _network.DisposeAsync();
            _lifetime?.Dispose();
        }

        private void Update()
        {
            ApplyPendingSnapshot();
            foreach (var view in _views.Values)
            {
                view.GameObject.transform.position = Vector3.Lerp(view.GameObject.transform.position, view.Target, 14f * Time.deltaTime);
            }

            if (_network?.IsConnected != true || Time.unscaledTime < _nextCommandAt) return;
            _nextCommandAt = Time.unscaledTime + WorldProtocol.TickIntervalSeconds;
            var moveX = Input.GetAxisRaw("Horizontal");
            var moveY = Input.GetAxisRaw("Vertical");
            var target = Input.GetKey(KeyCode.Space) ? FindNearestMonster() : "";
            _ = SendCommandAsync(moveX, moveY, target);
        }

        public void OnWorldSnapshot(WorldSnapshot snapshot)
        {
            lock (_snapshotLock) _pendingSnapshot = snapshot;
        }

        private async Task ConnectAsync()
        {
            if (_network is not null) await _network.DisposeAsync();
            _network = new MmoNetworkSession();
            _status = "Connecting through one WebSocket...";
            try
            {
                var reply = await _network.ConnectAndEnterAsync("127.0.0.1", 20100, "/ws", _characterName, this, _lifetime!.Token);
                _status = reply.Message;
                if (reply.Code == 0)
                {
                    _characterId = reply.CharacterId;
                    OnWorldSnapshot(reply.Snapshot);
                }
            }
            catch (Exception exception)
            {
                _status = exception.GetBaseException().Message;
            }
        }

        private async Task SendCommandAsync(float moveX, float moveY, string targetId)
        {
            try
            {
                await _network!.SubmitAsync(new CharacterCommand
                {
                    Sequence = ++_sequence,
                    MoveX = moveX,
                    MoveY = moveY,
                    AttackTargetId = targetId
                });
            }
            catch (Exception exception) { _status = exception.GetBaseException().Message; }
        }

        private void ApplyPendingSnapshot()
        {
            WorldSnapshot? snapshot;
            lock (_snapshotLock) { snapshot = _pendingSnapshot; _pendingSnapshot = null; }
            if (snapshot == null) return;

            var visible = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in snapshot.Entities)
            {
                visible.Add(entity.EntityId);
                if (!_views.TryGetValue(entity.EntityId, out var view))
                {
                    var primitive = GameObject.CreatePrimitive(entity.Kind == EntityKind.Monster ? PrimitiveType.Cube : PrimitiveType.Sphere);
                    primitive.name = $"{entity.Kind}: {entity.Name}";
                    primitive.transform.localScale = entity.Kind == EntityKind.Monster ? Vector3.one * 0.9f : Vector3.one;
                    primitive.GetComponent<Renderer>().material.color = entity.EntityId == _characterId
                        ? new Color(0.1f, 0.8f, 1f)
                        : entity.Kind == EntityKind.Monster ? new Color(1f, 0.3f, 0.25f) : new Color(0.35f, 1f, 0.45f);
                    view = new EntityView(primitive);
                    _views.Add(entity.EntityId, view);
                }
                view.Target = new Vector3(entity.X, entity.Y, 0f);
                view.Health = entity.Health;
                view.MaxHealth = entity.MaxHealth;
                view.GameObject.SetActive(entity.Alive);
            }

            foreach (var stale in _views.Keys.Where(id => !visible.Contains(id)).ToArray())
            {
                Destroy(_views[stale].GameObject);
                _views.Remove(stale);
            }
            _status = $"Zone {snapshot.ZoneId} · server tick {snapshot.ServerTick} · AOI entities {snapshot.Entities.Count}";
        }

        private string FindNearestMonster()
        {
            if (!_views.TryGetValue(_characterId, out var self)) return "";
            return _views
                .Where(pair => pair.Key.StartsWith("monster-", StringComparison.Ordinal))
                .OrderBy(pair => (pair.Value.Target - self.Target).sqrMagnitude)
                .Select(pair => pair.Key)
                .FirstOrDefault() ?? "";
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18, 18, 460, 160), GUI.skin.box);
            GUILayout.Label("Lakona MMO · server-authoritative state sync");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Character", GUILayout.Width(80));
            _characterName = GUILayout.TextField(_characterName, 24, GUILayout.Width(180));
            GUI.enabled = _network?.IsConnected != true;
            if (GUILayout.Button("Enter World", GUILayout.Width(120))) _ = ConnectAsync();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(_status);
            GUILayout.Label("WASD / arrows: move intent · hold Space: attack nearest monster");
            GUILayout.Label("The client interpolates snapshots; it never calculates authoritative movement or damage.");
            GUILayout.EndArea();
        }

        private sealed class EntityView
        {
            public EntityView(GameObject gameObject) { GameObject = gameObject; Target = gameObject.transform.position; }
            public GameObject GameObject { get; }
            public Vector3 Target { get; set; }
            public int Health { get; set; }
            public int MaxHealth { get; set; }
        }
    }
}
