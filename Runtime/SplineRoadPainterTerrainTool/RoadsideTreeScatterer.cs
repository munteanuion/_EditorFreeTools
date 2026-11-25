#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace NextnityStudio.SplineRoad
{
    public readonly struct TreePlacement
    {
        public TreePlacement(Vector3 worldPosition, float scale, int prototypeIndex)
        {
            WorldPosition = worldPosition;
            Scale = scale;
            PrototypeIndex = prototypeIndex;
        }

        public Vector3 WorldPosition { get; }

        public float Scale { get; }

        public int PrototypeIndex { get; }
    }

    /// <summary>
    /// World-space keep-out table for tree placements. One instance is shared by every terrain tile of an
    /// apply pass, so trees on the border of two tiles are still separated instead of landing on top of each other.
    /// </summary>
    public sealed class TreeSeparationGrid
    {
        private const float MinCellSize = 1f;
        private const int KeyShift = 32;

        private readonly float _cellSize;
        private readonly Dictionary<long, List<Vector2>> _cells = new Dictionary<long, List<Vector2>>();

        public TreeSeparationGrid(float maxRadius)
        {
            _cellSize = Mathf.Max(maxRadius, MinCellSize);
        }

        public bool IsTooClose(Vector2 point, float radius)
        {
            if (radius <= 0f) return false;

            var centerX = Mathf.FloorToInt(point.x / _cellSize);
            var centerY = Mathf.FloorToInt(point.y / _cellSize);
            var range = Mathf.CeilToInt(radius / _cellSize);
            var radiusSqr = radius * radius;

            for (var y = centerY - range; y <= centerY + range; y++)
            {
                for (var x = centerX - range; x <= centerX + range; x++)
                {
                    if (!_cells.TryGetValue(KeyOf(x, y), out var list)) continue;

                    for (var i = 0; i < list.Count; i++)
                    {
                        if ((list[i] - point).sqrMagnitude < radiusSqr) return true;
                    }
                }
            }

            return false;
        }

        public void Add(Vector2 point)
        {
            var key = KeyOf(Mathf.FloorToInt(point.x / _cellSize), Mathf.FloorToInt(point.y / _cellSize));

            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<Vector2>();
                _cells[key] = list;
            }

            list.Add(point);
        }

        private long KeyOf(int x, int y) => ((long)x << KeyShift) | (uint)y;
    }

    public sealed class RoadsideTreeScatterer
    {
        private const float FullCircle = Mathf.PI * 2f;
        private const float MinSegmentLength = 0.001f;
        private const float KeepOutTolerance = 0.05f;
        private const float Half = 0.5f;
        private const float SignedScale = 2f;
        private const float SideRight = 1f;
        private const float SideLeft = -1f;

        private sealed class Placement
        {
            public Placement(TreeInstance instance, Vector3 worldPosition, float scale, int prototypeIndex)
            {
                Instance = instance;
                WorldPosition = worldPosition;
                Scale = scale;
                PrototypeIndex = prototypeIndex;
            }

            public TreeInstance Instance { get; }

            public Vector3 WorldPosition { get; }

            public float Scale { get; }

            public int PrototypeIndex { get; }
        }

        private sealed class Context
        {
            public Context(TerrainTile tile, PolylineIndex index, RoadProfile profile, Random random, List<Placement> placed, TreeSeparationGrid grid, List<int> validPrototypes)
            {
                Tile = tile;
                Index = index;
                Profile = profile;
                Random = random;
                Placed = placed;
                Grid = grid;
                ValidPrototypes = validPrototypes;
            }

            public TerrainTile Tile { get; }

            public PolylineIndex Index { get; }

            public RoadProfile Profile { get; }

            public Random Random { get; }

            public List<Placement> Placed { get; }

            public TreeSeparationGrid Grid { get; }

            public List<int> ValidPrototypes { get; }
        }

        #region Scatter

        public List<TreeRecord> Scatter(TerrainTile tile, IReadOnlyList<Vector3[]> polylines, PolylineIndex index, RoadProfile profile, TreeSeparationGrid grid)
        {
            if (tile.Data == null) throw new ArgumentException("The terrain tile has no TerrainData.", nameof(tile));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var separation = grid ?? new TreeSeparationGrid(MaxSeparation(profile));
            var placements = Compute(tile, polylines, index, profile, separation);
            var records = new List<TreeRecord>(placements.Count);

            for (var i = 0; i < placements.Count; i++)
            {
                records.Add(new TreeRecord(placements[i].Instance));
            }

            AppendToTerrain(tile.Data, records);

            return records;
        }

        /// <summary>Placements for every tile, computed with one shared separation grid so the preview matches the apply.</summary>
        public List<TreePlacement> PreviewPlacements(IReadOnlyList<TerrainTile> tiles, IReadOnlyList<Vector3[]> polylines, RoadProfile profile)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var preview = new List<TreePlacement>();
            if (!HasTreeRules(profile) || polylines.Count == 0) return preview;

            var margin = KeepOutRadius(profile) + MaxSeparation(profile);
            if (!TryGetBounds(polylines, margin, out var min, out var max)) return preview;

            var index = new PolylineIndex(polylines, KeepOutRadius(profile));
            var grid = new TreeSeparationGrid(MaxSeparation(profile));

            for (var t = 0; t < tiles.Count; t++)
            {
                var tile = tiles[t];
                if (tile.Data == null || tile.Data.treePrototypes.Length == 0) continue;

                // Skip tiles the road does not reach: a world made of many tiles would otherwise
                // run the whole scatter for every tile on every scene repaint.
                if (!tile.Overlaps(min, max)) continue;

                var placements = Compute(tile, polylines, index, profile, grid);

                for (var i = 0; i < placements.Count; i++)
                {
                    var placement = placements[i];
                    preview.Add(new TreePlacement(placement.WorldPosition, placement.Scale, placement.PrototypeIndex));
                }
            }

            return preview;
        }

        private static bool TryGetBounds(IReadOnlyList<Vector3[]> polylines, float margin, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            var found = false;

            for (var p = 0; p < polylines.Count; p++)
            {
                var points = polylines[p];

                for (var i = 0; i < points.Length; i++)
                {
                    var flat = new Vector2(points[i].x, points[i].z);
                    min = Vector2.Min(min, flat);
                    max = Vector2.Max(max, flat);
                    found = true;
                }
            }

            var offset = new Vector2(margin, margin);
            min -= offset;
            max += offset;

            return found;
        }

        public static float KeepOutRadius(RoadProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var keepOut = 0f;
            var rules = profile.Roadside;
            if (rules == null) return keepOut;

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null || !rule.Enabled || rule.Kind != RoadsideKind.Tree) continue;

                keepOut = Mathf.Max(keepOut, profile.HalfWidth + rule.OffsetMin);
            }

            return Mathf.Max(keepOut, 0f);
        }

        public static float MaxSeparation(RoadProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var max = 0f;
            var rules = profile.Roadside;
            if (rules == null) return max;

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null || !rule.Enabled || rule.Kind != RoadsideKind.Tree) continue;

                max = Mathf.Max(max, rule.MinSeparation);
            }

            return max;
        }

        public static bool HasTreeRules(RoadProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var rules = profile.Roadside;
            if (rules == null) return false;

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule != null && rule.Enabled && rule.Kind == RoadsideKind.Tree) return true;
            }

            return false;
        }

        private List<Placement> Compute(TerrainTile tile, IReadOnlyList<Vector3[]> polylines, PolylineIndex index, RoadProfile profile, TreeSeparationGrid grid)
        {
            var placed = new List<Placement>();
            var prototypeCount = tile.Data.treePrototypes.Length;
            var rules = profile.Roadside;
            if (rules == null) return placed;

            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule == null || !rule.Enabled || rule.Kind != RoadsideKind.Tree) continue;

                var valid = rule.ValidPrototypes(prototypeCount);
                if (valid.Count == 0) continue;

                for (var p = 0; p < polylines.Count; p++)
                {
                    var random = new Random(HashCode.Combine(rule.Seed, p, r));
                    var context = new Context(tile, index, profile, random, placed, grid, valid);
                    ScatterAlong(polylines[p], rule, context);
                }
            }

            return placed;
        }

        private void ScatterAlong(Vector3[] points, RoadsideRule rule, Context context)
        {
            var nextSlot = 0f;
            var walked = 0f;

            for (var i = 1; i < points.Length; i++)
            {
                var start = points[i - 1];
                var end = points[i];
                var direction = new Vector2(end.x - start.x, end.z - start.z);
                var length = direction.magnitude;
                if (length < MinSegmentLength) continue;

                direction /= length;

                while (nextSlot <= walked + length)
                {
                    var along = (nextSlot - walked) / length;
                    var center = new Vector2(Mathf.Lerp(start.x, end.x, along), Mathf.Lerp(start.z, end.z, along));

                    PlaceSides(center, direction, rule, context);
                    nextSlot += rule.Spacing;
                }

                walked += length;
            }
        }

        private void PlaceSides(Vector2 center, Vector2 direction, RoadsideRule rule, Context context)
        {
            var right = new Vector2(direction.y, -direction.x);

            if (rule.Side != RoadsideSide.Left)
            {
                TryPlace(center, direction, right * SideRight, rule, context);
            }

            if (rule.Side != RoadsideSide.Right)
            {
                TryPlace(center, direction, right * SideLeft, rule, context);
            }
        }

        #endregion

        #region Place

        private void TryPlace(Vector2 center, Vector2 direction, Vector2 sideDirection, RoadsideRule rule, Context context)
        {
            var random = context.Random;
            var chanceRoll = (float)random.NextDouble();
            var offsetRoll = (float)random.NextDouble();
            var alongRoll = (float)random.NextDouble();
            var scaleRoll = (float)random.NextDouble();
            var heightRoll = (float)random.NextDouble();
            var rotationRoll = (float)random.NextDouble();
            var tintRoll = (float)random.NextDouble();
            var prototypeRoll = (float)random.NextDouble();

            if (chanceRoll > rule.Chance) return;

            var tile = context.Tile;
            var half = context.Profile.HalfWidth;
            var distance = half + Mathf.Lerp(rule.OffsetMin, rule.OffsetMax, offsetRoll);
            var alongOffset = (alongRoll * SignedScale - 1f) * rule.AlongJitter * rule.Spacing * Half;
            var point = center + sideDirection * distance + direction * alongOffset;

            if (context.Index.IsWithin(point, Mathf.Max(0f, half + rule.OffsetMin - KeepOutTolerance))) return;
            if (context.Grid.IsTooClose(point, rule.MinSeparation)) return;

            // A tree belongs to exactly one tile: the one whose terrain holds this position.
            var normalizedX = (point.x - tile.Min.x) / tile.Size.x;
            var normalizedZ = (point.y - tile.Min.y) / tile.Size.y;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f) return;
            if (tile.Data.GetSteepness(normalizedX, normalizedZ) > rule.MaxSlope) return;

            var height = tile.Data.GetInterpolatedHeight(normalizedX, normalizedZ) + rule.HeightOffset;
            var widthScale = Mathf.Lerp(rule.ScaleMin, rule.ScaleMax, scaleRoll);
            var heightScale = widthScale * (1f + (heightRoll * SignedScale - 1f) * rule.HeightVariation);
            var shade = 1f - tintRoll * rule.TintVariation;
            var valid = context.ValidPrototypes;
            var prototype = valid[Mathf.Min((int)(prototypeRoll * valid.Count), valid.Count - 1)];

            var tree = new TreeInstance
            {
                position = new Vector3(normalizedX, Mathf.Clamp01(height / tile.Data.size.y), normalizedZ),
                prototypeIndex = prototype,
                widthScale = widthScale,
                heightScale = heightScale,
                rotation = rule.RandomRotation ? rotationRoll * FullCircle : 0f,
                color = new Color(shade, shade, shade, 1f),
                lightmapColor = Color.white
            };

            var worldPosition = new Vector3(point.x, tile.Position.y + height, point.y);
            context.Placed.Add(new Placement(tree, worldPosition, widthScale, prototype));
            context.Grid.Add(point);
        }

        private void AppendToTerrain(TerrainData terrainData, List<TreeRecord> placed)
        {
            if (placed.Count == 0) return;

            var existing = terrainData.treeInstances;
            var all = new TreeInstance[existing.Length + placed.Count];
            Array.Copy(existing, all, existing.Length);

            for (var i = 0; i < placed.Count; i++)
            {
                all[existing.Length + i] = placed[i].ToInstance();
            }

            terrainData.SetTreeInstances(all, false);
        }

        #endregion
    }
}
#endif
