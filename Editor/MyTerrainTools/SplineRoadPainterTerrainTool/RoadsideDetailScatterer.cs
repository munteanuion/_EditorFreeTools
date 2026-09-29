using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public sealed class RoadsideDetailScatterer
    {
        private const int MaxDetailDensity = 16;
        private const float Half = 0.5f;
        private const float MinBandWidth = 0.001f;
        private const uint HashMask = 0xFFFFFF;
        private const uint SeedPrime = 73856093u;
        private const uint XPrime = 19349663u;
        private const uint YPrime = 83492791u;
        private const uint MixPrime = 2246822519u;
        private const int MixShiftA = 13;
        private const int MixShiftB = 16;
        private const int PrototypeSeedFactor = 31;
        private const int PrototypeSeedSalt = 7;

        public void Scatter(TerrainData terrainData, RoadField field, RoadProfile profile)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var layerCount = terrainData.detailPrototypes.Length;
            var rules = profile.Roadside;
            if (rules == null) return;

            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule == null || !rule.Enabled || rule.Kind != RoadsideKind.Detail) continue;

                var valid = rule.ValidPrototypes(layerCount);
                if (valid.Count == 0) continue;

                ScatterRule(terrainData, field, profile.HalfWidth, rule, valid);
            }
        }

        private void ScatterRule(TerrainData terrainData, RoadField field, float halfWidth, RoadsideRule rule, List<int> valid)
        {
            var rect = field.Rect;
            var resolution = terrainData.detailResolution;
            var maps = new int[valid.Count][,];
            var changed = new bool[valid.Count];

            for (var i = 0; i < valid.Count; i++)
            {
                maps[i] = terrainData.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, valid[i]);
            }

            for (var y = 0; y < rect.height; y++)
            {
                for (var x = 0; x < rect.width; x++)
                {
                    var index = y * rect.width + x;
                    if (field.DistanceSqr[index] >= float.MaxValue) continue;

                    var offset = field.DistanceAt(index) - halfWidth;
                    if (offset < rule.OffsetMin || offset > rule.OffsetMax) continue;
                    if (!MatchesSide(rule.Side, field.Side[index])) continue;

                    var cellX = rect.x + x;
                    var cellY = rect.y + y;
                    if (Noise(rule.Seed, cellX, cellY) > rule.Chance) continue;
                    if (terrainData.GetSteepness((cellX + Half) / resolution, (cellY + Half) / resolution) > rule.MaxSlope) continue;

                    var pick = Mathf.Min((int)(Noise(rule.Seed * PrototypeSeedFactor + PrototypeSeedSalt, cellX, cellY) * valid.Count), valid.Count - 1);
                    var map = maps[pick];
                    var value = Mathf.Clamp(Mathf.RoundToInt(rule.Density * EdgeFade(offset, rule)), 0, MaxDetailDensity);
                    if (value <= map[y, x]) continue;

                    map[y, x] = value;
                    changed[pick] = true;
                }
            }

            for (var i = 0; i < valid.Count; i++)
            {
                if (changed[i])
                {
                    terrainData.SetDetailLayer(rect.x, rect.y, valid[i], maps[i]);
                }
            }
        }

        private bool MatchesSide(RoadsideSide side, float sign)
        {
            if (side == RoadsideSide.Both) return true;

            return side == RoadsideSide.Right ? sign > 0f : sign < 0f;
        }

        private float EdgeFade(float offset, RoadsideRule rule)
        {
            var bandWidth = rule.OffsetMax - rule.OffsetMin;
            var fadeLength = bandWidth * rule.EdgeFade * Half;
            if (bandWidth <= MinBandWidth || fadeLength <= MinBandWidth) return 1f;

            var fromEdge = Mathf.Min(offset - rule.OffsetMin, rule.OffsetMax - offset);
            return Mathf.Clamp01(fromEdge / fadeLength);
        }

        private float Noise(int seed, int x, int y)
        {
            unchecked
            {
                var hash = (uint)seed * SeedPrime ^ (uint)x * XPrime ^ (uint)y * YPrime;
                hash ^= hash >> MixShiftA;
                hash *= MixPrime;
                hash ^= hash >> MixShiftB;

                return (hash & HashMask) / (float)HashMask;
            }
        }
    }
}
