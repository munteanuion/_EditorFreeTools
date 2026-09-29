using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NextnityStudio.SplineRoad.EditorTools
{
    [CustomEditor(typeof(SplineRoadPainter))]
    public sealed class SplineRoadPainterEditor : Editor
    {
        private const string ApplyUndoName = "Apply Spline Road";
        private const string RevertUndoName = "Revert Spline Road";
        private const string BakeUndoName = "Bake Spline Road";
        private const string LoadUndoName = "Restore Saved Spline";
        private const string DefaultAssetName = "SplineRoadData";
        private const string LegendText = "Cyan: road width | Orange: shoulder | Yellow: texture feather | Green: vegetation clearing | Red: terrain cut | Blue: terrain fill | Magenta: roadside bands + tree placements";
        private const float ApplyButtonHeight = 32f;

        private readonly RoadPreviewDrawer _drawer = new RoadPreviewDrawer();
        private readonly RoadHandleController _handles = new RoadHandleController();
        private List<Vector3[]> _polylines;

        public override void OnInspectorGUI()
        {
            var painter = (SplineRoadPainter)target;

            if (DrawDefaultInspector())
            {
                SceneView.RepaintAll();
            }

            if (painter.Data == null)
            {
                DrawCreateData();
                return;
            }

            DrawProfile(painter.Data);
            EditorGUILayout.HelpBox(LegendText, MessageType.None);
            DrawStatus(painter);
            DrawButtons(painter);
        }

        private void OnSceneGUI()
        {
            var painter = (SplineRoadPainter)target;
            if (!painter.IsReady || !painter.Preview.Enabled) return;

            if (Event.current.type == EventType.Repaint)
            {
                _polylines = painter.SamplePolylines();
                _drawer.Draw(_polylines, painter.Terrain, painter.Data.Profile, painter.Preview);
            }

            if (!painter.Preview.ShowHandles || _polylines == null || _polylines.Count == 0) return;

            using var dataObject = new SerializedObject(painter.Data);

            dataObject.Update();
            _handles.Draw(_polylines, dataObject, painter.Data.Profile);
            dataObject.ApplyModifiedProperties();
        }

        private void DrawCreateData()
        {
            EditorGUILayout.HelpBox("Assign a Spline Road Data asset or create a new one.", MessageType.Info);

            if (!GUILayout.Button("Create Data Asset")) return;

            var path = EditorUtility.SaveFilePanelInProject("Create Spline Road Data", DefaultAssetName, "asset", "Choose where to save the road data.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<SplineRoadData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            serializedObject.FindProperty(SplineRoadPainter.DataFieldName).objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProfile(SplineRoadData data)
        {
            using var dataObject = new SerializedObject(data);

            dataObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(dataObject.FindProperty(SplineRoadData.ProfileFieldName), true);
            var changed = EditorGUI.EndChangeCheck();
            dataObject.ApplyModifiedProperties();

            if (changed)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawStatus(SplineRoadPainter painter)
        {
            var message = painter.HasAppliedRoad
                ? "Road applied. Revert restores the original terrain, Bake makes it permanent."
                : "No active road on the terrain.";

            EditorGUILayout.HelpBox(message, MessageType.None);
        }

        private void DrawButtons(SplineRoadPainter painter)
        {
            using (new EditorGUI.DisabledScope(!painter.IsReady))
            {
                if (GUILayout.Button("Apply Road", GUILayout.Height(ApplyButtonHeight)))
                {
                    RunOnTerrain(painter, ApplyUndoName, painter.ApplyRoad);
                }

                using (new EditorGUI.DisabledScope(!painter.HasAppliedRoad))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Revert"))
                    {
                        RunOnTerrain(painter, RevertUndoName, painter.RevertRoad);
                    }

                    if (GUILayout.Button("Bake"))
                    {
                        RunOnTerrain(painter, BakeUndoName, painter.BakeRoad);
                    }
                }

                using (new EditorGUI.DisabledScope(!painter.Data.HasSavedSpline))
                {
                    if (GUILayout.Button("Restore Saved Spline"))
                    {
                        RestoreSpline(painter);
                    }
                }
            }
        }

        private void RunOnTerrain(SplineRoadPainter painter, string undoName, Action action)
        {
            var terrainData = painter.Terrain.terrainData;

            Undo.RegisterCompleteObjectUndo(new Object[] { terrainData, painter.Data }, undoName);
            action();

            EditorUtility.SetDirty(terrainData);
            EditorUtility.SetDirty(painter.Data);
            SceneView.RepaintAll();
        }

        private void RestoreSpline(SplineRoadPainter painter)
        {
            Undo.RecordObject(painter.transform, LoadUndoName);
            Undo.RecordObject(painter.Container, LoadUndoName);

            painter.LoadSavedSpline();
            SceneView.RepaintAll();
        }
    }
}
