#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    /// <summary>
    /// One terrain tile of the world: its data, world origin and XZ bounds.
    /// The road tool paints every tile it crosses, so all terrain lookups go through this struct
    /// instead of a single Terrain reference.
    /// </summary>
    public readonly struct TerrainTile
    {
        private const float BoundsEpsilon = 0.001f;

        public TerrainTile(Terrain terrain)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));

            var data = terrain.terrainData;
            if (data == null) throw new ArgumentException("The terrain has no TerrainData.", nameof(terrain));

            Terrain = terrain;
            Data = data;
            Position = terrain.transform.position;
            Size = new Vector2(data.size.x, data.size.z);
            Min = new Vector2(Position.x, Position.z);
            Max = Min + Size;
        }

        public Terrain Terrain { get; }

        public TerrainData Data { get; }

        public Vector3 Position { get; }

        public Vector2 Size { get; }

        public Vector2 Min { get; }

        public Vector2 Max { get; }

        public bool Contains(Vector2 worldXZ)
        {
            return worldXZ.x >= Min.x - BoundsEpsilon
                   && worldXZ.x <= Max.x + BoundsEpsilon
                   && worldXZ.y >= Min.y - BoundsEpsilon
                   && worldXZ.y <= Max.y + BoundsEpsilon;
        }

        public bool Overlaps(Vector2 worldMin, Vector2 worldMax)
        {
            return worldMin.x <= Max.x + BoundsEpsilon
                   && worldMax.x >= Min.x - BoundsEpsilon
                   && worldMin.y <= Max.y + BoundsEpsilon
                   && worldMax.y >= Min.y - BoundsEpsilon;
        }

        /// <summary>Horizontal distance to the tile bounds, zero when the point is inside the tile.</summary>
        public float DistanceSqrTo(Vector2 worldXZ)
        {
            var dx = Mathf.Max(Mathf.Max(Min.x - worldXZ.x, worldXZ.x - Max.x), 0f);
            var dz = Mathf.Max(Mathf.Max(Min.y - worldXZ.y, worldXZ.y - Max.y), 0f);

            return dx * dx + dz * dz;
        }

        public float SampleHeight(Vector3 world)
        {
            return Terrain.SampleHeight(world) + Position.y;
        }
    }

    /// <summary>
    /// Resolves the terrain tile under a world position.
    /// A spline that spans several tiles is projected on the heightmap that actually owns each point,
    /// and points outside the tiled area fall back to the nearest tile instead of dropping to zero.
    /// </summary>
    public sealed class TerrainHeightSampler
    {
        private readonly TerrainTile[] _tiles;
        private int _lastTile;

        public TerrainHeightSampler(IReadOnlyList<TerrainTile> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));

            _tiles = new TerrainTile[tiles.Count];

            for (var i = 0; i < tiles.Count; i++)
            {
                _tiles[i] = tiles[i];
            }
        }

        public int Count => _tiles.Length;

        public bool TryGetTile(Vector3 world, out TerrainTile tile)
        {
            var xz = new Vector2(world.x, world.z);

            // Points arrive in spline order, so the tile that answered last is the cheapest first guess.
            if (_tiles.Length > 0 && _tiles[_lastTile].Contains(xz))
            {
                tile = _tiles[_lastTile];
                return true;
            }

            for (var i = 0; i < _tiles.Length; i++)
            {
                if (!_tiles[i].Contains(xz)) continue;

                _lastTile = i;
                tile = _tiles[i];
                return true;
            }

            tile = default;
            return false;
        }

        /// <summary>Samples the containing tile, or the nearest tile when the position is outside the tiled area.</summary>
        public float Sample(Vector3 world)
        {
            if (_tiles.Length == 0) return world.y;
            if (TryGetTile(world, out var tile)) return tile.SampleHeight(world);

            var xz = new Vector2(world.x, world.z);
            var nearest = 0;
            var nearestDistance = float.MaxValue;

            for (var i = 0; i < _tiles.Length; i++)
            {
                var distance = _tiles[i].DistanceSqrTo(xz);
                if (distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearest = i;
            }

            return _tiles[nearest].SampleHeight(world);
        }
    }
}
#endif
