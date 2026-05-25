#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TerrainGlobalSyncEditor
{
    [MenuItem("CONTEXT/Terrain/Sync Children Terrains Settings")]
    private static void SyncChildrenTerrainsSettings(MenuCommand command)
    {
        Terrain masterTerrain = command.context as Terrain;

        if (masterTerrain == null)
        {
            Debug.LogWarning("No master Terrain selected.");
            return;
        }

        Transform parent = masterTerrain.transform.parent;

        if (parent == null)
        {
            Debug.LogWarning("Master Terrain has no parent.");
            return;
        }

        Terrain[] terrains = parent.GetComponentsInChildren<Terrain>(true);

        if (terrains.Length < 2)
        {
            Debug.LogWarning("Need at least 2 terrains under parent.");
            return;
        }

        TerrainData masterData = masterTerrain.terrainData;

        Undo.RecordObjects(terrains, "Sync Terrain Settings");

        foreach (Terrain t in terrains)
        {
            if (t == null || t == masterTerrain) continue;

            TerrainData targetData = t.terrainData;

            //--------------------------------
            // TERRAIN COMPONENT SETTINGS
            //--------------------------------
            t.drawHeightmap = masterTerrain.drawHeightmap;
            t.drawTreesAndFoliage = masterTerrain.drawTreesAndFoliage;
            t.heightmapPixelError = masterTerrain.heightmapPixelError;
            t.basemapDistance = masterTerrain.basemapDistance;
            t.shadowCastingMode = masterTerrain.shadowCastingMode;
            t.detailObjectDistance = masterTerrain.detailObjectDistance;
            t.detailObjectDensity = masterTerrain.detailObjectDensity;
            t.treeDistance = masterTerrain.treeDistance;
            t.treeBillboardDistance = masterTerrain.treeBillboardDistance;
            t.treeCrossFadeLength = masterTerrain.treeCrossFadeLength;
            t.treeMaximumFullLODCount = masterTerrain.treeMaximumFullLODCount;
            t.materialTemplate = masterTerrain.materialTemplate;
            t.reflectionProbeUsage = masterTerrain.reflectionProbeUsage;
            t.allowAutoConnect = masterTerrain.allowAutoConnect;
            t.groupingID = masterTerrain.groupingID;

            //--------------------------------
            // SHARED TERRAIN DATA REFERENCES
            //--------------------------------
            targetData.terrainLayers = masterData.terrainLayers;
            targetData.detailPrototypes = masterData.detailPrototypes;
            targetData.treePrototypes = masterData.treePrototypes;

            //--------------------------------
            // GRASS SETTINGS
            //--------------------------------
            targetData.wavingGrassStrength = masterData.wavingGrassStrength;
            targetData.wavingGrassAmount = masterData.wavingGrassAmount;
            targetData.wavingGrassSpeed = masterData.wavingGrassSpeed;
            targetData.wavingGrassTint = masterData.wavingGrassTint;

            EditorUtility.SetDirty(t);
            EditorUtility.SetDirty(targetData);

            t.Flush();
        }

        Debug.Log($"Synced {terrains.Length - 1} terrains from master: {masterTerrain.name}");
    }
}
#endif