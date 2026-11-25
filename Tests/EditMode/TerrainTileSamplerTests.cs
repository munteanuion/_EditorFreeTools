#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NextnityStudio.SplineRoad.Tests
{
    public sealed class TerrainTileSamplerTests
    {
        private const int Resolution = 65;
        private const float TileSize = 64f;
        private const float TileHeight = 100f;
        private const float LeftHeight = 2f;
        private const float RightHeight = 7f;
        private const float HeightTolerance = 0.05f;
        private const float BorderOffset = 0.1f;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<TerrainData> _data = new List<TerrainData>();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _objects.Count; i++)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            for (var i = 0; i < _data.Count; i++)
            {
                Object.DestroyImmediate(_data[i]);
            }

            _objects.Clear();
            _data.Clear();
        }

        [Test]
        public void Sampler_UsesTheTileUnderThePoint()
        {
            var left = CreateTerrain("Left", Vector3.zero, LeftHeight);
            var right = CreateTerrain("Right", new Vector3(TileSize, 0f, 0f), RightHeight);
            var sampler = CreateSampler(left, right);

            Assert.AreEqual(LeftHeight, sampler.Sample(new Vector3(10f, 0f, 10f)), HeightTolerance);
            Assert.AreEqual(RightHeight, sampler.Sample(new Vector3(TileSize + 10f, 0f, 10f)), HeightTolerance);
        }

        [Test]
        public void Sampler_CrossesTheTileBorderWithoutClampingToTheFirstTile()
        {
            var left = CreateTerrain("Left", Vector3.zero, LeftHeight);
            var right = CreateTerrain("Right", new Vector3(TileSize, 0f, 0f), RightHeight);
            var sampler = CreateSampler(left, right);

            Assert.AreEqual(LeftHeight, sampler.Sample(new Vector3(TileSize - BorderOffset, 0f, 10f)), HeightTolerance);
            Assert.AreEqual(RightHeight, sampler.Sample(new Vector3(TileSize + BorderOffset, 0f, 10f)), HeightTolerance);
        }

        [Test]
        public void Sampler_UsesTerrainPositionAsVerticalOffset()
        {
            var raised = CreateTerrain("Raised", new Vector3(0f, 5f, 0f), LeftHeight);
            var sampler = CreateSampler(raised);

            Assert.AreEqual(LeftHeight + raised.transform.position.y, sampler.Sample(new Vector3(10f, 0f, 10f)), HeightTolerance);
        }

        [Test]
        public void Sampler_FallsBackToTheNearestTileOutsideTheGrid()
        {
            var left = CreateTerrain("Left", Vector3.zero, LeftHeight);
            var right = CreateTerrain("Right", new Vector3(TileSize, 0f, 0f), RightHeight);
            var sampler = CreateSampler(left, right);

            Assert.AreEqual(LeftHeight, sampler.Sample(new Vector3(-20f, 0f, 10f)), HeightTolerance);
            Assert.AreEqual(RightHeight, sampler.Sample(new Vector3(TileSize * 2f + 10f, 0f, 10f)), HeightTolerance);
        }

        [Test]
        public void Sampler_ReportsWhichTileOwnsAPosition()
        {
            var left = CreateTerrain("Left", Vector3.zero, LeftHeight);
            var right = CreateTerrain("Right", new Vector3(TileSize, 0f, 0f), RightHeight);
            var sampler = CreateSampler(left, right);

            Assert.IsTrue(sampler.TryGetTile(new Vector3(10f, 0f, 10f), out var inside));
            Assert.AreEqual(left, inside.Terrain);
            Assert.IsFalse(sampler.TryGetTile(new Vector3(-10f, 0f, 10f), out _));
        }

        [Test]
        public void TerrainTile_ReportsBoundsAndDistance()
        {
            var tile = new TerrainTile(CreateTerrain("Tile", Vector3.zero, LeftHeight));

            Assert.IsTrue(tile.Contains(new Vector2(1f, 1f)));
            Assert.IsTrue(tile.Contains(new Vector2(TileSize, TileSize)));
            Assert.IsFalse(tile.Contains(new Vector2(-1f, 1f)));
            Assert.AreEqual(0f, tile.DistanceSqrTo(new Vector2(10f, 10f)), 0.0001f);
            Assert.AreEqual(4f, tile.DistanceSqrTo(new Vector2(-2f, 10f)), 0.0001f);
        }

        [Test]
        public void TerrainTile_OverlapsOnlyWhenTheBoundsIntersect()
        {
            var tile = new TerrainTile(CreateTerrain("Tile", Vector3.zero, LeftHeight));

            Assert.IsTrue(tile.Overlaps(new Vector2(TileSize - 1f, 10f), new Vector2(TileSize + 4f, 20f)));
            Assert.IsFalse(tile.Overlaps(new Vector2(TileSize + 1f, 10f), new Vector2(TileSize + 4f, 20f)));
        }

        private TerrainHeightSampler CreateSampler(params Terrain[] terrains)
        {
            var tiles = new List<TerrainTile>(terrains.Length);

            for (var i = 0; i < terrains.Length; i++)
            {
                tiles.Add(new TerrainTile(terrains[i]));
            }

            return new TerrainHeightSampler(tiles);
        }

        private Terrain CreateTerrain(string name, Vector3 position, float height)
        {
            var terrainData = new TerrainData();
            terrainData.heightmapResolution = Resolution;
            terrainData.size = new Vector3(TileSize, TileHeight, TileSize);

            var heights = new float[Resolution, Resolution];
            var normalized = height / terrainData.size.y;

            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    heights[y, x] = normalized;
                }
            }

            terrainData.SetHeights(0, 0, heights);
            _data.Add(terrainData);

            var terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = name;
            terrainObject.transform.position = position;
            _objects.Add(terrainObject);

            return terrainObject.GetComponent<Terrain>();
        }
    }
}
#endif
