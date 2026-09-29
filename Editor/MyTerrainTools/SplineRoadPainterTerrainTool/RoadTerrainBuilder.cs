using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public sealed class RoadTerrainBuilder
    {
        private const int NoLayer = -1;

        private readonly RoadFieldRasterizer _rasterizer = new RoadFieldRasterizer();
        private readonly VegetationClearer _vegetationClearer = new VegetationClearer();
        private readonly RoadsideDetailScatterer _detailScatterer = new RoadsideDetailScatterer();
        private readonly RoadsideTreeScatterer _treeScatterer = new RoadsideTreeScatterer();

        #region Build

        public void Build(Terrain terrain, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, TerrainSnapshot snapshot)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var terrainData = terrain.terrainData;
            var position = terrain.transform.position;
            var origin = new Vector2(position.x, position.z);
            var size = new Vector2(terrainData.size.x, terrainData.size.z);

            var heightGrid = new TerrainGrid(terrainData.heightmapResolution, origin, size, false);
            var heightField = _rasterizer.Rasterize(heightGrid, polylines, profile.Reach);
            if (heightField == null) return;

            RoadField alphaField = null;
            RoadField detailField = null;
            var layerIndex = NoLayer;

            if (profile.PaintTexture && profile.RoadLayer != null)
            {
                layerIndex = EnsureLayer(terrainData, profile.RoadLayer);
                var alphaGrid = new TerrainGrid(terrainData.alphamapResolution, origin, size, true);
                alphaField = _rasterizer.Rasterize(alphaGrid, polylines, profile.Reach);
            }

            if (HasDetails(terrainData) && (profile.ClearDetails || HasRoadsideRule(profile, RoadsideKind.Detail)))
            {
                var detailGrid = new TerrainGrid(terrainData.detailResolution, origin, size, true);
                detailField = _rasterizer.Rasterize(detailGrid, polylines, profile.Reach);
            }

            snapshot.Capture(terrainData, heightField.Rect, alphaField?.Rect ?? default, detailField?.Rect ?? default);

            ApplyHeights(terrainData, position.y, heightField, profile);

            if (alphaField != null)
            {
                ApplyTexture(terrainData, alphaField, profile, layerIndex);
            }

            if (detailField != null)
            {
                if (profile.ClearDetails)
                {
                    _vegetationClearer.ClearDetails(terrainData, detailField, profile.VegetationRadius);
                }

                if (HasRoadsideRule(profile, RoadsideKind.Detail))
                {
                    _detailScatterer.Scatter(terrainData, detailField, profile);
                }
            }

            if (profile.ClearTrees)
            {
                snapshot.StoreTrees(_vegetationClearer.ClearTrees(terrainData, position, polylines, profile.VegetationRadius));
            }

            if (HasRoadsideRule(profile, RoadsideKind.Tree) && terrainData.treePrototypes.Length > 0)
            {
                var index = new PolylineIndex(polylines, RoadsideTreeScatterer.KeepOutRadius(profile));
                snapshot.StorePlacedTrees(_treeScatterer.Scatter(terrainData, position, polylines, index, profile));
            }
        }

        private bool HasDetails(TerrainData terrainData)
        {
            return terrainData.detailResolution > 0 && terrainData.detailPrototypes.Length > 0;
        }

        private bool HasRoadsideRule(RoadProfile profile, RoadsideKind kind)
        {
            var rules = profile.Roadside;
            if (rules == null) return false;

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule != null && rule.Enabled && rule.Kind == kind) return true;
            }

            return false;
        }

        #endregion

        #region Heights

        private void ApplyHeights(TerrainData terrainData, float baseHeight, RoadField field, RoadProfile profile)
        {
            var rect = field.Rect;
            var heights = terrainData.GetHeights(rect.x, rect.y, rect.width, rect.height);
            var sizeY = terrainData.size.y;

            for (var y = 0; y < rect.height; y++)
            {
                for (var x = 0; x < rect.width; x++)
                {
                    var index = y * rect.width + x;
                    var weight = profile.HeightWeight(field.DistanceAt(index));
                    if (weight <= 0f) continue;

                    var target = Mathf.Clamp01((field.Height[index] + profile.HeightOffset - baseHeight) / sizeY);
                    heights[y, x] = Mathf.Lerp(heights[y, x], target, weight);
                }
            }

            terrainData.SetHeightsDelayLOD(rect.x, rect.y, heights);
            terrainData.SyncHeightmap();
        }

        #endregion

        #region Texture

        private void ApplyTexture(TerrainData terrainData, RoadField field, RoadProfile profile, int layerIndex)
        {
            var rect = field.Rect;
            var layers = terrainData.alphamapLayers;
            var alphas = terrainData.GetAlphamaps(rect.x, rect.y, rect.width, rect.height);

            for (var y = 0; y < rect.height; y++)
            {
                for (var x = 0; x < rect.width; x++)
                {
                    var weight = profile.TextureWeight(field.DistanceAt(y * rect.width + x));
                    if (weight <= 0f) continue;

                    for (var layer = 0; layer < layers; layer++)
                    {
                        var goal = layer == layerIndex ? 1f : 0f;
                        alphas[y, x, layer] = Mathf.Lerp(alphas[y, x, layer], goal, weight);
                    }
                }
            }

            terrainData.SetAlphamaps(rect.x, rect.y, alphas);
        }

        private int EnsureLayer(TerrainData terrainData, TerrainLayer roadLayer)
        {
            var layers = terrainData.terrainLayers;

            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i] == roadLayer) return i;
            }

            var expanded = new TerrainLayer[layers.Length + 1];
            Array.Copy(layers, expanded, layers.Length);
            expanded[layers.Length] = roadLayer;
            terrainData.terrainLayers = expanded;

            return layers.Length;
        }

        #endregion
    }
}
