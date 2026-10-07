using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

// Replaces Unity terrain trees with prefab GameObject instances.
// Original approach: http://answers.unity3d.com/questions/723266/converting-all-terrain-trees-to-gameobjects.html
public class TreeReplacerS : EditorWindow
{
    #region Constants

    private const string SavedTreesPrefix = "SAVED_TREES_TERRAIN_";
    private const string ConvertedTreesPrefix = "CONVERTED_TREES_TERRAIN_";
    private const string LegacyGeneratedPrefix = "TREES_GENERATED";
    private const int ProgressUpdateInterval = 64;
    private const string ProgressTitle = "Tree Replacer";
    private const string ConvertUndoName = "Convert terrain trees";

    #endregion

    #region Settings

    private enum SlopeMode
    {
        TerrainNormal,
        PhysicsRaycast
    }

    private sealed class ConversionStats
    {
        public int Processed;
        public int Instantiated;
        public int Skipped;
        public bool Cancelled;
    }

    private SlopeMode _slopeMode = SlopeMode.TerrainNormal;
    private bool _keepPrefabLinks = true;
    private bool _addIndexToNames = true;

    #endregion

    #region Window

    [MenuItem("Tools/Terrain/TreeReplacer")]
    private static void Init()
    {
        GetWindow<TreeReplacerS>();
    }

    private void OnGUI()
    {
        GUILayout.Label("Settings", EditorStyles.boldLabel);

        _slopeMode = (SlopeMode)EditorGUILayout.EnumPopup(
            new GUIContent("Slope Matching", "Terrain Normal samples the heightmap (fast). Physics Raycast reproduces the original raycast behaviour (slow on scenes with many colliders)."),
            _slopeMode);

        _keepPrefabLinks = EditorGUILayout.Toggle(
            new GUIContent("Keep Prefab Links", "On: real prefab instances, same as before. Off: plain clones, faster, without prefab connection."),
            _keepPrefabLinks);

        _addIndexToNames = EditorGUILayout.Toggle(
            new GUIContent("Add Index To Names", "Appends ' (index)' to every instance name. Turn off for a faster conversion."),
            _addIndexToNames);

        GUILayout.Space(6f);
        GUILayout.Label("Create", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Convert (Clear Previous)", GUILayout.Height(40f)))
        {
            ClearAll();
            ConvertAll();
        }

        GUI.backgroundColor = Color.red;

        if (GUILayout.Button("Clear AllTerrainTreeInstances", GUILayout.Height(40f)))
        {
            ClearAllTerrainTreeInstances();
        }

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = Color.green;

        if (GUILayout.Button("Save (Current Trees)", GUILayout.Height(40f)))
        {
            SaveTrees();
        }

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = Color.red;

        if (GUILayout.Button("Delete (Converted Trees)", GUILayout.Height(40f)))
        {
            DeleteConvertedTrees();
        }

        if (GUILayout.Button("Delete (Saved Trees)", GUILayout.Height(40f)))
        {
            DeleteSavedTrees();
        }

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();
    }

    #endregion

    #region Conversion

    public void ConvertAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(ProgressTitle, "Exit Play Mode before converting terrain trees.", "OK");
            return;
        }

        Terrain[] terrains = GetAllTerrains();
        System.Array.Sort(terrains, CompareTerrainsByName);

        int totalTrees = 0;

        for (int i = 0; i < terrains.Length; i++)
        {
            totalTrees += GetTreeCount(terrains[i]);
        }

        if (totalTrees == 0)
        {
            Debug.Log("[TreeReplacer] No terrain trees found to convert.");
            return;
        }

        ConversionStats stats = new ConversionStats();
        Stopwatch stopwatch = Stopwatch.StartNew();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(ConvertUndoName);

