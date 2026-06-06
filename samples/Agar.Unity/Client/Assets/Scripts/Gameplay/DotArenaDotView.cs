#nullable enable

using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotView
    {
        private readonly SpriteRenderer _renderer;
        private readonly SpriteRenderer _outlineRenderer;
        private readonly SpriteRenderer? _spawnWaveRenderer;
        private readonly TextMesh _nameText;
        private readonly TextMesh _massText;
        private readonly bool _usesAuthoredSkin;
        private readonly float _createdAt;
        private float _impactUntil;

        public DotView(GameObject root, SpriteRenderer renderer, SpriteRenderer outlineRenderer, SpriteRenderer? spawnWaveRenderer, TextMesh nameText, TextMesh massText, bool usesAuthoredSkin)
        {
            Root = root;
            _renderer = renderer;
            _outlineRenderer = outlineRenderer;
            _spawnWaveRenderer = spawnWaveRenderer;
            _nameText = nameText;
            _massText = massText;
            _usesAuthoredSkin = usesAuthoredSkin;
            _createdAt = Time.time;
        }

        public GameObject Root { get; }

        public Vector2 GetPosition()
        {
            var position = Root.transform.position;
            return new Vector2(position.x, position.y);
        }

        public void SetPosition(Vector2 position)
        {
            Root.transform.position = new Vector3(position.x, position.y, 0f);
        }

        public void TriggerCollisionJelly()
        {
            _impactUntil = Time.time + 0.28f;
        }

        public void UpdateJelly(float time)
        {
            var remaining = Mathf.Clamp01((_impactUntil - time) / 0.28f);
            var pulse = remaining * remaining;
            UpdateMaterial(_renderer, time, pulse, 1f);
            UpdateSpawnWave(time);
        }

        public void SetIdentity(string playerId, float mass)
        {
            _nameText.text = playerId;
            _massText.text = DotArenaPresentation.FormatMass(mass);
        }

        public void ApplyPresentation(Color baseColor, PlayerLifeState state, bool alive, float radius)
        {
            var color = baseColor;
            if (!alive)
            {
                color = new Color(baseColor.r * 0.35f, baseColor.g * 0.35f, baseColor.b * 0.35f, 0.55f);
            }
            else if (state == PlayerLifeState.Move)
            {
                color = Color.Lerp(baseColor, Color.white, 0.12f);
            }

            _renderer.color = _usesAuthoredSkin
                ? ResolveAuthoredSkinColor(alive)
                : color;
            _outlineRenderer.enabled = false;
            _outlineRenderer.color = new Color(PlayerOutlineColor.r, PlayerOutlineColor.g, PlayerOutlineColor.b, 0f);
            var serverRadius = !float.IsNaN(radius) && !float.IsInfinity(radius) && radius > 0f
                ? radius
                : GameplayConfig.PlayerVisualRadius;
            var diameter = Mathf.Max(0.4f, serverRadius * 2f);
            Root.transform.localScale = new Vector3(diameter, diameter, 1f);
            var outlineScale = 1.14f;
            _outlineRenderer.transform.localScale = new Vector3(outlineScale, outlineScale, 1f);
        }

        private static Color ResolveAuthoredSkinColor(bool alive)
        {
            if (!alive)
            {
                return new Color(0.55f, 0.55f, 0.55f, 0.55f);
            }

            return Color.white;
        }

        private void UpdateSpawnWave(float time)
        {
            if (_spawnWaveRenderer == null)
            {
                return;
            }

            var progress = Mathf.Clamp01((time - _createdAt) / 0.55f);
            if (progress >= 1f)
            {
                _spawnWaveRenderer.enabled = false;
                return;
            }

            _spawnWaveRenderer.enabled = true;
            _spawnWaveRenderer.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.45f, progress);
            _spawnWaveRenderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.58f, 0f, progress));
        }

        private static void UpdateMaterial(SpriteRenderer renderer, float time, float impactPulse, float wobbleScale)
        {
            var material = renderer.material;
            if (material == null || !material.HasProperty("_WobbleAmount"))
            {
                return;
            }

            var wobble = (0.18f + (impactPulse * 0.62f)) * wobbleScale;
            var speed = 4.8f + (impactPulse * 9.5f);
            material.SetFloat("_WobbleAmount", wobble);
            material.SetFloat("_WobbleSpeed", speed + (Mathf.Sin(time * 1.3f) * 0.15f));
        }
    }
}
