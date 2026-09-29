using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public sealed class PolylineIndex
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

        private readonly List<Segment> _segments = new List<Segment>();
        private readonly Dictionary<long, List<int>> _buckets = new Dictionary<long, List<int>>();
        private readonly float _indexRadius;
        private readonly float _bucketSize;

        public PolylineIndex(IReadOnlyList<Vector3[]> polylines, float indexRadius)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            _indexRadius = indexRadius;
            _bucketSize = Mathf.Max(indexRadius * BucketFactor, MinBucketSize);

            Build(polylines);
        }

        public bool IsWithin(Vector2 point, float radius)
        {
            var clamped = Mathf.Min(radius, _indexRadius);
            var radiusSqr = clamped * clamped;
            var key = KeyOf(Mathf.FloorToInt(point.x / _bucketSize), Mathf.FloorToInt(point.y / _bucketSize));

            if (!_buckets.TryGetValue(key, out var list)) return false;

            for (var i = 0; i < list.Count; i++)
            {
                if (DistanceSqrToSegment(point, _segments[list[i]]) <= radiusSqr) return true;
            }

            return false;
        }

        private void Build(IReadOnlyList<Vector3[]> polylines)
        {
            var margin = new Vector2(_indexRadius, _indexRadius);

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length - 1; i++)
                {
                    var start = new Vector2(points[i].x, points[i].z);
                    var end = new Vector2(points[i + 1].x, points[i + 1].z);
                    var segmentIndex = _segments.Count;
                    _segments.Add(new Segment(start, end));

                    var min = Vector2.Min(start, end) - margin;
                    var max = Vector2.Max(start, end) + margin;
                    var minX = Mathf.FloorToInt(min.x / _bucketSize);
                    var maxX = Mathf.FloorToInt(max.x / _bucketSize);
                    var minY = Mathf.FloorToInt(min.y / _bucketSize);
                    var maxY = Mathf.FloorToInt(max.y / _bucketSize);

                    for (var y = minY; y <= maxY; y++)
                    {
                        for (var x = minX; x <= maxX; x++)
                        {
                            AddToBucket(KeyOf(x, y), segmentIndex);
                        }
                    }
                }
            }
        }

        private void AddToBucket(long key, int segmentIndex)
        {
            if (!_buckets.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _buckets[key] = list;
            }

            list.Add(segmentIndex);
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
    }
}