        try
        {
            for (int i = 0; i < terrains.Length && !stats.Cancelled; i++)
            {
                ConvertTerrain(terrains[i], ConvertedTreesPrefix + i, totalTrees, stats);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        stopwatch.Stop();

        string result = $"[TreeReplacer] Converted {stats.Instantiated} trees from {terrains.Length} terrain(s) in {stopwatch.Elapsed.TotalSeconds:F1}s ({stats.Skipped} skipped).";

        if (stats.Cancelled)
        {
            Debug.LogWarning(result + " Cancelled - partial results were kept.");
        }
        else if (stats.Skipped > 0)
        {
            Debug.LogWarning(result + " Some tree prototypes have no prefab assigned.");
        }
        else
        {
            Debug.Log(result);
        }
    }

    public void Convert(Terrain terrain, string parentName)
    {
        if (terrain == null || string.IsNullOrEmpty(parentName))
        {
            return;
        }

        ConversionStats stats = new ConversionStats();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(ConvertUndoName);

        try
        {
            ConvertTerrain(terrain, parentName, GetTreeCount(terrain), stats);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        if (stats.Cancelled)
        {
            Debug.LogWarning($"[TreeReplacer] Conversion cancelled after {stats.Instantiated} trees.");
        }
    }

    private void ConvertTerrain(Terrain terrain, string parentName, int totalTrees, ConversionStats stats)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            return;
        }

        TerrainData data = terrain.terrainData;
        TreeInstance[] trees = data.treeInstances;
        TreePrototype[] prototypes = data.treePrototypes;

        if (trees.Length == 0)
        {
            return;
        }

        Transform terrainTransform = terrain.transform;
        Vector3 terrainPosition = terrainTransform.position;
        Vector3 terrainSize = data.size;
        Quaternion terrainRotation = terrainTransform.rotation;
        Vector3 terrainUp = terrainTransform.up;
        float heightStep = 1f / data.heightmapResolution;

        Transform parentTransform = GetOrCreateParent(terrain, parentName).transform;

        for (int i = 0; i < trees.Length; i++)
        {
            if (i % ProgressUpdateInterval == 0 && DisplayConversionProgress(totalTrees, stats.Processed, terrain))
            {
                stats.Cancelled = true;
                return;
            }

            TreeInstance tree = trees[i];
            GameObject prefab = ResolvePrefab(prototypes, tree.prototypeIndex);
            stats.Processed++;

            if (prefab == null)
            {
                stats.Skipped++;
                continue;
            }

            Vector3 position = Vector3.Scale(tree.position, terrainSize) + terrainPosition;
            GameObject instance = CreateInstance(prefab, parentTransform);

            if (instance == null)
            {
                stats.Skipped++;
                continue;
            }

            Transform instanceTransform = instance.transform;
            Quaternion finalRotation = ComputeFinalRotation(tree, instanceTransform.rotation, data, terrainRotation, terrainUp, terrainSize, heightStep, position);
            instanceTransform.SetPositionAndRotation(position, finalRotation);
            instanceTransform.localScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);

            if (_addIndexToNames)
            {
                instance.name = prefab.name + " (" + i + ")";
            }

            stats.Instantiated++;
        }
    }

    private GameObject CreateInstance(GameObject prefab, Transform parent)
    {
        if (_keepPrefabLinks)
        {
            GameObject prefabInstance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;

            if (prefabInstance != null)
            {
                return prefabInstance;
            }
        }

        return Object.Instantiate(prefab, parent) as GameObject;
    }

    private Quaternion ComputeFinalRotation(TreeInstance tree, Quaternion initialRotation, TerrainData data, Quaternion terrainRotation, Vector3 terrainUp, Vector3 terrainSize, float heightStep, Vector3 worldPosition)
    {
        float yaw = Random.Range(0, 360);
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, initialRotation * Vector3.up);
        Vector3 slopeNormal = GetSlopeNormal(tree, data, terrainRotation, terrainSize, heightStep, worldPosition);
        Quaternion slopeRotation = Quaternion.FromToRotation(terrainUp, slopeNormal);
        return slopeRotation * yawRotation * initialRotation;
    }

    private Vector3 GetSlopeNormal(TreeInstance tree, TerrainData data, Quaternion terrainRotation, Vector3 terrainSize, float heightStep, Vector3 worldPosition)
    {
        if (_slopeMode == SlopeMode.PhysicsRaycast)
        {
            return Physics.Raycast(worldPosition, Vector3.down, out RaycastHit hit) ? hit.normal : terrainRotation * Vector3.up;
        }

        float x = tree.position.x;
        float z = tree.position.z;
        float xMin = Mathf.Max(0f, x - heightStep);
        float xMax = Mathf.Min(1f, x + heightStep);
        float zMin = Mathf.Max(0f, z - heightStep);
        float zMax = Mathf.Min(1f, z + heightStep);
        float spanX = xMax - xMin;
        float spanZ = zMax - zMin;

        float slopeX = spanX > 0f ? (data.GetInterpolatedHeight(xMax, z) - data.GetInterpolatedHeight(xMin, z)) / spanX : 0f;
        float slopeZ = spanZ > 0f ? (data.GetInterpolatedHeight(x, zMax) - data.GetInterpolatedHeight(x, zMin)) / spanZ : 0f;

        Vector3 normal = new Vector3(-slopeX / terrainSize.x, 1f, -slopeZ / terrainSize.z);
        normal.Normalize();
        return terrainRotation * normal;
    }

    private static GameObject ResolvePrefab(TreePrototype[] prototypes, int prototypeIndex)
    {
        if (prototypeIndex < 0 || prototypeIndex >= prototypes.Length)
        {
            return null;
        }

        return prototypes[prototypeIndex].prefab;
    }

    private static GameObject GetOrCreateParent(Terrain terrain, string parentName)
    {
        Transform existing = terrain.transform.Find(parentName);

        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject parent = new GameObject(parentName);
        parent.transform.SetParent(terrain.transform, false);
        return parent;
    }

    #endregion

    #region Cleanup

    public void ClearAll()
    {
        int destroyed = DestroyGroupsByPrefix(ConvertedTreesPrefix);
        destroyed += DestroyGroupsByPrefix(LegacyGeneratedPrefix);
        Debug.Log($"[TreeReplacer] Cleared {destroyed} generated group(s).");
    }

    public void ClearAllTerrainTreeInstances()
    {
        Terrain[] terrains = GetAllTerrains();
        int cleared = 0;

        for (int i = 0; i < terrains.Length; i++)
        {
            TerrainData data = terrains[i].terrainData;

            if (data == null || data.treeInstanceCount == 0)
            {
                continue;
            }

            data.treeInstances = System.Array.Empty<TreeInstance>();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            cleared++;
        }

        Debug.Log($"[TreeReplacer] Cleared terrain tree instances on {cleared} terrain(s).");
    }

    public void SaveTrees()
    {
        Terrain[] terrains = GetAllTerrains();
        int renamed = 0;

        for (int i = 0; i < terrains.Length; i++)
        {
            Transform terrainTransform = terrains[i].transform;

            for (int childIndex = terrainTransform.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = terrainTransform.GetChild(childIndex);

                if (child.name.StartsWith(ConvertedTreesPrefix, System.StringComparison.Ordinal))
                {
                    child.name = SavedTreesPrefix + child.name.Substring(ConvertedTreesPrefix.Length);
                    renamed++;
                }
            }
        }

        Debug.Log($"[TreeReplacer] Saved {renamed} converted group(s) as saved trees.");
    }

    public void DeleteConvertedTrees()
    {
        int destroyed = DestroyGroupsByPrefix(ConvertedTreesPrefix);
        Debug.Log($"[TreeReplacer] Deleted {destroyed} converted group(s).");
    }

    public void DeleteSavedTrees()
    {
        int destroyed = DestroyGroupsByPrefix(SavedTreesPrefix);
        Debug.Log($"[TreeReplacer] Deleted {destroyed} saved group(s).");
    }

    private static int DestroyGroupsByPrefix(string prefix)
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        int destroyed = 0;

        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform candidate = transforms[i];

            if (candidate == null || !candidate.name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                continue;
            }

            Object.DestroyImmediate(candidate.gameObject);
            destroyed++;
        }

        return destroyed;
    }

    #endregion

    #region Helpers

    private static Terrain[] GetAllTerrains()
    {
        return Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
    }

    private static int GetTreeCount(Terrain terrain)
    {
        return terrain != null && terrain.terrainData != null ? terrain.terrainData.treeInstanceCount : 0;
    }

    private static int CompareTerrainsByName(Terrain a, Terrain b)
    {
        return string.CompareOrdinal(a.name, b.name);
    }

    private static bool DisplayConversionProgress(int totalTrees, int processed, Terrain terrain)
    {
        float progress = totalTrees > 0 ? (float)processed / totalTrees : 0f;
        string info = terrain.name + ": " + processed + " / " + totalTrees + " trees";
        return EditorUtility.DisplayCancelableProgressBar(ProgressTitle, info, progress);
    }

    #endregion
}
