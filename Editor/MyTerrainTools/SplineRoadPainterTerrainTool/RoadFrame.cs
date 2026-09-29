#if UNITY_EDITOR
using UnityEngine;

namespace NextnityStudio.SplineRoad.EditorTools
{
    public readonly struct RoadFrame
    {
        private const float MinDirectionSqr = 1e-8f;

        public RoadFrame(Vector3[] points, int index)
        {
            Center = points[index];

            var before = points[Mathf.Max(index - 1, 0)];
            var after = points[Mathf.Min(index + 1, points.Length - 1)];
            var forward = new Vector3(after.x - before.x, 0f, after.z - before.z);

            if (forward.sqrMagnitude < MinDirectionSqr)
            {
                Right = Vector3.right;
                return;
            }

            forward.Normalize();
            Right = new Vector3(forward.z, 0f, -forward.x);
        }

        public Vector3 Center { get; }

        public Vector3 Right { get; }

        public Vector3 Offset(float distance, float y)
        {
            return new Vector3(Center.x + Right.x * distance, y, Center.z + Right.z * distance);
        }

        public float Distance(Vector3 position)
        {
            return Vector3.Dot(position - Center, Right);
        }
    }
}

#endif
