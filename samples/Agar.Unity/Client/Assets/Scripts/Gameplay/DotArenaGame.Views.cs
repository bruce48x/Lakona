#nullable enable

using System;
using System.Threading.Tasks;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void ResetSessionPresentation()
        {
            Debug.Log($"[DotArena] ResetSessionPresentation views={_views.Count}, pickups={_pickupViews.Count}, mode={_sessionMode}");
            foreach (var view in _views.Values)
            {
                Destroy(view.Root);
            }

            foreach (var pickupView in _pickupViews)
            {
                Destroy(pickupView.Root);
            }

            _overlayPresenter.Clear(Destroy);
            _views.Clear();
            _pickupViews.Clear();
            _renderStates.Clear();
            _callbackInbox.Clear();
            _localWinCount = _sessionMode == SessionMode.Multiplayer ? _localWinCount : 0;
            _lastWorldTick = -1;
            _lastLoggedPlayerCount = -1;
            _nextInputAt = 0f;
            _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
            UpdateArenaVisuals();
        }

        private void UpdateViews()
        {
            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var elapsed = Mathf.Clamp01((Time.time - renderState.ReceivedAt) / InterpolationDurationSeconds);
                var smoothed = elapsed * elapsed * (3f - (2f * elapsed));
                var position = Vector2.Lerp(renderState.PreviousPosition, renderState.TargetPosition, smoothed);
                entry.Value.SetPosition(position);
            }

            UpdateCameraFollow();
            _overlayPresenter.UpdateOverlayViews(_sceneUiPresenter, _views, _renderStates);

            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            foreach (var pickupView in _pickupViews)
            {
                var pulse = 1f + (Mathf.Sin(Time.time * PickupPulseFrequency) * PickupPulseAmplitude);
                pickupView.UpdateVisual(Time.time, pickupScale * pulse, PickupAbsorbDurationSeconds);
            }

            foreach (var dotView in _views.Values)
            {
                dotView.UpdateJelly(Time.time);
            }
        }

        private void UpdateCameraFollow()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var targetPosition = Vector2.zero;
            var desiredCameraSize = FollowCameraSize;
            if (_views.TryGetValue(_localPlayerId, out var localView))
            {
                targetPosition = localView.GetPosition();
            }

            if (_renderStates.TryGetValue(_localPlayerId, out var localState))
            {
                desiredCameraSize = Mathf.Clamp(
                    Mathf.Max(FollowCameraSize, localState.Radius * FollowCameraRadiusMultiplier),
                    FollowCameraSize,
                    MaxFollowCameraSize);
            }

            var zoomT = 1f - Mathf.Exp(-CameraZoomSharpness * Time.deltaTime);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, desiredCameraSize, zoomT);

            var halfVisibleHeight = Mathf.Max(0f, camera.orthographicSize - ArenaVisualPadding);
            var halfVisibleWidth = halfVisibleHeight * camera.aspect;
            var limitX = Mathf.Max(0f, CurrentArenaHalfWidth - halfVisibleWidth);
            var limitY = Mathf.Max(0f, CurrentArenaHalfHeight - halfVisibleHeight);
            var desired = new Vector3(
                Mathf.Clamp(targetPosition.x, -limitX, limitX),
                Mathf.Clamp(targetPosition.y, -limitY, limitY),
                -10f);
            var t = 1f - Mathf.Exp(-CameraFollowSharpness * Time.deltaTime);
            camera.transform.position = Vector3.Lerp(camera.transform.position, desired, t);
        }

        private DotView CreateView(string playerId)
        {
            var viewRoot = new GameObject(playerId);
            viewRoot.transform.SetParent(transform, false);
            Debug.Log($"[DotArena] CreateView root={viewRoot.name} parent={viewRoot.transform.parent?.name}");

            var renderer = viewRoot.AddComponent<SpriteRenderer>();
            var cosmeticId = playerId == _localPlayerId ? GetLocalPresentationCosmeticId() : null;
            var baseColor = DotArenaPresentation.ResolvePlayerColor(playerId, cosmeticId);
            var skinSprite = ResolvePlayerSkinSprite(playerId, cosmeticId, out var usesAuthoredSkin);
            renderer.sprite = skinSprite;
            renderer.color = usesAuthoredSkin ? Color.white : baseColor;
            renderer.sortingOrder = PlayerSortingOrder;
            renderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 3.6f, 0.06f);

            var outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(viewRoot.transform, false);
            outlineObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
            outlineRenderer.sprite = _playerOutlineSprite;
            outlineRenderer.color = Color.clear;
            outlineRenderer.sortingOrder = PlayerSortingOrder - 1;
            outlineRenderer.enabled = false;
            outlineRenderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 4.2f, 0.08f);

            SpriteRenderer? spawnWaveRenderer = null;
            if (_spawnWaveSprite != null)
            {
                var spawnWave = new GameObject("SpawnWave");
                spawnWave.transform.SetParent(viewRoot.transform, false);
                spawnWave.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                spawnWaveRenderer = spawnWave.AddComponent<SpriteRenderer>();
                spawnWaveRenderer.sprite = _spawnWaveSprite;
                spawnWaveRenderer.color = new Color(1f, 1f, 1f, 0.58f);
                spawnWaveRenderer.sortingOrder = PlayerSortingOrder - 2;
            }

            var nameBackdrop = new GameObject("NameBackdrop");
            nameBackdrop.transform.SetParent(viewRoot.transform, false);
            nameBackdrop.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth + 0.01f);
            nameBackdrop.transform.localScale = new Vector3(PlayerNameBackdropWidth, PlayerNameBackdropHeight, 1f);
            var nameBackdropRenderer = nameBackdrop.AddComponent<SpriteRenderer>();
            nameBackdropRenderer.sprite = _pixelSprite;
            nameBackdropRenderer.color = PlayerTextBackdropColor;
            nameBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var nameLabel = new GameObject("NameLabel");
            nameLabel.transform.SetParent(viewRoot.transform, false);
            nameLabel.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth);
            nameLabel.transform.localScale = Vector3.one * PlayerNameScale;

            var nameText = nameLabel.AddComponent<TextMesh>();
            nameText.text = playerId;
            nameText.fontSize = 48;
            nameText.characterSize = PlayerTextCharacterSize;
            nameText.anchor = TextAnchor.MiddleCenter;
            nameText.alignment = TextAlignment.Center;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = new Color(0.98f, 0.99f, 1f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(nameText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var massBackdrop = new GameObject("MassBackdrop");
            massBackdrop.transform.SetParent(viewRoot.transform, false);
            massBackdrop.transform.localPosition = new Vector3(0f, PlayerMassOffsetY, PlayerTextDepth + 0.01f);
            massBackdrop.transform.localScale = new Vector3(PlayerMassBackdropWidth, PlayerMassBackdropHeight, 1f);
            var massBackdropRenderer = massBackdrop.AddComponent<SpriteRenderer>();
            massBackdropRenderer.sprite = _pixelSprite;
            massBackdropRenderer.color = PlayerTextBackdropColor;
            massBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var massLabel = new GameObject("MassLabel");
            massLabel.transform.SetParent(viewRoot.transform, false);
            massLabel.transform.localPosition = new Vector3(0f, PlayerMassOffsetY, PlayerTextDepth);
            massLabel.transform.localScale = Vector3.one * PlayerMassScale;

            var massText = massLabel.AddComponent<TextMesh>();
            massText.text = "0";
            massText.fontSize = 44;
            massText.characterSize = PlayerTextCharacterSize;
            massText.anchor = TextAnchor.MiddleCenter;
            massText.alignment = TextAlignment.Center;
            massText.fontStyle = FontStyle.Bold;
            massText.color = new Color(1f, 0.97f, 0.72f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(massText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var view = new DotView(viewRoot, renderer, outlineRenderer, spawnWaveRenderer, nameText, massText, usesAuthoredSkin);
            view.SetIdentity(playerId, 0f);
            view.ApplyPresentation(baseColor, PlayerLifeState.Idle, true, GameplayConfig.PlayerVisualRadius);
            return view;
        }

        private PickupView CreatePickupView(PickupType pickupType)
        {
            var pickupRoot = new GameObject($"{pickupType}Pickup");
            pickupRoot.transform.SetParent(transform, false);

            var pickupColor = DotArenaPresentation.GetPickupColor(pickupType);
            var massPickupSprite = ResolveMassPickupSprite();
            var usesAuthoredPickupSprite = pickupType == PickupType.MassPoint && massPickupSprite != null;
            var renderer = pickupRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = usesAuthoredPickupSprite ? massPickupSprite : _playerSprite;
            renderer.color = usesAuthoredPickupSprite ? Color.white : pickupColor;
            renderer.sortingOrder = PickupSortingOrder;
            renderer.material = CreatePickupAbsorbMaterial(pickupColor);

            var glow = new GameObject("Glow");
            glow.transform.SetParent(pickupRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            glow.transform.localScale = Vector3.one * 1.24f;

            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = _pickupGlowSprite != null ? _pickupGlowSprite : _playerOutlineSprite;
            glowRenderer.color = Color.clear;
            glowRenderer.sortingOrder = PickupSortingOrder - 1;
            glowRenderer.enabled = false;

            var label = new GameObject("Label");
            label.transform.SetParent(pickupRoot.transform, false);
            label.transform.localPosition = new Vector3(0f, 0f, PlayerTextDepth);
            label.transform.localScale = Vector3.one * PickupLabelScale;

            var labelText = label.AddComponent<TextMesh>();
            labelText.text = DotArenaPresentation.GetPickupDisplayName(pickupType);
            labelText.fontSize = 64;
            labelText.characterSize = 0.12f;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = DotArenaPresentation.GetPickupLabelColor(pickupType);
            DotArenaSpriteFactory.ConfigureTextRenderer(labelText.GetComponent<MeshRenderer>(), PickupLabelSortingOrder);

            pickupRoot.SetActive(false);
            return new PickupView(pickupRoot, renderer, glowRenderer, labelText);
        }

        private Sprite? ResolveMassPickupSprite()
        {
            if (_massPickupSprite == null)
            {
                return _goldPickupSprite;
            }

            if (_goldPickupSprite == null)
            {
                return _massPickupSprite;
            }

            return (_pickupViews.Count & 1) == 0 ? _massPickupSprite : _goldPickupSprite;
        }

        private Material CreatePickupAbsorbMaterial(Color baseColor)
        {
            if (_pickupAbsorbShader == null)
            {
                _pickupAbsorbShader = Shader.Find(PickupAbsorbShaderName);
            }

            var shader = _pickupAbsorbShader != null ? _pickupAbsorbShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_pickupAbsorbShader != null)
            {
                material.SetFloat("_Dissolve", 0f);
                if (material.HasProperty("_EdgeColor"))
                {
                    material.SetColor("_EdgeColor", Color.Lerp(baseColor, Color.white, 0.55f));
                }
            }

            return material;
        }

        private Material CreateJellyMaterial(float phase, float wobbleSpeed, float wobbleAmount)
        {
            if (_jellyShader == null)
            {
                _jellyShader = Shader.Find(JellyShaderName);
            }

            var shader = _jellyShader != null ? _jellyShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_jellyShader != null)
            {
                material.SetFloat("_Phase", phase);
                material.SetFloat("_WobbleSpeed", wobbleSpeed);
                material.SetFloat("_WobbleAmount", wobbleAmount);
            }

            return material;
        }

    }
}
