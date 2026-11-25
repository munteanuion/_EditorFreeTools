#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NextnityStudio.SplineRoad.Tests
{
    public sealed class RoadFieldRasterizerTests
    {
        private const int GridResolution = 65;
        private const float GridSize = 64f;
        private const float Reach = 5f;
        private const float BlendRadius = 8f;
        private const float HeightTolerance = 0.01f;

        private readonly RoadFieldRasterizer _rasterizer = new RoadFieldRasterizer();

        [Test]
        public void CrossingSplines_BlendHeightsAcrossTheJunction()
        {
            TerrainGrid grid = CreateGrid();
            RoadField field = _rasterizer.Rasterize(grid, CreateCrossingSplines(), Reach, BlendRadius);

            Assert.IsNotNull(field);
            Assert.AreEqual(3f, HeightAt(field, grid, Vector2.zero), HeightTolerance);
            Assert.That(HeightAt(field, grid, new Vector2(0f, 2f)), Is.InRange(3f, 3.55f));
            Assert.That(HeightAt(field, grid, new Vector2(0f, 4f)), Is.GreaterThan(3.7f));
        }

        [Test]
        public void CrossingSplines_PreserveHeightAwayFromTheJunction()
        {
            TerrainGrid grid = CreateGrid();
            RoadField field = _rasterizer.Rasterize(grid, CreateCrossingSplines(), Reach, BlendRadius);

            Assert.AreEqual(2f, HeightAt(field, grid, new Vector2(12f, 0f)), HeightTolerance);
            Assert.AreEqual(4f, HeightAt(field, grid, new Vector2(0f, 12f)), HeightTolerance);
        }

        [Test]
        public void RasterizeWithoutBlend_UsesNearestSegmentHeight()
        {
            TerrainGrid grid = CreateGrid();
            RoadField field = _rasterizer.Rasterize(grid, CreateCrossingSplines(), Reach);

            Assert.IsNull(field.BlendWeight);
            Assert.AreEqual(2f, HeightAt(field, grid, Vector2.zero), HeightTolerance);
        }

        [Test]
        public void JunctionBlendRadius_IsNeverSmallerThanRoadAndShoulder()
        {
            var profile = new RoadProfile();

            Assert.AreEqual(8f, profile.JunctionBlendRadius, HeightTolerance);
        }

        [Test]
        public void JunctionTextureFill_IsEnabledByDefault()
        {
            var profile = new RoadProfile();

            Assert.Greater(profile.JunctionTextureFill, 0f);
        }

        [Test]
        public void FillJunctionGaps_ClosesTheStripBetweenTwoNearbyRoads()
        {
            TerrainGrid grid = CreateGrid();
            List<Vector3[]> polylines = CreateParallelRoads(10f);
            const float paintedRadius = 4.5f;
            const float maxGap = 6f;

            RoadField field = _rasterizer.Rasterize(grid, polylines, paintedRadius + 1f);
            Assert.IsNotNull(field);

            int stripIndex = IndexAt(field, grid, new Vector2(32f, 32f));
            Assert.Greater(field.DistanceAt(stripIndex), paintedRadius, "The strip must start unpainted.");

            int filled = _rasterizer.FillJunctionGaps(field, grid, paintedRadius, maxGap);

            Assert.Greater(filled, 0);
            Assert.AreEqual(0f, field.DistanceAt(stripIndex), HeightTolerance, "The strip between the two arms must be filled.");
        }

        [Test]
        public void FillJunctionGaps_LeavesRoadsThatAreFarApartUntouched()
        {
            TerrainGrid grid = CreateGrid();
            List<Vector3[]> polylines = CreateParallelRoads(30f);
            const float paintedRadius = 4.5f;
            const float maxGap = 6f;

            RoadField field = _rasterizer.Rasterize(grid, polylines, paintedRadius + 1f);
            Assert.IsNotNull(field);

            int middleIndex = IndexAt(field, grid, new Vector2(32f, 32f));
            float before = field.DistanceAt(middleIndex);
            Assert.Greater(before, maxGap, "The gap between the roads must be wider than the fill limit.");

            _rasterizer.FillJunctionGaps(field, grid, paintedRadius, maxGap);

            Assert.AreEqual(before, field.DistanceAt(middleIndex), HeightTolerance, "A wide gap must not be filled.");
        }

        [Test]
        public void FillJunctionGaps_UsesTheTotalGapNotHalfOfIt()
        {
            TerrainGrid grid = CreateGrid();
            // 20 m between centerlines and 4.5 m of paint per side -> 11 m of open grass, wider than the 10 m limit.
            List<Vector3[]> polylines = CreateParallelRoads(20f);
            const float paintedRadius = 4.5f;
            const float maxGap = 10f;

            RoadField field = _rasterizer.Rasterize(grid, polylines, paintedRadius + 1f);
            Assert.IsNotNull(field);

            int middleIndex = IndexAt(field, grid, new Vector2(32f, 32f));
            float before = field.DistanceAt(middleIndex);

            _rasterizer.FillJunctionGaps(field, grid, paintedRadius, maxGap);

            Assert.AreEqual(before, field.DistanceAt(middleIndex), HeightTolerance, "Only gaps up to maxGapWidth in total may be filled.");
        }

        [Test]
        public void FillJunctionGaps_DoesNothingWhenDisabled()
        {
            TerrainGrid grid = CreateGrid();
            List<Vector3[]> polylines = CreateParallelRoads(10f);

            RoadField field = _rasterizer.Rasterize(grid, polylines, 5.5f);
            Assert.IsNotNull(field);

            int stripIndex = IndexAt(field, grid, new Vector2(32f, 32f));
            float before = field.DistanceAt(stripIndex);

            Assert.AreEqual(0, _rasterizer.FillJunctionGaps(field, grid, 4.5f, 0f));
            Assert.AreEqual(before, field.DistanceAt(stripIndex), HeightTolerance);
        }

        private static List<Vector3[]> CreateParallelRoads(float separation)
        {
            const float centerZ = 32f;
            float half = separation * 0.5f;

            return new List<Vector3[]>
            {
                new[] { new Vector3(0f, 0f, centerZ - half), new Vector3(GridSize, 0f, centerZ - half) },
                new[] { new Vector3(0f, 0f, centerZ + half), new Vector3(GridSize, 0f, centerZ + half) }
            };
        }

        private static int IndexAt(RoadField field, TerrainGrid grid, Vector2 world)
        {
            RectInt rect = field.Rect;
            float bestDistance = float.MaxValue;
            int best = 0;

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    float distance = (grid.CellToWorld(x, y) - world).sqrMagnitude;
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = field.IndexOf(x, y);
                }
            }

            Assert.Less(bestDistance, 0.51f, "Requested world point was not represented by the rasterized field.");
            return best;
        }

        private static TerrainGrid CreateGrid()
        {
            return new TerrainGrid(GridResolution, Vector2.zero, Vector2.one * GridSize, false);
        }

        private static List<Vector3[]> CreateCrossingSplines()
        {
            return new List<Vector3[]>
            {
                new[] { new Vector3(-16f, 2f, 0f), new Vector3(16f, 2f, 0f) },
                new[] { new Vector3(0f, 4f, -16f), new Vector3(0f, 4f, 16f) }
            };
        }

        private static float HeightAt(RoadField field, TerrainGrid grid, Vector2 world)
        {
            RectInt rect = field.Rect;
            float bestDistance = float.MaxValue;
            float height = 0f;

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    float distance = (grid.CellToWorld(x, y) - world).sqrMagnitude;
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    height = field.Height[field.IndexOf(x, y)];
                }
            }

            Assert.Less(bestDistance, 0.51f, "Requested world point was not represented by the rasterized field.");
            return height;
        }
    }
}
#endif
