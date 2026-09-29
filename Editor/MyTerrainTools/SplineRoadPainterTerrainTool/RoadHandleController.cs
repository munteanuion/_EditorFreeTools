#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NextnityStudio.SplineRoad.EditorTools
{
    public sealed class RoadHandleController
    {
        private const float HandleSizeScale = 0.12f;
        private const float LabelLift = 1.6f;
        private const float MinHalfWidth = 0.25f;
        private const float HalfToFull = 2f;
        private const float WidthAnchor = 0.5f;
        private const float ShoulderAnchor = 0.5f;
        private const float HeightAnchor = 0.5f;
        private const float VegetationAnchor = 0.35f;
        private const float TextureAnchor = 0.65f;

        private readonly Color _shoulderColor = new Color(1f, 0.6f, 0.1f);

        public void Draw(IReadOnlyList<Vector3[]> polylines, SerializedObject dataObject, RoadProfile profile)
        {
            if (polylines == null || polylines.Count == 0 || dataObject == null || profile == null) return;

            var points = polylines[0];
            if (points.Length == 0) return;

            DrawWidth(FrameAt(points, WidthAnchor), profile, dataObject);
            DrawShoulder(FrameAt(points, ShoulderAnchor), profile, dataObject);
            DrawHeightOffset(FrameAt(points, HeightAnchor), profile, dataObject);

            if (profile.ClearDetails || profile.ClearTrees)
            {
                DrawVegetation(FrameAt(points, VegetationAnchor), profile, dataObject);
            }

            if (profile.PaintTexture)
            {
                DrawTexture(FrameAt(points, TextureAnchor), profile, dataObject);
            }
        }

        private void DrawWidth(RoadFrame frame, RoadProfile profile, SerializedObject dataObject)
        {
            var target = TargetHeight(frame, profile);
            var position = frame.Offset(profile.HalfWidth, target);
            var moved = Slide(position, frame.Right, Color.cyan, $"Width {profile.HalfWidth * HalfToFull:0.0} m");
            if (moved == position) return;

            var half = Mathf.Max(MinHalfWidth, frame.Distance(moved));
            SetFloat(dataObject, RoadProfile.WidthFieldName, half * HalfToFull);
        }

        private void DrawShoulder(RoadFrame frame, RoadProfile profile, SerializedObject dataObject)
        {
            var target = TargetHeight(frame, profile);
            var position = frame.Offset(profile.HalfWidth + profile.ShoulderWidth, target);
            var moved = Slide(position, frame.Right, _shoulderColor, $"Shoulder {profile.ShoulderWidth:0.0} m");
            if (moved == position) return;

            SetFloat(dataObject, RoadProfile.ShoulderFieldName, Mathf.Max(0f, frame.Distance(moved) - profile.HalfWidth));
        }

        private void DrawVegetation(RoadFrame frame, RoadProfile profile, SerializedObject dataObject)
        {
            var target = TargetHeight(frame, profile);
            var position = frame.Offset(-profile.VegetationRadius, target);
            var moved = Slide(position, -frame.Right, Color.green, $"Clearance {profile.VegetationClearance:0.0} m");
            if (moved == position) return;

            SetFloat(dataObject, RoadProfile.VegetationClearanceFieldName, Mathf.Max(0f, -frame.Distance(moved) - profile.HalfWidth));
        }

        private void DrawTexture(RoadFrame frame, RoadProfile profile, SerializedObject dataObject)
        {
            var target = TargetHeight(frame, profile);
            var position = frame.Offset(-(profile.HalfWidth + profile.TextureFeather), target);
            var moved = Slide(position, -frame.Right, Color.yellow, $"Feather {profile.TextureFeather:0.0} m");
            if (moved == position) return;

            SetFloat(dataObject, RoadProfile.TextureFeatherFieldName, Mathf.Max(0f, -frame.Distance(moved) - profile.HalfWidth));
        }

        private void DrawHeightOffset(RoadFrame frame, RoadProfile profile, SerializedObject dataObject)
        {
            var position = frame.Offset(0f, TargetHeight(frame, profile));
            var moved = Slide(position, Vector3.up, Color.white, $"Height offset {profile.HeightOffset:0.00} m");
            if (moved == position) return;

            SetFloat(dataObject, RoadProfile.HeightOffsetFieldName, moved.y - frame.Center.y);
        }

        private Vector3 Slide(Vector3 position, Vector3 direction, Color color, string label)
        {
            var size = HandleUtility.GetHandleSize(position) * HandleSizeScale;

            Handles.color = color;
            Handles.Label(position + Vector3.up * size * LabelLift, label);

            return Handles.Slider(position, direction, size, Handles.ConeHandleCap, 0f);
        }

        private RoadFrame FrameAt(Vector3[] points, float fraction)
        {
            return new RoadFrame(points, Mathf.RoundToInt(fraction * (points.Length - 1)));
        }

        private float TargetHeight(RoadFrame frame, RoadProfile profile)
        {
            return frame.Center.y + profile.HeightOffset;
        }

        private void SetFloat(SerializedObject dataObject, string fieldName, float value)
        {
            dataObject.FindProperty($"{SplineRoadData.ProfileFieldName}.{fieldName}").floatValue = value;
        }
    }
}

#endif
