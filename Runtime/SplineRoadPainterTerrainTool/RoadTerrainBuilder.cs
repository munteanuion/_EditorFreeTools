#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public sealed class RoadTerrainBuilder
    {
        private const int NoLayer = -1;

        /// <summary>Called before each tile is built. Return false to stop the pass (user cancelled).</summary>
        public delegate bool RoadBuildStep(int completedSteps, string step);

        private readonly RoadFieldRasterizer _rasterizer = new RoadFieldRasterizer();
        private readonly VegetationClearer _vegetationClearer = new VegetationClearer();
        private readonly RoadsideDetailScatterer _detailScatterer = new RoadsideDetailScatterer();
        private readonly RoadsideTreeScatterer _treeScatterer = new RoadsideTreeScatterer();

        /// <summary>
        /// Shared state of one apply pass. Kept across tiles so tree spacing and the road keep-out
        /// are measured in world space instead of per tile.
        /// </summary>
        private sealed class BuildContext
        {
            public BuildContext(IReadOnlyList<Vector3[]> polylines, RoadProfile profile)
            {
                if (!RoadsideTreeScatterer.HasTreeRules(profile)) return;

                TreeIndex = new PolylineIndex(polylines, RoadsideTreeScatterer.KeepOutRadius(profile));
                TreeGrid = new TreeSeparationGrid(RoadsideTreeScatterer.MaxSeparation(profile));
            }

            public PolylineIndex TreeIndex { get; }

            public TreeSeparationGrid TreeGrid { get; }
        }

        #region Build

        /// <summary>
        /// Paints the road on every tile it crosses and stores one snapshot per touched tile.
        /// Returns the number of touched tiles; tiles the road does not reach are left untouched.
        /// The pass stops as soon as <paramref name="step"/> returns false, keeping the snapshots of the
        /// tiles already painted so a partial apply can still be reverted.
        /// </summary>
        public int Build(IReadOnlyList<TerrainTile> tiles, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, SplineRoadData data, RoadBuildStep step = null)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.ClearSnapshots();

            var context = new BuildContext(polylines, profile);
            var built = 0;

            for (var i = 0; i < tiles.Count; i++)
            {
                if (step != null && !step(i, DescribeTile(tiles[i]))) break;

                var snapshot = new TerrainSnapshot();
                if (!BuildTile(tiles[i], polylines, profile, context, snapshot)) continue;

                data.AddTerrainSnapshot(tiles[i].Data, snapshot);
                built++;
            }

            return built;
        }

        private static string DescribeTile(TerrainTile tile)
        {
            return tile.Data != null ? tile.Data.name : "terrain";
        }

        private bool BuildTile(TerrainTile tile, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, BuildContext context, TerrainSnapshot snapshot)
        {
            var terrainData = tile.Data;
            if (terrainData == null) return false;

            var heightGrid = new TerrainGrid(terrainData.heightmapResolution, tile.Min, tile.Size, false);
            var heightField = _rasterizer.Rasterize(heightGrid, polylines, profile.Reach, profile.JunctionBlendRadius);
            if (heightField == null) return false;

            RoadField alphaField = null;
            RoadField detailField = null;
            var layerIndex = NoLayer;

            if (profile.PaintTexture && profile.RoadLayer != null)
            {
                layerIndex = EnsureLayer(terrainData, profile.RoadLayer);
                var alphaGrid = new TerrainGrid(terrainData.alphamapResolution, tile.Min, tile.Size, true);
                alphaField = _rasterizer.Rasterize(alphaGrid, polylines, profile.Reach);

                if (alphaField != null)
                {
                    _rasterizer.FillJunctionGaps(alphaField, alphaGrid, profile.HalfWidth + profile.TextureFeather, profile.JunctionTextureFill);
                }
            }

            if (HasDetails(terrainData) && (profile.ClearDetails || HasRoadsideRule(profile, RoadsideKind.Detail)))
            {
                var detailGrid = new TerrainGrid(terrainData.detailResolution, tile.Min, tile.Size, true);
                detailField = _rasterizer.Rasterize(detailGrid, polylines, profile.Reach);

                if (detailField != null)
                {
                    _rasterizer.FillJunctionGaps(detailField, detailGrid, profile.VegetationRadius, profile.JunctionTextureFill);
                }
            }

            snapshot.Capture(terrainData, heightField.Rect, alphaField?.Rect ?? default, detailField?.Rect ?? default);

            ApplyHeights(terrainData, tile.Position.y, heightField, profile);

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
                snapshot.StoreTrees(_vegetationClearer.ClearTrees(terrainData, tile.Position, polylines, profile.VegetationRadius));
            }

            if (context.TreeGrid != null && terrainData.treePrototypes.Length > 0)
            {
                snapshot.StorePlacedTrees(_treeScatterer.Scatter(tile, polylines, context.TreeIndex, profile, context.TreeGrid));
            }

            return true;
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

#endif
