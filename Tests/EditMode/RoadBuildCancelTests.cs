#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NextnityStudio.SplineRoad.Tests
{
    public sealed class RoadBuildCancelTests
    {
        private const int Resolution = 33;
        private const float TileSize = 32f;
        private const float TileHeight = 20f;
        private const float RoadHeight = 2f;
        private const float PaintedHeight = RoadHeight; // TerrainData.GetHeight reports world metres, not a normalized value
        private const float HeightTolerance = 0.01f;
        private const int PaintedCellX = 16;
        private const int PaintedCellY = 8;
        private const int SecondTileCellX = 4;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<TerrainData> _data = new List<TerrainData>();
        private SplineRoadData _roadData;

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

            if (_roadData != null)
            {
                Object.DestroyImmediate(_roadData);
            }

            _objects.Clear();
            _data.Clear();
            _roadData = null;
        }

        [Test]
        public void Build_StopsBeforeTheFirstTileWhenTheStepCancels()
        {
            var tile = new TerrainTile(CreateTerrain("Tile", Vector3.zero));
            var data = CreateRoadData();

            var built = CreateBuilder().Build(new[] { tile }, CreatePolylines(), data.Profile, data, CancelAlways);

            Assert.AreEqual(0, built);
            Assert.AreEqual(0, data.SnapshotTerrainCount);
            Assert.IsFalse(data.HasSnapshot);
            Assert.AreEqual(0f, tile.Data.GetHeight(PaintedCellX, PaintedCellY), HeightTolerance);
        }

        [Test]
        public void Build_KeepsTheSnapshotOfTheTilePaintedBeforeTheCancel()
        {
            var first = new TerrainTile(CreateTerrain("First", Vector3.zero));
            var second = new TerrainTile(CreateTerrain("Second", new Vector3(TileSize, 0f, 0f)));
            var data = CreateRoadData();

            var built = CreateBuilder().Build(new[] { first, second }, CreatePolylines(), data.Profile, data, CancelAfterFirstTile);

            Assert.AreEqual(1, built);
            Assert.AreEqual(1, data.SnapshotTerrainCount);
            Assert.AreEqual(PaintedHeight, first.Data.GetHeight(PaintedCellX, PaintedCellY), HeightTolerance);
            Assert.AreEqual(0f, second.Data.GetHeight(SecondTileCellX, PaintedCellY), HeightTolerance);
        }

        [Test]
        public void ProgressReporter_SmallWorkloadNeverBlocksAndDisposesTwiceSafely()
        {
            var reporter = new RoadProgressReporter(2);

            Assert.IsTrue(reporter.Report(0, "tile"));
            Assert.IsFalse(reporter.Cancelled);

            reporter.Dispose();
            Assert.DoesNotThrow(reporter.Dispose);
        }

        private static bool CancelAlways(int completedSteps, string step)
        {
            return false;
        }

        private static bool CancelAfterFirstTile(int completedSteps, string step)
        {
            return completedSteps < 1;
        }

        private static RoadTerrainBuilder CreateBuilder()
        {
            return new RoadTerrainBuilder();
        }

        private static List<Vector3[]> CreatePolylines()
        {
            return new List<Vector3[]>
            {
                new[] { new Vector3(-8f, RoadHeight, 8f), new Vector3(TileSize + 8f, RoadHeight, 8f) }
            };
        }

        private SplineRoadData CreateRoadData()
        {
            _roadData = ScriptableObject.CreateInstance<SplineRoadData>();

            return _roadData;
        }

        private Terrain CreateTerrain(string name, Vector3 position)
        {
            var terrainData = new TerrainData();
            terrainData.heightmapResolution = Resolution;
            terrainData.size = new Vector3(TileSize, TileHeight, TileSize);
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
