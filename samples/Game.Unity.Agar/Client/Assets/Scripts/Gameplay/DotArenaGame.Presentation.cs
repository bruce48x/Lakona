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
        private void ConfigureCamera()
        {
            var cameraObject = GameObject.FindWithTag("MainCamera");
            var mainCamera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;

            if (mainCamera == null)
            {
                cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                mainCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            mainCamera.orthographic = true;
            mainCamera.orthographicSize = FollowCameraSize;
            mainCamera.backgroundColor = BackgroundColor;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            mainCamera.transform.rotation = Quaternion.identity;
        }

        private void BuildArena()
        {
            _pixelSprite = DotArenaSpriteFactory.CreatePixelSprite();
            _playerSprite = DotArenaSpriteFactory.CreateCircleSprite();
            _playerOutlineSprite = DotArenaSpriteFactory.CreateCircleOutlineSprite();
            _jellyShader = Shader.Find(JellyShaderName);

            var existingRoot = transform.Find("ArenaRoot");
            if (existingRoot != null)
            {
                Destroy(existingRoot.gameObject);
            }

            var arenaRoot = new GameObject("ArenaRoot");
            arenaRoot.transform.SetParent(transform, false);

            CreateRect(arenaRoot.transform, "DangerZone", Vector2.zero,
                new Vector2((ArenaHalfWidth + 1f) * 2f, (ArenaHalfHeight + 1f) * 2f), DangerColor, -30);

            CreateRect(arenaRoot.transform, "Board", Vector2.zero, new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f),
                BoardColor, -20);

            _safeZoneRenderer = CreateRect(arenaRoot.transform, "SafeZone", Vector2.zero,
                new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f), SafeZoneColor, -15);

            const float gridStep = 2f;
            for (var x = -ArenaHalfWidth; x <= ArenaHalfWidth + 0.01f; x += gridStep)
            {
                CreateRect(arenaRoot.transform, $"Vertical-{Mathf.RoundToInt(x)}", new Vector2(x, 0f),
                    new Vector2(0.05f, ArenaHalfHeight * 2f), GridColor, -10);
            }

            for (var y = -ArenaHalfHeight; y <= ArenaHalfHeight + 0.01f; y += gridStep)
            {
                CreateRect(arenaRoot.transform, $"Horizontal-{Mathf.RoundToInt(y)}", new Vector2(0f, y),
                    new Vector2(ArenaHalfWidth * 2f, 0.05f), GridColor, -10);
            }

            _topBorderRenderer = CreateRect(arenaRoot.transform, "TopBorder", new Vector2(0f, ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            _bottomBorderRenderer = CreateRect(arenaRoot.transform, "BottomBorder", new Vector2(0f, -ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            _leftBorderRenderer = CreateRect(arenaRoot.transform, "LeftBorder", new Vector2(-ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
            _rightBorderRenderer = CreateRect(arenaRoot.transform, "RightBorder", new Vector2(ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
            UpdateArenaVisuals();
        }

        private Sprite ResolvePlayerSkinSprite(string playerId, string? cosmeticId, out bool usesAuthoredSkin)
        {
            usesAuthoredSkin = false;
            return _playerSprite;
        }

        private string? GetLocalPresentationCosmeticId()
        {
            var equippedCosmeticId = _metaState?.EquippedCosmeticId;
            if (!string.Equals(equippedCosmeticId, "skin_default", StringComparison.Ordinal))
            {
                return equippedCosmeticId;
            }

            return null;
        }

        private SpriteRenderer CreateRect(Transform parent, string objectName, Vector2 position, Vector2 size, Color color, int sortingOrder)
        {
            var rectangle = new GameObject(objectName);
            rectangle.transform.SetParent(parent, false);
            rectangle.transform.localPosition = new Vector3(position.x, position.y, 0f);
            rectangle.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = rectangle.AddComponent<SpriteRenderer>();
            renderer.sprite = _pixelSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static float ArenaHalfWidth => GameplayConfig.ArenaHalfExtents.x;
        private static float ArenaHalfHeight => GameplayConfig.ArenaHalfExtents.y;
        private float CurrentArenaHalfWidth => _currentArenaHalfExtents.x;
        private float CurrentArenaHalfHeight => _currentArenaHalfExtents.y;
        private void UpdateArenaVisuals()
        {
            if (_safeZoneRenderer != null)
            {
                _safeZoneRenderer.transform.localScale = new Vector3(CurrentArenaHalfWidth * 2f, CurrentArenaHalfHeight * 2f, 1f);
            }

            UpdateBorderRenderer(_topBorderRenderer, new Vector2(0f, CurrentArenaHalfHeight), new Vector2(CurrentArenaHalfWidth * 2f + 0.18f, 0.18f));
            UpdateBorderRenderer(_bottomBorderRenderer, new Vector2(0f, -CurrentArenaHalfHeight), new Vector2(CurrentArenaHalfWidth * 2f + 0.18f, 0.18f));
            UpdateBorderRenderer(_leftBorderRenderer, new Vector2(-CurrentArenaHalfWidth, 0f), new Vector2(0.18f, CurrentArenaHalfHeight * 2f + 0.18f));
            UpdateBorderRenderer(_rightBorderRenderer, new Vector2(CurrentArenaHalfWidth, 0f), new Vector2(0.18f, CurrentArenaHalfHeight * 2f + 0.18f));
        }

        private static void UpdateBorderRenderer(SpriteRenderer? renderer, Vector2 position, Vector2 size)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.transform.localPosition = new Vector3(position.x, position.y, 0f);
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
