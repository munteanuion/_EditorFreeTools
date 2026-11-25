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
