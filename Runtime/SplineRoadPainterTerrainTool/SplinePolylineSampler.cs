#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NextnityStudio.SplineRoad
{
    public sealed class SplinePolylineSampler
    {
        private const int MinKnots = 2;
        private const int MinSamples = 2;
        private const int MaxSamples = 16384;
        private const int SmoothingNeighbours = 3;

        public List<Vector3[]> Sample(SplineContainer container, Terrain terrain, RoadProfile profile)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var result = new List<Vector3[]>();
            var containerTransform = container.transform;
            var matrix = containerTransform.localToWorldMatrix;
            var lossyScale = containerTransform.lossyScale;
            var scaleFactor = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));

            foreach (var spline in container.Splines)
            {
                if (spline.Count < MinKnots) continue;

                var points = SampleSpline(spline, matrix, scaleFactor, profile.SampleSpacing);

                if (profile.FollowTerrain && terrain != null)
                {
                    ProjectOnTerrain(points, terrain);
                    SmoothHeights(points, profile.SmoothingPasses);
                }

                result.Add(points);
            }

            return result;
        }

        private Vector3[] SampleSpline(Spline spline, Matrix4x4 matrix, float scaleFactor, float spacing)
        {
            var length = spline.GetLength() * scaleFactor;
            var count = Mathf.Clamp(Mathf.CeilToInt(length / spacing) + 1, MinSamples, MaxSamples);
            var lastIndex = count - 1;
            var points = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                Vector3 local = SplineUtility.EvaluatePosition(spline, i / (float)lastIndex);
                points[i] = matrix.MultiplyPoint3x4(local);
            }

            return points;
        }

        private void ProjectOnTerrain(Vector3[] points, Terrain terrain)
        {
            var baseHeight = terrain.transform.position.y;

            for (var i = 0; i < points.Length; i++)
            {
                points[i].y = terrain.SampleHeight(points[i]) + baseHeight;
            }
        }

        private void SmoothHeights(Vector3[] points, int passes)
        {
            if (points.Length <= SmoothingNeighbours - 1) return;

            var buffer = new float[points.Length];
            var last = points.Length - 1;

            for (var pass = 0; pass < passes; pass++)
            {
                buffer[0] = points[0].y;
                buffer[last] = points[last].y;

                for (var i = 1; i < last; i++)
                {
                    buffer[i] = (points[i - 1].y + points[i].y + points[i + 1].y) / SmoothingNeighbours;
                }

                for (var i = 0; i < points.Length; i++)
                {
                    points[i].y = buffer[i];
                }
            }
        }
    }
}

#endif
