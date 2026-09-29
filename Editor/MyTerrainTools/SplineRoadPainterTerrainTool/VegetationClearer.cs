using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public sealed class VegetationClearer
    {
        private const float MinBucketSize = 1f;
        private const float BucketFactor = 4f;
        private const float MinSegmentLengthSqr = 1e-8f;
        private const int KeyShift = 32;

        private readonly struct Segment
        {
            public Segment(Vector2 start, Vector2 end)
            {
                Start = start;
                End = end;
            }

            public Vector2 Start { get; }

            public Vector2 End { get; }
        }

        #region Details

        public void ClearDetails(TerrainData terrainData, RoadField field, float radius)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (field == null) throw new ArgumentNullException(nameof(field));

            var rect = field.Rect;
            var radiusSqr = radius * radius;
            var layerCount = terrainData.detailPrototypes.Length;

            for (var layer = 0; layer < layerCount; layer++)
            {
                var map = terrainData.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, layer);
                var changed = false;

                for (var y = 0; y < rect.height; y++)
                {
                    for (var x = 0; x < rect.width; x++)
                    {
                        if (field.DistanceSqr[y * rect.width + x] > radiusSqr) continue;
                        if (map[y, x] == 0) continue;

                        map[y, x] = 0;
                        changed = true;
                    }
                }

                if (changed)
                {
                    terrainData.SetDetailLayer(rect.x, rect.y, layer, map);
                }
            }
        }

        #endregion

        #region Trees

        public List<RemovedTree> ClearTrees(TerrainData terrainData, Vector3 terrainPosition, IReadOnlyList<Vector3[]> polylines, float radius)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            var removed = new List<RemovedTree>();
            var trees = terrainData.treeInstances;
            if (trees.Length == 0) return removed;

            var bucketSize = Mathf.Max(radius * BucketFactor, MinBucketSize);
            var segments = new List<Segment>();
            var buckets = new Dictionary<long, List<int>>();
            BuildIndex(polylines, radius, bucketSize, segments, buckets);

            var size = terrainData.size;
            var radiusSqr = radius * radius;
            var kept = new List<TreeInstance>(trees.Length);

            for (var i = 0; i < trees.Length; i++)
            {
                var point = new Vector2(terrainPosition.x + trees[i].position.x * size.x, terrainPosition.z + trees[i].position.z * size.z);

                if (IsInsideRadius(point, segments, buckets, bucketSize, radiusSqr))
                {
                    removed.Add(new RemovedTree(trees[i]));
                    continue;
                }

                kept.Add(trees[i]);
            }

            terrainData.SetTreeInstances(kept.ToArray(), true);

            return removed;
        }

        private void BuildIndex(IReadOnlyList<Vector3[]> polylines, float radius, float bucketSize, List<Segment> segments, Dictionary<long, List<int>> buckets)
        {
            var margin = new Vector2(radius, radius);

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length - 1; i++)
                {
                    var start = new Vector2(points[i].x, points[i].z);
                    var end = new Vector2(points[i + 1].x, points[i + 1].z);
                    var segmentIndex = segments.Count;
                    segments.Add(new Segment(start, end));

                    var min = Vector2.Min(start, end) - margin;
                    var max = Vector2.Max(start, end) + margin;
                    var minX = Mathf.FloorToInt(min.x / bucketSize);
                    var maxX = Mathf.FloorToInt(max.x / bucketSize);
                    var minY = Mathf.FloorToInt(min.y / bucketSize);
                    var maxY = Mathf.FloorToInt(max.y / bucketSize);

                    for (var y = minY; y <= maxY; y++)
                    {
                        for (var x = minX; x <= maxX; x++)
                        {
                            var key = KeyOf(x, y);

                            if (!buckets.TryGetValue(key, out var list))
                            {
                                list = new List<int>();
                                buckets[key] = list;
                            }

                            list.Add(segmentIndex);
                        }
                    }
                }
            }
        }

        private bool IsInsideRadius(Vector2 point, List<Segment> segments, Dictionary<long, List<int>> buckets, float bucketSize, float radiusSqr)
        {
            var key = KeyOf(Mathf.FloorToInt(point.x / bucketSize), Mathf.FloorToInt(point.y / bucketSize));
            if (!buckets.TryGetValue(key, out var list)) return false;

            for (var i = 0; i < list.Count; i++)
            {
                if (DistanceSqrToSegment(point, segments[list[i]]) <= radiusSqr) return true;
            }

            return false;
        }

        private float DistanceSqrToSegment(Vector2 point, Segment segment)
        {
            var direction = segment.End - segment.Start;
            var toPoint = point - segment.Start;
            var lengthSqr = direction.sqrMagnitude;
            var t = lengthSqr > MinSegmentLengthSqr ? Mathf.Clamp01(Vector2.Dot(toPoint, direction) / lengthSqr) : 0f;

            return (toPoint - direction * t).sqrMagnitude;
        }

        private long KeyOf(int x, int y) => ((long)x << KeyShift) | (uint)y;

        #endregion
    }
}
