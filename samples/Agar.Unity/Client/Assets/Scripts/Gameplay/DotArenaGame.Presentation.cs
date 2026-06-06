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
            LoadPlayerSkinSprites();
            LoadEnvironmentArtSprites();
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

            var boardRenderer = CreateRect(arenaRoot.transform, "Board", Vector2.zero, new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f),
                BoardColor, -20);
            if (_arenaBackgroundSprite != null)
            {
                boardRenderer.sprite = _arenaBackgroundSprite;
                boardRenderer.color = Color.white;
            }

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

        private void LoadEnvironmentArtSprites()
        {
            _massPickupSprite = null;
            _goldPickupSprite = null;
            _arenaBackgroundSprite = null;
            _pickupGlowSprite = null;
            _spawnWaveSprite = null;

#if UNITY_EDITOR
            _massPickupSprite = TryLoadArtSprite("mass pickup", "Assets/Art/Pickups/Pickup_Mass_Teal_01.png");
            _goldPickupSprite = TryLoadArtSprite("gold mass pickup", "Assets/Art/Pickups/Pickup_Mass_Gold_01.png");
            _arenaBackgroundSprite = TryLoadArtSprite("arena background", "Assets/Art/Backgrounds/BG_Arena_Grid_Dark_01.png");
            _pickupGlowSprite = TryLoadArtSprite("pickup absorb ring", "Assets/Art/FX/FX_Absorb_Ring_01.png");
            _spawnWaveSprite = TryLoadArtSprite("player spawn wave", "Assets/Art/FX/FX_Spawn_Wave_01.png");
#endif
        }

        private void LoadPlayerSkinSprites()
        {
            _playerSkinSprites.Clear();
            _remotePlayerSkinSprites.Clear();

#if UNITY_EDITOR
            TryLoadPlayerSkinSprite("skin_default", "Assets/Art/Sprites/Skins/Skin_Jelly_Cyan.png", true);
            TryLoadPlayerSkinSprite("skin_crimson", "Assets/Art/Sprites/Skins/Skin_Jelly_Crimson.png", true);
            TryLoadPlayerSkinSprite("skin_sunburst", "Assets/Art/Sprites/Skins/Skin_Jelly_Sunburst.png", true);
#endif
        }

#if UNITY_EDITOR
        private static Sprite? TryLoadArtSprite(string label, string assetPath)
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[DotArena] {label} sprite not found: {assetPath}");
            }

            return sprite;
        }

        private void TryLoadPlayerSkinSprite(string cosmeticId, string assetPath, bool useForRemotePlayers)
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"[DotArena] Player skin sprite not found: {assetPath}");
                return;
            }

            _playerSkinSprites[cosmeticId] = sprite;
            if (useForRemotePlayers)
            {
                _remotePlayerSkinSprites.Add(sprite);
            }
        }
#endif

        private Sprite ResolvePlayerSkinSprite(string playerId, string? cosmeticId, out bool usesAuthoredSkin)
        {
            if (string.IsNullOrWhiteSpace(cosmeticId) || string.Equals(cosmeticId, "skin_default", StringComparison.Ordinal))
            {
                usesAuthoredSkin = false;
                return _playerSprite;
            }

            if (!string.IsNullOrWhiteSpace(cosmeticId) && _playerSkinSprites.TryGetValue(cosmeticId, out var cosmeticSprite))
            {
                usesAuthoredSkin = true;
                return cosmeticSprite;
            }

            if (_remotePlayerSkinSprites.Count > 0)
            {
                usesAuthoredSkin = true;
                return _remotePlayerSkinSprites[GetStableSkinIndex(playerId) % _remotePlayerSkinSprites.Count];
            }

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

        private static int GetStableSkinIndex(string playerId)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in playerId)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                return (int)(hash & 0x7fffffffu);
            }
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
