#nullable enable

using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class DotArenaGradientGraphic : Graphic
    {
        [SerializeField] private Color _topLeft = new(0.88f, 0.98f, 1f, 1f);
        [SerializeField] private Color _topRight = new(0.98f, 1f, 0.97f, 1f);
        [SerializeField] private Color _bottomLeft = new(0.78f, 0.95f, 1f, 1f);
        [SerializeField] private Color _bottomRight = new(1f, 0.91f, 0.86f, 1f);
        [SerializeField] private float _cornerRadius;
        [SerializeField] private int _cornerSegments = 8;

        public Color TopLeft
        {
            get => _topLeft;
            set
            {
                _topLeft = value;
                SetVerticesDirty();
            }
        }

        public Color TopRight
        {
            get => _topRight;
            set
            {
                _topRight = value;
                SetVerticesDirty();
            }
        }

        public Color BottomLeft
        {
            get => _bottomLeft;
            set
            {
                _bottomLeft = value;
                SetVerticesDirty();
            }
        }

        public Color BottomRight
        {
            get => _bottomRight;
            set
            {
                _bottomRight = value;
                SetVerticesDirty();
            }
        }

        public float CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = Mathf.Max(0f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var radius = Mathf.Min(Mathf.Max(0f, _cornerRadius), rect.width * 0.5f, rect.height * 0.5f);
            if (radius <= 0.01f)
            {
                PopulateRectangle(vh, rect);
                return;
            }

            PopulateRoundedRectangle(vh, rect, radius, Mathf.Clamp(_cornerSegments, 2, 16));
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _cornerRadius = Mathf.Max(0f, _cornerRadius);
            _cornerSegments = Mathf.Clamp(_cornerSegments, 2, 16);
            SetVerticesDirty();
        }
#endif

        private void PopulateRectangle(VertexHelper vh, Rect rect)
        {
            var vertex = UIVertex.simpleVert;

            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertex.color = _bottomLeft;
            vh.AddVert(vertex);

            vertex.position = new Vector2(rect.xMin, rect.yMax);
            vertex.color = _topLeft;
            vh.AddVert(vertex);

            vertex.position = new Vector2(rect.xMax, rect.yMax);
            vertex.color = _topRight;
            vh.AddVert(vertex);

            vertex.position = new Vector2(rect.xMax, rect.yMin);
            vertex.color = _bottomRight;
            vh.AddVert(vertex);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private void PopulateRoundedRectangle(VertexHelper vh, Rect rect, float radius, int segments)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = rect.center;
            vertex.color = GetGradientColor(rect, rect.center);
            vh.AddVert(vertex);

            AddCorner(vh, rect, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f, segments);
            AddCorner(vh, rect, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f, segments);
            AddCorner(vh, rect, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f, segments);
            AddCorner(vh, rect, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f, segments);

            var vertexCount = vh.currentVertCount;
            for (var i = 1; i < vertexCount; i++)
            {
                var next = i == vertexCount - 1 ? 1 : i + 1;
                vh.AddTriangle(0, next, i);
            }
        }

        private void AddCorner(VertexHelper vh, Rect rect, Vector2 center, float radius, float startDegrees, float endDegrees, int segments)
        {
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
                var position = new Vector2(
                    center.x + (Mathf.Cos(angle) * radius),
                    center.y + (Mathf.Sin(angle) * radius));
                position.x = Mathf.Clamp(position.x, rect.xMin, rect.xMax);
                position.y = Mathf.Clamp(position.y, rect.yMin, rect.yMax);

                var vertex = UIVertex.simpleVert;
                vertex.position = position;
                vertex.color = GetGradientColor(rect, position);
                vh.AddVert(vertex);
            }
        }

        private Color GetGradientColor(Rect rect, Vector2 position)
        {
            var x = Mathf.InverseLerp(rect.xMin, rect.xMax, position.x);
            var y = Mathf.InverseLerp(rect.yMin, rect.yMax, position.y);
            var bottom = Color.Lerp(_bottomLeft, _bottomRight, x);
            var top = Color.Lerp(_topLeft, _topRight, x);
            return Color.Lerp(bottom, top, y);
        }
    }
}
