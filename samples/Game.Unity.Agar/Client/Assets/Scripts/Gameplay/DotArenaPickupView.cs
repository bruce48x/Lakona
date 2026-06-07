#nullable enable

using UnityEngine;

namespace SampleClient.Gameplay
{
    internal sealed class PickupView
    {
        private readonly Color _baseLabelColor;
        private Vector3 _absorbStartPosition;
        private Vector3 _absorbTargetPosition;
        private float _absorbStartedAt;
        private float _shownAt;
        private bool _isAbsorbing;

        public PickupView(GameObject root, SpriteRenderer renderer, SpriteRenderer glowRenderer, TextMesh labelText)
        {
            Root = root;
            Renderer = renderer;
            GlowRenderer = glowRenderer;
            LabelText = labelText;
            _baseLabelColor = labelText.color;
        }

        public GameObject Root { get; }
        public SpriteRenderer Renderer { get; }
        public SpriteRenderer GlowRenderer { get; }
        public TextMesh LabelText { get; }
        public bool IsAbsorbing => _isAbsorbing;

        public void ShowAt(Vector3 position, float scale)
        {
            _isAbsorbing = false;
            _shownAt = Time.time;
            Root.SetActive(true);
            Root.transform.position = position;
            Root.transform.localScale = new Vector3(scale, scale, 1f);
            GlowRenderer.enabled = true;
            GlowRenderer.transform.localScale = Vector3.one * 1.24f;
            GlowRenderer.color = new Color(1f, 1f, 1f, 0.16f);

            var labelColor = _baseLabelColor;
            labelColor.a = _baseLabelColor.a;
            LabelText.color = labelColor;

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", 0f);
            }
        }

        public void StartAbsorb(Vector3 targetPosition, float time, float scale)
        {
            if (!Root.activeSelf)
            {
                return;
            }

            _isAbsorbing = true;
            _absorbStartedAt = time;
            _absorbStartPosition = Root.transform.position;
            _absorbTargetPosition = targetPosition;
            Root.transform.localScale = new Vector3(scale, scale, 1f);

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", 0f);
            }
        }

        public void UpdateVisual(float time, float pulseScale, float absorbDurationSeconds)
        {
            if (!_isAbsorbing)
            {
                if (Root.activeSelf)
                {
                    Root.transform.localScale = new Vector3(pulseScale, pulseScale, 1f);
                    var idlePulse = 0.12f + (Mathf.Sin((time - _shownAt) * 5.1f) * 0.04f);
                    GlowRenderer.transform.localScale = Vector3.one * (1.16f + (pulseScale * 0.08f));
                    GlowRenderer.color = new Color(1f, 1f, 1f, idlePulse);
                }

                return;
            }

            var progress = Mathf.Clamp01((time - _absorbStartedAt) / absorbDurationSeconds);
            var eased = 1f - Mathf.Pow(1f - progress, 3f);
            Root.transform.position = Vector3.Lerp(_absorbStartPosition, _absorbTargetPosition, eased);
            var scale = Mathf.Lerp(pulseScale, pulseScale * 0.24f, eased);
            Root.transform.localScale = new Vector3(scale, scale, 1f);
            GlowRenderer.enabled = true;
            GlowRenderer.transform.localScale = Vector3.one * Mathf.Lerp(1.24f, 0.42f, eased);

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", Mathf.SmoothStep(0f, 1f, progress));
            }

            GlowRenderer.transform.Rotate(0f, 0f, 280f * Time.deltaTime);
            GlowRenderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.72f, 0f, eased));

            var labelColor = _baseLabelColor;
            labelColor.a = Mathf.Lerp(_baseLabelColor.a, 0f, Mathf.Clamp01(progress * 1.25f));
            LabelText.color = labelColor;

            if (progress >= 1f)
            {
                _isAbsorbing = false;
                Root.SetActive(false);
            }
        }
    }
}
