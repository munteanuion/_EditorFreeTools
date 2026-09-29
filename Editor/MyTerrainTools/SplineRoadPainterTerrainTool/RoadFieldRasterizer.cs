using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public readonly struct TerrainGrid
    {
        private const float CenterOffset = 0.5f;

        private readonly Vector2 _origin;
        private readonly Vector2 _cellSize;
        private readonly float _offset;

        public TerrainGrid(int resolution, Vector2 origin, Vector2 size, bool cellCentered)
        {
            Resolution = resolution;
            _origin = origin;
            _offset = cellCentered ? CenterOffset : 0f;
            _cellSize = cellCentered ? size / resolution : size / (resolution - 1);
        }

        public int Resolution { get; }

        public Vector2 CellToWorld(int x, int y)
        {
            return new Vector2(_origin.x + (x + _offset) * _cellSize.x, _origin.y + (y + _offset) * _cellSize.y);
        }

        public RectInt ClampedRect(Vector2 worldMin, Vector2 worldMax)
        {
            var last = Resolution - 1;
            var minX = Mathf.Clamp(Mathf.FloorToInt((worldMin.x - _origin.x) / _cellSize.x - _offset), 0, last);
            var maxX = Mathf.Clamp(Mathf.CeilToInt((worldMax.x - _origin.x) / _cellSize.x - _offset), 0, last);
            var minY = Mathf.Clamp(Mathf.FloorToInt((worldMin.y - _origin.y) / _cellSize.y - _offset), 0, last);
            var maxY = Mathf.Clamp(Mathf.CeilToInt((worldMax.y - _origin.y) / _cellSize.y - _offset), 0, last);

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }

    public sealed class RoadField
    {
        public RoadField(RectInt rect)
        {
            Rect = rect;
            DistanceSqr = new float[rect.width * rect.height];
            Side = new float[rect.width * rect.height];
            Height = new float[rect.width * rect.height];
            Array.Fill(DistanceSqr, float.MaxValue);
        }

        public RectInt Rect { get; }

        public float[] DistanceSqr { get; }

        /// <summary>Signed side of the closest segment: positive = right, negative = left, 0 = on center.</summary>
        public float[] Side { get; }

        public float[] Height { get; }

        public int IndexOf(int x, int y) => (y - Rect.y) * Rect.width + (x - Rect.x);

        public float DistanceAt(int index) => Mathf.Sqrt(DistanceSqr[index]);
    }

    public sealed class RoadFieldRasterizer
    {
        private const float MinSegmentLengthSqr = 1e-8f;

        public RoadField Rasterize(TerrainGrid grid, IReadOnlyList<Vector3[]> polylines, float reach)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (!TryGetBounds(polylines, reach, out var min, out var max)) return null;

            var rect = grid.ClampedRect(min, max);
            if (rect.width <= 0 || rect.height <= 0) return null;

            var field = new RoadField(rect);

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length - 1; i++)
                {
                    StampSegment(grid, field, points[i], points[i + 1], reach);
                }
            }

            return field;
        }

        private bool TryGetBounds(IReadOnlyList<Vector3[]> polylines, float reach, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            var found = false;

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length; i++)
                {
                    var flat = new Vector2(points[i].x, points[i].z);
                    min = Vector2.Min(min, flat);
                    max = Vector2.Max(max, flat);
                    found = true;
                }
            }

            var margin = new Vector2(reach, reach);
            min -= margin;
            max += margin;

            return found;
        }

        private void StampSegment(TerrainGrid grid, RoadField field, Vector3 start, Vector3 end, float reach)
        {
            var origin = new Vector2(start.x, start.z);
            var direction = new Vector2(end.x - start.x, end.z - start.z);
            var lengthSqr = direction.sqrMagnitude;
            var margin = new Vector2(reach, reach);
            var segmentRect = grid.ClampedRect(Vector2.Min(origin, origin + direction) - margin, Vector2.Max(origin, origin + direction) + margin);
            var reachSqr = reach * reach;

            for (var y = segmentRect.yMin; y < segmentRect.yMax; y++)
            {
                for (var x = segmentRect.xMin; x < segmentRect.xMax; x++)
                {
                    var toCell = grid.CellToWorld(x, y) - origin;
                    var t = lengthSqr > MinSegmentLengthSqr ? Mathf.Clamp01(Vector2.Dot(toCell, direction) / lengthSqr) : 0f;
                    var closest = direction * t;
                    var distanceSqr = (toCell - closest).sqrMagnitude;
                    if (distanceSqr > reachSqr) continue;

                    var index = field.IndexOf(x, y);
                    if (distanceSqr >= field.DistanceSqr[index]) continue;

                    field.DistanceSqr[index] = distanceSqr;
                    field.Side[index] = (toCell.x - closest.x) * direction.y - (toCell.y - closest.y) * direction.x;
                    field.Height[index] = Mathf.Lerp(start.y, end.y, t);
                }
            }
        }
    }
}
