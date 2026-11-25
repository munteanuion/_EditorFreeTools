#if UNITY_EDITOR
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
        private bool _showAppliedTiles;

        public override void OnInspectorGUI()
        {
            var painter = (SplineRoadPainter)target;

            serializedObject.Update();

            DrawTerrains(painter);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(SplineRoadPainter.DataFieldName));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(SplineRoadPainter.PreviewFieldName));

            if (serializedObject.ApplyModifiedProperties())
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
            DrawAppliedTiles(painter);
            DrawButtons(painter);
        }

        private void OnSceneGUI()
        {
            var painter = (SplineRoadPainter)target;
            if (!painter.IsReady || !painter.Preview.Enabled) return;

            var tiles = painter.ResolveTiles();
            if (tiles.Count == 0) return;

            if (Event.current.type == EventType.Repaint)
            {
                _polylines = painter.SamplePolylines();
                _drawer.Draw(_polylines, tiles, painter.Data.Profile, painter.Preview);
            }

            if (!painter.Preview.ShowHandles || _polylines == null || _polylines.Count == 0) return;

            using var dataObject = new SerializedObject(painter.Data);

            dataObject.Update();
            _handles.Draw(_polylines, dataObject, painter.Data.Profile);
            dataObject.ApplyModifiedProperties();
        }

        private void DrawTerrains(SplineRoadPainter painter)
        {
            var property = serializedObject.FindProperty(SplineRoadPainter.TerrainsFieldName);

            EditorGUILayout.PropertyField(property, new GUIContent("Terrains", "Terrain tiles the road is painted on. Tiles the spline does not cross are skipped."), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Scene Terrains"))
                {
                    AddTerrains(FindSceneTerrains());
                }

                using (new EditorGUI.DisabledScope(Selection.gameObjects.Length == 0))
                {
                    if (GUILayout.Button("Add Selected"))
                    {
                        AddTerrains(FindSelectedTerrains());
                    }
                }

                using (new EditorGUI.DisabledScope(property.arraySize == 0))
                {
                    if (GUILayout.Button("Clear"))
                    {
                        property.ClearArray();
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            }

            if (painter.HasLegacyTerrain)
            {
                EditorGUILayout.HelpBox($"Using the legacy single terrain '{painter.Terrain.name}'. Press Add Scene Terrains to move it into the list.", MessageType.Info);
            }
        }

        private static Terrain[] FindSceneTerrains()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            Array.Sort(terrains, CompareByPosition);

            return terrains;
        }

        private static Terrain[] FindSelectedTerrains()
        {
            var found = new List<Terrain>();

            foreach (var gameObject in Selection.gameObjects)
            {
                var terrain = gameObject.GetComponentInParent<Terrain>();
                if (terrain != null && !found.Contains(terrain))
                {
                    found.Add(terrain);
                }
            }

            found.Sort(CompareByPosition);

            return found.ToArray();
        }

        private static int CompareByPosition(Terrain left, Terrain right)
        {
            var leftPosition = left.transform.position;
            var rightPosition = right.transform.position;
            var row = rightPosition.z.CompareTo(leftPosition.z);

            return row != 0 ? row : leftPosition.x.CompareTo(rightPosition.x);
        }

        private void AddTerrains(Terrain[] terrains)
        {
            var property = serializedObject.FindProperty(SplineRoadPainter.TerrainsFieldName);
            var legacy = serializedObject.FindProperty(SplineRoadPainter.LegacyTerrainFieldName);
            var legacyTerrain = legacy.objectReferenceValue as Terrain;
            var changed = false;

            if (legacyTerrain != null && !Contains(property, legacyTerrain))
            {
                Append(property, legacyTerrain);
                changed = true;
            }

            for (var i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null || Contains(property, terrain)) continue;

                Append(property, terrain);
                changed = true;
            }

            if (property.arraySize > 0 && legacy.objectReferenceValue != null)
            {
                legacy.objectReferenceValue = null;
                changed = true;
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static bool Contains(SerializedProperty array, Terrain terrain)
        {
            for (var i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == terrain) return true;
            }

            return false;
        }

        private static void Append(SerializedProperty array, Terrain terrain)
        {
            array.arraySize++;
            array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = terrain;
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
                ? $"Road applied on {painter.Data.SnapshotTerrainCount} of {painter.ResolveTerrains().Count} terrain tile(s). Revert restores every touched tile, Bake makes it permanent."
                : $"No active road on the {painter.ResolveTerrains().Count} assigned terrain tile(s).";

            EditorGUILayout.HelpBox(message, MessageType.None);
        }

        private void DrawAppliedTiles(SplineRoadPainter painter)
        {
            var entries = painter.Data.TerrainSnapshots;
            if (entries.Count == 0) return;

            _showAppliedTiles = EditorGUILayout.Foldout(_showAppliedTiles, $"Tiles with an active road ({painter.Data.SnapshotTerrainCount})", true);
            if (!_showAppliedTiles) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var terrainData = entry?.TerrainData;
                    var label = terrainData != null ? terrainData.name : "Missing terrain data";

                    EditorGUILayout.LabelField(label, entry != null && entry.HasData ? "painted" : "empty");
                }
            }
        }

        private void DrawButtons(SplineRoadPainter painter)
        {
            using (new EditorGUI.DisabledScope(!painter.IsReady))
            {
                if (GUILayout.Button("Apply Road", GUILayout.Height(ApplyButtonHeight)))
                {
                    RunOnTerrains(painter, ApplyUndoName, painter.ApplyRoad);
                }

                using (new EditorGUI.DisabledScope(!painter.HasAppliedRoad))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Revert"))
                    {
                        RunOnTerrains(painter, RevertUndoName, painter.RevertRoad);
                    }

                    if (GUILayout.Button("Bake"))
                    {
                        RunOnTerrains(painter, BakeUndoName, painter.BakeRoad);
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

        private void RunOnTerrains(SplineRoadPainter painter, string undoName, Action action)
        {
            var terrains = new List<Terrain>(painter.ResolveTerrains());
            var targets = new List<Object>(terrains.Count + 1);

            for (var i = 0; i < terrains.Count; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null) continue;

                targets.Add(terrain.terrainData);
            }

            targets.Add(painter.Data);
            Undo.RegisterCompleteObjectUndo(targets.ToArray(), undoName);

            action();

            for (var i = 0; i < terrains.Count; i++)
            {
                var terrain = terrains[i];
                if (terrain != null && terrain.terrainData != null)
                {
                    EditorUtility.SetDirty(terrain.terrainData);
                }
            }

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

#endif
