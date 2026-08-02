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
        private Camera? _worldCamera;
        private Vector3 _cameraVelocity;

        private const float GroundHeight = 0.75f;
        private static readonly Vector3 CameraOffset = new Vector3(0f, 18f, -13f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<MmoGame>() == null) new GameObject("MMO Sample").AddComponent<MmoGame>();
        }

        private void Awake()
        {
            _lifetime = new CancellationTokenSource();
            _worldCamera = Camera.main;
            if (_worldCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                _worldCamera = cameraObject.AddComponent<Camera>();
            }

            ConfigureCamera(_worldCamera);
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
                view.GameObject.transform.rotation = Quaternion.Slerp(view.GameObject.transform.rotation, view.TargetRotation, 14f * Time.deltaTime);
            }

            var target = FindNearestMonsterInRange();
            if (_network?.IsConnected != true || Time.unscaledTime < _nextCommandAt) return;
            _nextCommandAt = Time.unscaledTime + WorldProtocol.TickIntervalSeconds;
            var moveX = Input.GetAxisRaw("Horizontal");
            var moveY = Input.GetAxisRaw("Vertical");
            _ = SendCommandAsync(moveX, moveY, target);
        }

        private void LateUpdate()
        {
            if (_worldCamera == null || !_views.TryGetValue(_characterId, out var self)) return;
            var desired = self.Target + CameraOffset;
            _worldCamera.transform.position = Vector3.SmoothDamp(
                _worldCamera.transform.position,
                desired,
                ref _cameraVelocity,
                0.18f,
                100f,
                Time.unscaledDeltaTime);
            _worldCamera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
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
                    var primitive = GameObject.CreatePrimitive(entity.Kind == EntityKind.Monster ? PrimitiveType.Cube : PrimitiveType.Capsule);
                    primitive.name = $"{entity.Kind}: {entity.Name}";
                    primitive.transform.localScale = entity.Kind == EntityKind.Monster
                        ? new Vector3(1.2f, 1.2f, 1.2f)
                        : new Vector3(0.85f, 1f, 0.85f);
                    primitive.GetComponent<Renderer>().material.color = entity.EntityId == _characterId
                        ? new Color(0.1f, 0.8f, 1f)
                        : entity.Kind == EntityKind.Monster ? new Color(1f, 0.3f, 0.25f) : new Color(0.35f, 1f, 0.45f);
                    view = new EntityView(primitive, entity.Kind == EntityKind.Character ? CreateSword(primitive.transform) : null);
                    _views.Add(entity.EntityId, view);
                }
                view.Target = LogicToWorld(entity.X, entity.Y);
                if (Mathf.Abs(entity.FacingX) > 0.001f || Mathf.Abs(entity.FacingY) > 0.001f)
                {
                    view.TargetRotation = Quaternion.LookRotation(new Vector3(entity.FacingX, 0f, entity.FacingY), Vector3.up);
                }
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

        private string FindNearestMonsterInRange()
        {
            if (!_views.TryGetValue(_characterId, out var self)) return "";
            var target = _views
                .Where(pair => pair.Key.StartsWith("monster-", StringComparison.Ordinal))
                .Where(pair => pair.Value.GameObject.activeSelf)
                .Select(pair => new { pair.Key, Distance = (pair.Value.Target - self.Target).sqrMagnitude })
                .Where(pair => pair.Distance <= WorldProtocol.AttackRange * WorldProtocol.AttackRange)
                .OrderBy(pair => pair.Distance)
                .FirstOrDefault();

            if (self.SwordPivot != null)
            {
                var swing = target == null ? 0f : Mathf.Sin(Time.time * 11f) * 70f;
                self.SwordPivot.localRotation = Quaternion.Euler(0f, swing, 0f);
            }
            return target?.Key ?? "";
        }

        public static Vector3 LogicToWorld(float x, float y) => new Vector3(x, GroundHeight, y);

        private static void ConfigureCamera(Camera camera)
        {
            camera.orthographic = false;
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            camera.transform.position = CameraOffset;
            camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            camera.backgroundColor = new Color(0.3f, 0.48f, 0.68f);
        }

        private static Transform CreateSword(Transform owner)
        {
            var pivot = new GameObject("Sword Pivot").transform;
            pivot.SetParent(owner, false);
            pivot.localPosition = new Vector3(0.7f, 0.2f, 0.25f);

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Auto Attack Sword";
            blade.transform.SetParent(pivot, false);
            blade.transform.localPosition = new Vector3(0f, 0f, 0.9f);
            blade.transform.localScale = new Vector3(0.18f, 0.12f, 1.6f);
            blade.GetComponent<Renderer>().material.color = new Color(0.85f, 0.88f, 0.95f);
            var collider = blade.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return pivot;
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
            GUILayout.Label("WASD / arrows: move intent · auto-attacks the nearest monster in range");
            GUILayout.Label("The client interpolates snapshots; it never calculates authoritative movement or damage.");
            GUILayout.EndArea();
        }

        private sealed class EntityView
        {
            public EntityView(GameObject gameObject, Transform? swordPivot)
            {
                GameObject = gameObject;
                SwordPivot = swordPivot;
                Target = gameObject.transform.position;
                TargetRotation = gameObject.transform.rotation;
            }
            public GameObject GameObject { get; }
            public Transform? SwordPivot { get; }
            public Vector3 Target { get; set; }
            public Quaternion TargetRotation { get; set; }
            public int Health { get; set; }
            public int MaxHealth { get; set; }
        }
    }
}
