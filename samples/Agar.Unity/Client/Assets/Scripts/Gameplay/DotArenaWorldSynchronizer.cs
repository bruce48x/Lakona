#nullable enable

using System;
using System.Collections.Generic;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaWorldSynchronizer
    {
        private readonly Dictionary<string, DotView> _views;
        private readonly Dictionary<string, PlayerRenderState> _renderStates;
        private readonly List<PickupView> _pickupViews;
        private readonly Dictionary<string, PlayerOverlayView> _playerOverlayViews;
        private readonly Func<string, DotView> _createView;
        private readonly Action<string> _ensurePlayerOverlay;
        private readonly Func<PickupType, PickupView> _createPickupView;
        private readonly Action<UnityEngine.Object> _destroyObject;
        private readonly Action _updateArenaVisuals;
        private readonly Action<string> _pushEvent;
        private readonly Action<string, float> _pushEventWithDuration;
        private readonly Action<string> _setEventMessage;
        private readonly Func<string?> _getLocalEquippedCosmeticId;

        public DotArenaWorldSynchronizer(
            Dictionary<string, DotView> views,
            Dictionary<string, PlayerRenderState> renderStates,
            List<PickupView> pickupViews,
            Dictionary<string, PlayerOverlayView> playerOverlayViews,
            Func<string, DotView> createView,
            Action<string> ensurePlayerOverlay,
            Func<PickupType, PickupView> createPickupView,
            Action<UnityEngine.Object> destroyObject,
            Action updateArenaVisuals,
            Action<string> pushEvent,
            Action<string, float> pushEventWithDuration,
            Action<string> setEventMessage,
            Func<string?> getLocalEquippedCosmeticId)
        {
            _views = views;
            _renderStates = renderStates;
            _pickupViews = pickupViews;
            _playerOverlayViews = playerOverlayViews;
            _createView = createView;
            _ensurePlayerOverlay = ensurePlayerOverlay;
            _createPickupView = createPickupView;
            _destroyObject = destroyObject;
            _updateArenaVisuals = updateArenaVisuals;
            _pushEvent = pushEvent;
            _pushEventWithDuration = pushEventWithDuration;
            _setEventMessage = setEventMessage;
            _getLocalEquippedCosmeticId = getLocalEquippedCosmeticId;
        }

        public void ApplyWorldState(
            WorldState worldState,
            string localPlayerId,
            ref int lastWorldTick,
            ref int lastRoundRemainingSeconds,
            ref int lastLoggedPlayerCount,
            ref Vector2 currentArenaHalfExtents)
        {
            if (worldState.Tick < lastWorldTick)
            {
                return;
            }

            var previousArenaHalfExtents = currentArenaHalfExtents;
            lastWorldTick = worldState.Tick;
            lastRoundRemainingSeconds = worldState.RoundRemainingSeconds;
            currentArenaHalfExtents = new Vector2(worldState.ArenaHalfExtentX, worldState.ArenaHalfExtentY);
            _updateArenaVisuals();
            if (worldState.Players.Count != lastLoggedPlayerCount)
            {
                lastLoggedPlayerCount = worldState.Players.Count;
                Debug.Log($"[DotArena] WorldState tick={worldState.Tick}, players={worldState.Players.Count}, local={localPlayerId}");
                Debug.Log($"[DotArena] Players => {string.Join(", ", worldState.Players.ConvertAll(static p => $"{p.PlayerId}@({p.X:0.00},{p.Y:0.00}) alive={p.Alive}"))}");
            }

            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in worldState.Players)
            {
                activeIds.Add(player.PlayerId);
                var targetPosition = new Vector2(player.X, player.Y);
                var isNewView = false;
                var isNewRenderState = false;

                if (!_views.TryGetValue(player.PlayerId, out var view))
                {
                    view = _createView(player.PlayerId);
                    _views.Add(player.PlayerId, view);
                    _ensurePlayerOverlay(player.PlayerId);
                    view.SetPosition(targetPosition);
                    isNewView = true;
                    Debug.Log($"[DotArena] Created view for {player.PlayerId}, totalViews={_views.Count}");
                }

                if (!_renderStates.TryGetValue(player.PlayerId, out var renderState))
                {
                    renderState = new PlayerRenderState();
                    _renderStates.Add(player.PlayerId, renderState);
                    isNewRenderState = true;
                }

                var previousMass = renderState.Mass;

                var currentPosition = view.GetPosition();
                if (isNewView || isNewRenderState)
                {
                    currentPosition = targetPosition;
                }

                renderState.PreviousPosition = currentPosition;
                renderState.TargetPosition = targetPosition;
                renderState.ReceivedAt = Time.time;
                renderState.Alive = player.Alive;
                renderState.State = player.State;
                renderState.Mass = player.Mass;
                renderState.Radius = player.Radius;
                renderState.MoveSpeed = player.MoveSpeed;

                view.SetIdentity(player.PlayerId, player.Mass);
                if (_playerOverlayViews.TryGetValue(player.PlayerId, out var overlay))
                {
                    overlay.NameText.text = player.PlayerId;
                    overlay.MassText.text = $"mass {DotArenaPresentation.FormatMass(player.Mass)}";
                }

                if (player.Mass > previousMass + 0.9f && player.PlayerId == localPlayerId)
                {
                    view.TriggerCollisionJelly();
                }

                var cosmeticId = player.PlayerId == localPlayerId
                    ? _getLocalEquippedCosmeticId()
                    : null;

                view.ApplyPresentation(
                    DotArenaPresentation.ResolvePlayerColor(player.PlayerId, cosmeticId),
                    player.State,
                    player.Alive,
                    player.Radius);

                if (player.PlayerId == localPlayerId)
                {
                    if (player.Mass > previousMass + 0.05f)
                    {
                        _pushEvent($"Mass {DotArenaPresentation.FormatMass(player.Mass)}");
                    }
                }

                if (_views.Count >= 2 && worldState.Players.Exists(static p => p.Alive))
                {
                    _setEventMessage("Match in progress");
                }
            }

            var removedIds = new List<string>();
            foreach (var playerId in _views.Keys)
            {
                if (!activeIds.Contains(playerId))
                {
                    removedIds.Add(playerId);
                }
            }

            foreach (var removedId in removedIds)
            {
                _destroyObject(_views[removedId].Root);
                _views.Remove(removedId);
                _renderStates.Remove(removedId);
                if (_playerOverlayViews.TryGetValue(removedId, out var overlay))
                {
                    _destroyObject(overlay.Root);
                    _playerOverlayViews.Remove(removedId);
                }
            }

            if (previousArenaHalfExtents.x >= GameplayConfig.ArenaHalfExtents.x - 0.05f &&
                currentArenaHalfExtents.x < previousArenaHalfExtents.x - 0.05f)
            {
                _pushEventWithDuration("Ring is closing. Stay inside the arena.", 2.5f);
            }

            ApplyPickupState(worldState);
            Debug.Log($"[DotArena] ApplyWorldState complete tick={worldState.Tick}, views={_views.Count}, renders={_renderStates.Count}");
        }

        private void ApplyPickupState(WorldState worldState)
        {
            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            while (_pickupViews.Count < worldState.Pickups.Count)
            {
                var index = _pickupViews.Count;
                var pickupType = worldState.Pickups[index].Type;
                _pickupViews.Add(_createPickupView(pickupType));
            }

            for (var i = 0; i < worldState.Pickups.Count; i++)
            {
                var pickup = worldState.Pickups[i];
                var view = _pickupViews[i];
                view.ShowAt(new Vector3(pickup.X, pickup.Y, 0f), pickupScale);
            }

            for (var i = worldState.Pickups.Count; i < _pickupViews.Count; i++)
            {
                _pickupViews[i].Root.SetActive(false);
            }
        }
    }
}
