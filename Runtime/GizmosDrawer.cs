using UnityEngine;
using UnityEngine.Serialization;

namespace Plugins._EditorFreeTools.Runtime
{
    [AddComponentMenu("GizmosToolkit/Gizmos Drawer")]
    public class GizmosDrawer : MonoBehaviour
    {
        public enum GizmoShape { Cube, Sphere, Rect }

        [Header("General Settings")]
        public bool useColliders;
        public GizmoShape shapeType;
        [FormerlySerializedAs("size")] [Min(0)] 
        public float sizeRadius = 1f;
        public Color color = Color.blue;

        [Header("Axis Settings")]
        public bool showAxis;
        [Min(0)] public float axisLength = 0.65f;

        [Header("Connection Line")]
        public bool drawConnection;
        public Transform connectTo;

        [Header("Rect Settings")]
        public Vector3 rectSize = Vector3.one;

        [SerializeField] private Collider[] _colliders;
        [SerializeField] private bool _hasColliders;
        [SerializeField] private bool _initialized;

        [ContextMenu("Find Colliders")]
        private void FindColliders()
        {
            _colliders = GetComponents<Collider>();
            _hasColliders = _colliders != null && _colliders.Length > 0;
            if (_hasColliders) useColliders = true;
        }

        private void Reset()
        {
            FindColliders();
            color = new Color(Random.value, Random.value, Random.value, 1f);
        }

        private void OnValidate()
        {
            if (_initialized) return;
            FindColliders();
            _initialized = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!enabled) return;

            Color outlineColor = new(color.r, color.g, color.b, 1);
            Gizmos.color = outlineColor;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (showAxis)
            {
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.ArrowHandleCap(0, transform.position, transform.rotation, axisLength, EventType.Repaint);
            }

            if (drawConnection && connectTo)
            {
                Gizmos.color = color;
                Gizmos.DrawLine(transform.position, connectTo.position);
            }

            if (_hasColliders && useColliders)
            {
                DrawColliders(false);
                return;
            }

            DrawShape(outlineColor);
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;

            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (_hasColliders && useColliders)
            {
                DrawColliders(true);
                return;
            }

            switch (shapeType)
            {
                case GizmoShape.Cube:
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * sizeRadius);
                    break;
                case GizmoShape.Sphere:
                    Gizmos.DrawWireSphere(Vector3.zero, sizeRadius);
                    break;
                case GizmoShape.Rect:
                    Gizmos.DrawWireCube(Vector3.zero, rectSize * sizeRadius);
                    break;
            }
        }

        private void DrawShape(Color outlineColor)
        {
            switch (shapeType)
            {
                case GizmoShape.Cube:
                    Gizmos.color = outlineColor;
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * sizeRadius);
                    Gizmos.color = color;
                    Gizmos.DrawCube(Vector3.zero, Vector3.one * sizeRadius);
                    break;

                case GizmoShape.Sphere:
                    Gizmos.color = outlineColor;
                    Gizmos.DrawWireSphere(Vector3.zero, sizeRadius);
                    Gizmos.color = color;
                    Gizmos.DrawSphere(Vector3.zero, sizeRadius);
                    break;

                case GizmoShape.Rect:
                    Gizmos.color = outlineColor;
                    Gizmos.DrawWireCube(Vector3.zero, rectSize * sizeRadius);
                    Gizmos.color = color;
                    Gizmos.DrawCube(Vector3.zero, rectSize * sizeRadius);
                    break;
            }
        }

        private void DrawColliders(bool selected)
        {
            Color outlineColor = selected ? Color.yellow : new(color.r, color.g, color.b, 1);

            foreach (var col in _colliders)
            {
                if (!col || !col.enabled) continue;
                Gizmos.matrix = transform.localToWorldMatrix;

                switch (col)
                {
                    case BoxCollider box:
                        Gizmos.color = outlineColor;
                        Gizmos.DrawWireCube(box.center, box.size);
                        if (!selected)
                        {
                            Gizmos.color = color;
                            Gizmos.DrawCube(box.center, box.size);
                        }
                        break;

                    case SphereCollider sphere:
                        Gizmos.color = outlineColor;
                        Gizmos.DrawWireSphere(sphere.center, sphere.radius);
                        if (!selected)
                        {
                            Gizmos.color = color;
                            Gizmos.DrawSphere(sphere.center, sphere.radius);
                        }
                        break;

                    case CapsuleCollider capsule:
                        DrawCapsule(capsule, color, selected);
                        break;

                    case MeshCollider mesh when mesh.sharedMesh != null:
                        Gizmos.color = outlineColor;
                        Gizmos.DrawWireMesh(mesh.sharedMesh);
                        if (!selected)
                        {
                            Gizmos.color = color;
                            Gizmos.DrawMesh(mesh.sharedMesh);
                        }
                        break;
                }
            }
        }

        private void DrawCapsule(CapsuleCollider capsule, Color col, bool selected)
        {
            Gizmos.color = selected ? Color.yellow : col;

            Vector3 center = capsule.center;
            float radius = capsule.radius;
            float height = capsule.height;
            int dir = capsule.direction;

            Vector3 axis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
            float cylinderHeight = Mathf.Max(0, height - 2 * radius);

            Vector3 top = center + axis * (cylinderHeight / 2);
            Vector3 bottom = center - axis * (cylinderHeight / 2);

            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);
        }
#endif
    }
}
