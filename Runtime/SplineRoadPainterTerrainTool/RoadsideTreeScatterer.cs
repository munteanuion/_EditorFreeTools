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

    public sealed class RoadsideTreeScatterer
    {
        private const float FullCircle = Mathf.PI * 2f;
        private const float MinSegmentLength = 0.001f;
        private const float KeepOutTolerance = 0.05f;
        private const float Half = 0.5f;
        private const float SignedScale = 2f;
        private const float SideRight = 1f;
        private const float SideLeft = -1f;
        private const int KeyShift = 32;
        private const float MinCellSize = 1f;

        private sealed class SeparationGrid
        {
            private readonly float _cellSize;
            private readonly Dictionary<long, List<Vector2>> _cells = new Dictionary<long, List<Vector2>>();

            public SeparationGrid(float maxRadius)
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
            public Context(TerrainData terrainData, Vector3 terrainPosition, PolylineIndex index, RoadProfile profile, Random random, List<Placement> placed, SeparationGrid grid, List<int> validPrototypes)
            {
                TerrainData = terrainData;
                TerrainPosition = terrainPosition;
                Index = index;
                Profile = profile;
                Random = random;
                Placed = placed;
                Grid = grid;
                ValidPrototypes = validPrototypes;
            }

            public TerrainData TerrainData { get; }

            public Vector3 TerrainPosition { get; }

            public PolylineIndex Index { get; }

            public RoadProfile Profile { get; }

            public Random Random { get; }

            public List<Placement> Placed { get; }

            public SeparationGrid Grid { get; }

            public List<int> ValidPrototypes { get; }
        }

        #region Scatter

        public List<TreeRecord> Scatter(TerrainData terrainData, Vector3 terrainPosition, IReadOnlyList<Vector3[]> polylines, PolylineIndex index, RoadProfile profile)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var placements = Compute(terrainData, terrainPosition, polylines, index, profile);
            var records = new List<TreeRecord>(placements.Count);

            for (var i = 0; i < placements.Count; i++)
            {
                records.Add(new TreeRecord(placements[i].Instance));
            }

            AppendToTerrain(terrainData, records);

            return records;
        }

        public List<TreePlacement> PreviewPlacements(TerrainData terrainData, Vector3 terrainPosition, IReadOnlyList<Vector3[]> polylines, RoadProfile profile)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var preview = new List<TreePlacement>();
            if (!HasTreeRules(profile) || terrainData.treePrototypes.Length == 0) return preview;

            var index = new PolylineIndex(polylines, KeepOutRadius(profile));
            var placements = Compute(terrainData, terrainPosition, polylines, index, profile);

            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                preview.Add(new TreePlacement(placement.WorldPosition, placement.Scale, placement.PrototypeIndex));
            }

            return preview;
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

        private List<Placement> Compute(TerrainData terrainData, Vector3 terrainPosition, IReadOnlyList<Vector3[]> polylines, PolylineIndex index, RoadProfile profile)
        {
            var placed = new List<Placement>();
            var prototypeCount = terrainData.treePrototypes.Length;
            var rules = profile.Roadside;
            if (rules == null) return placed;

            var grid = new SeparationGrid(MaxSeparation(profile));

            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule == null || !rule.Enabled || rule.Kind != RoadsideKind.Tree) continue;

                var valid = rule.ValidPrototypes(prototypeCount);
                if (valid.Count == 0) continue;

                for (var p = 0; p < polylines.Count; p++)
                {
                    var random = new Random(HashCode.Combine(rule.Seed, p, r));
                    var context = new Context(terrainData, terrainPosition, index, profile, random, placed, grid, valid);
                    ScatterAlong(polylines[p], rule, context);
                }
            }

            return placed;
        }

        private float MaxSeparation(RoadProfile profile)
        {
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

        private bool HasTreeRules(RoadProfile profile)
        {
            var rules = profile.Roadside;
            if (rules == null) return false;

            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule != null && rule.Enabled && rule.Kind == RoadsideKind.Tree) return true;
            }

            return false;
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

            var terrainData = context.TerrainData;
            var half = context.Profile.HalfWidth;
            var distance = half + Mathf.Lerp(rule.OffsetMin, rule.OffsetMax, offsetRoll);
            var alongOffset = (alongRoll * SignedScale - 1f) * rule.AlongJitter * rule.Spacing * Half;
            var point = center + sideDirection * distance + direction * alongOffset;

            if (context.Index.IsWithin(point, Mathf.Max(0f, half + rule.OffsetMin - KeepOutTolerance))) return;
            if (context.Grid.IsTooClose(point, rule.MinSeparation)) return;

            var size = terrainData.size;
            var normalizedX = (point.x - context.TerrainPosition.x) / size.x;
            var normalizedZ = (point.y - context.TerrainPosition.z) / size.z;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f) return;
            if (terrainData.GetSteepness(normalizedX, normalizedZ) > rule.MaxSlope) return;

            var height = terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) + rule.HeightOffset;
            var widthScale = Mathf.Lerp(rule.ScaleMin, rule.ScaleMax, scaleRoll);
            var heightScale = widthScale * (1f + (heightRoll * SignedScale - 1f) * rule.HeightVariation);
            var shade = 1f - tintRoll * rule.TintVariation;
            var valid = context.ValidPrototypes;
            var prototype = valid[Mathf.Min((int)(prototypeRoll * valid.Count), valid.Count - 1)];

            var tree = new TreeInstance
            {
                position = new Vector3(normalizedX, Mathf.Clamp01(height / size.y), normalizedZ),
                prototypeIndex = prototype,
                widthScale = widthScale,
                heightScale = heightScale,
                rotation = rule.RandomRotation ? rotationRoll * FullCircle : 0f,
                color = new Color(shade, shade, shade, 1f),
                lightmapColor = Color.white
            };

            var worldPosition = new Vector3(point.x, context.TerrainPosition.y + height, point.y);
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
