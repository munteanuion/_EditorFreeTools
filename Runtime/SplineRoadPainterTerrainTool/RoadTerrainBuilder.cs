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

        private readonly struct PreparedTile
        {
            public PreparedTile(TerrainTile tile, TerrainSnapshot snapshot, RoadField heightField, RoadField alphaField, RoadField detailField, int layerIndex)
            {
                Tile = tile;
                Snapshot = snapshot;
                HeightField = heightField;
                AlphaField = alphaField;
                DetailField = detailField;
                LayerIndex = layerIndex;
            }

            public TerrainTile Tile { get; }

            public TerrainSnapshot Snapshot { get; }

            public RoadField HeightField { get; }

            public RoadField AlphaField { get; }

            public RoadField DetailField { get; }

            public int LayerIndex { get; }
        }

        #region Build

        /// <summary>
        /// Paints the road on every tile it crosses and stores one snapshot per touched tile.
        /// Returns the number of touched tiles; tiles the road does not reach are left untouched.
        /// Heights are applied and synced on every touched tile before any alphamap texture is written,
        /// so listeners of OnTerrainChanged(Heightmap) (such as procedural terrain painters that repaint
        /// a 1-texel neighbour border via TerrainPaintUtility) cannot overwrite an already-painted seam.
        /// </summary>
        public int Build(IReadOnlyList<TerrainTile> tiles, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, SplineRoadData data, RoadBuildStep step = null)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.ClearSnapshots();

            var context = new BuildContext(polylines, profile);
            var prepared = new List<PreparedTile>();

            for (var i = 0; i < tiles.Count; i++)
            {
                if (step != null && !step(i, DescribeTile(tiles[i]))) break;

                if (!TryPrepareTile(tiles[i], polylines, profile, out var item)) continue;

                ApplyHeights(item.Tile.Data, item.Tile.Position.y, item.HeightField, profile);
                data.AddTerrainSnapshot(item.Tile.Data, item.Snapshot);
                prepared.Add(item);
            }

            for (var i = 0; i < prepared.Count; i++)
            {
                ApplySurfaceAndVegetation(prepared[i], polylines, profile, context);
            }

            return prepared.Count;
        }

        private static string DescribeTile(TerrainTile tile)
        {
            return tile.Data != null ? tile.Data.name : "terrain";
        }

        private bool TryPrepareTile(TerrainTile tile, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, out PreparedTile prepared)
        {
            prepared = default;

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
                // Corner-aligned (cellCentered = false): texel 0 sits on tile.Min and texel N-1 sits on tile.Max,
                // so the border texel of one tile and the border texel of its neighbour are evaluated at the exact
                // same world point and receive the exact same road weight across the seam.
                var alphaGrid = new TerrainGrid(terrainData.alphamapResolution, tile.Min, tile.Size, false);
                alphaField = _rasterizer.Rasterize(alphaGrid, polylines, profile.Reach);
            }

            if (HasDetails(terrainData) && (profile.ClearDetails || HasRoadsideRule(profile, RoadsideKind.Detail)))
            {
                var detailGrid = new TerrainGrid(terrainData.detailResolution, tile.Min, tile.Size, true);
                detailField = _rasterizer.Rasterize(detailGrid, polylines, profile.Reach);
            }

            var snapshot = new TerrainSnapshot();
            snapshot.Capture(terrainData, heightField.Rect, alphaField?.Rect ?? default, detailField?.Rect ?? default);

            prepared = new PreparedTile(tile, snapshot, heightField, alphaField, detailField, layerIndex);
            return true;
        }

        private void ApplySurfaceAndVegetation(PreparedTile item, IReadOnlyList<Vector3[]> polylines, RoadProfile profile, BuildContext context)
        {
            var tile = item.Tile;
            var terrainData = tile.Data;
            var snapshot = item.Snapshot;

            if (item.AlphaField != null)
            {
                ApplyTexture(terrainData, item.AlphaField, profile, item.LayerIndex);
            }

            if (item.DetailField != null)
            {
                if (profile.ClearDetails)
                {
                    _vegetationClearer.ClearDetails(terrainData, item.DetailField, profile.VegetationRadius);
                }

                if (HasRoadsideRule(profile, RoadsideKind.Detail))
                {
                    _detailScatterer.Scatter(terrainData, item.DetailField, profile);
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
