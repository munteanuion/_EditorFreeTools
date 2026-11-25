#if UNITY_EDITOR
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

        public Vector2 CellSize => _cellSize;

        public Vector2 CellToWorld(int x, int y)
        {
            return new Vector2(_origin.x + (x + _offset) * _cellSize.x, _origin.y + (y + _offset) * _cellSize.y);
        }

        public RectInt ClampedRect(Vector2 worldMin, Vector2 worldMax)
        {
            var last = Resolution - 1;
            var rawMinX = Mathf.FloorToInt((worldMin.x - _origin.x) / _cellSize.x - _offset);
            var rawMaxX = Mathf.CeilToInt((worldMax.x - _origin.x) / _cellSize.x - _offset);
            var rawMinY = Mathf.FloorToInt((worldMin.y - _origin.y) / _cellSize.y - _offset);
            var rawMaxY = Mathf.CeilToInt((worldMax.y - _origin.y) / _cellSize.y - _offset);

            // No overlap at all: report an empty rect so callers skip the grid instead of clamping
            // everything onto the border cell (which would produce a one-cell snapshot for every tile).
            if (rawMaxX < 0 || rawMaxY < 0 || rawMinX > last || rawMinY > last)
            {
                return new RectInt(0, 0, 0, 0);
            }

            var minX = Mathf.Clamp(rawMinX, 0, last);
            var maxX = Mathf.Clamp(rawMaxX, 0, last);
            var minY = Mathf.Clamp(rawMinY, 0, last);
            var maxY = Mathf.Clamp(rawMaxY, 0, last);

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }

    public sealed class RoadField
    {
        public RoadField(RectInt rect, bool blendHeights)
        {
            Rect = rect;
            DistanceSqr = new float[rect.width * rect.height];
            Side = new float[rect.width * rect.height];
            Height = new float[rect.width * rect.height];
            BlendWeight = blendHeights ? new float[rect.width * rect.height] : null;
            Array.Fill(DistanceSqr, float.MaxValue);
        }

        public RectInt Rect { get; }

        public float[] DistanceSqr { get; }

        /// <summary>Signed side of the closest segment: positive = right, negative = left, 0 = on center.</summary>
        public float[] Side { get; }

        public float[] Height { get; }

        /// <summary>
        /// Accumulated blend weight when the field is rasterized for heights.
        /// Null for distance-only fields (texture and detail maps).
        /// </summary>
        public float[] BlendWeight { get; }

        public int IndexOf(int x, int y) => (y - Rect.y) * Rect.width + (x - Rect.x);

        public float DistanceAt(int index) => Mathf.Sqrt(DistanceSqr[index]);
    }

    public sealed class RoadFieldRasterizer
    {
        private const float MinSegmentLengthSqr = 1e-8f;
        private const float MinBlendWeight = 1e-6f;

        public RoadField Rasterize(TerrainGrid grid, IReadOnlyList<Vector3[]> polylines, float reach)
        {
            return Rasterize(grid, polylines, reach, 0f);
        }

        public RoadField Rasterize(TerrainGrid grid, IReadOnlyList<Vector3[]> polylines, float reach, float heightBlendRadius)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (!TryGetBounds(polylines, reach, out var min, out var max)) return null;

            var rect = grid.ClampedRect(min, max);
            if (rect.width <= 0 || rect.height <= 0) return null;

            var blendHeights = heightBlendRadius > 0f;
            var field = new RoadField(rect, blendHeights);

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length - 1; i++)
                {
                    StampSegment(grid, field, points[i], points[i + 1], reach, heightBlendRadius);
                }
            }

            if (blendHeights)
            {
                NormalizeBlendedHeights(field);
            }

            return field;
        }

        /// <summary>
        /// Fills thin unpainted strips that sit between two road arms (a merge, a hairpin, a junction),
        /// so the texture stays continuous where the heights are already blended.
        /// A cell is filled when there is painted road on both sides of one axis within <paramref name="maxGapWidth"/>.
        /// </summary>
        public int FillJunctionGaps(RoadField field, TerrainGrid grid, float paintedRadius, float maxGapWidth)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (paintedRadius <= 0f || maxGapWidth <= 0f) return 0;

            var rect = field.Rect;
            var width = rect.width;
            var height = rect.height;
            if (width <= 0 || height <= 0) return 0;

            var paintedRadiusSqr = paintedRadius * paintedRadius;
            var stepsX = Mathf.Max(1, Mathf.CeilToInt(maxGapWidth / grid.CellSize.x));
            var stepsZ = Mathf.Max(1, Mathf.CeilToInt(maxGapWidth / grid.CellSize.y));

            // Snapshot of the mask: a gap is closed based on the road that exists before the fill,
            // so the pass cannot chain-fill its way across the map.
            var painted = new bool[width * height];

            for (var i = 0; i < painted.Length; i++)
            {
                painted[i] = field.DistanceSqr[i] <= paintedRadiusSqr;
            }

            var filled = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (painted[index]) continue;

                    var horizontal = HasPaintedOnBothSides(painted, width, height, x, y, stepsX, 1, 0);
                    if (!horizontal && !HasPaintedOnBothSides(painted, width, height, x, y, stepsZ, 0, 1)) continue;

                    field.DistanceSqr[index] = 0f;
                    filled++;
                }
            }

            return filled;
        }

        private static bool HasPaintedOnBothSides(bool[] painted, int width, int height, int x, int y, int steps, int stepX, int stepY)
        {
            return HasPainted(painted, width, height, x, y, steps, -stepX, -stepY)
                   && HasPainted(painted, width, height, x, y, steps, stepX, stepY);
        }

        private static bool HasPainted(bool[] painted, int width, int height, int x, int y, int steps, int stepX, int stepY)
        {
            for (var i = 1; i <= steps; i++)
            {
                var nextX = x + stepX * i;
                var nextY = y + stepY * i;
                if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height) return false;
                if (painted[nextY * width + nextX]) return true;
            }

            return false;
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

        private void StampSegment(
            TerrainGrid grid,
            RoadField field,
            Vector3 start,
            Vector3 end,
            float reach,
            float heightBlendRadius)
        {
            var origin = new Vector2(start.x, start.z);
            var direction = new Vector2(end.x - start.x, end.z - start.z);
            var lengthSqr = direction.sqrMagnitude;
            var margin = new Vector2(reach, reach);
            var segmentRect = grid.ClampedRect(Vector2.Min(origin, origin + direction) - margin, Vector2.Max(origin, origin + direction) + margin);
            var reachSqr = reach * reach;
            var blendRadiusSqr = heightBlendRadius * heightBlendRadius;

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
                    var height = Mathf.Lerp(start.y, end.y, t);

                    if (field.BlendWeight != null && distanceSqr < blendRadiusSqr)
                    {
                        var weight = BlendWeight(distanceSqr, blendRadiusSqr);
                        field.Height[index] += height * weight;
                        field.BlendWeight[index] += weight;
                    }

                    if (distanceSqr >= field.DistanceSqr[index]) continue;

                    field.DistanceSqr[index] = distanceSqr;
                    field.Side[index] = (toCell.x - closest.x) * direction.y - (toCell.y - closest.y) * direction.x;

                    if (field.BlendWeight == null)
                    {
                        field.Height[index] = height;
                    }
                }
            }
        }

        private static float BlendWeight(float distanceSqr, float blendRadiusSqr)
        {
            if (blendRadiusSqr <= MinBlendWeight) return 0f;

            var normalized = 1f - Mathf.Sqrt(distanceSqr / blendRadiusSqr);
            return normalized * normalized * normalized;
        }

        private static void NormalizeBlendedHeights(RoadField field)
        {
            for (var i = 0; i < field.Height.Length; i++)
            {
                var weight = field.BlendWeight[i];
                field.Height[i] = weight > MinBlendWeight ? field.Height[i] / weight : 0f;
            }
        }
    }
}

#endif
