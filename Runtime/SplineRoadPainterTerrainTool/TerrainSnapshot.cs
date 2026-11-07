using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    [Serializable]
    public sealed class TerrainSnapshot
    {
        [SerializeField] private bool _hasData;
        [SerializeField] private int _heightResolution;
        [SerializeField] private RectInt _heightRect;
        [SerializeField] private float[] _heights;
        [SerializeField] private int _alphaResolution;
        [SerializeField] private int _alphaLayers;
        [SerializeField] private RectInt _alphaRect;
        [SerializeField] private float[] _alphas;
        [SerializeField] private int _detailResolution;
        [SerializeField] private int _detailLayers;
        [SerializeField] private RectInt _detailRect;
        [SerializeField] private int[] _details;
        [SerializeField] private bool _treesProcessed;
        [SerializeField] private RemovedTree[] _trees;
        [SerializeField] private TreeRecord[] _placedTrees;

        public bool HasData => _hasData;

        #region Capture

        public void Capture(TerrainData terrainData, RectInt heightRect, RectInt alphaRect, RectInt detailRect)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));

            Clear();

            _heightResolution = terrainData.heightmapResolution;
            _heightRect = heightRect;
            _heights = Flatten(terrainData.GetHeights(heightRect.x, heightRect.y, heightRect.width, heightRect.height));

            if (IsValid(alphaRect))
            {
                _alphaResolution = terrainData.alphamapResolution;
                _alphaLayers = terrainData.alphamapLayers;
                _alphaRect = alphaRect;
                _alphas = Flatten(terrainData.GetAlphamaps(alphaRect.x, alphaRect.y, alphaRect.width, alphaRect.height));
            }

            if (IsValid(detailRect))
            {
                CaptureDetails(terrainData, detailRect);
            }

            _hasData = true;
        }

        public void StoreTrees(List<RemovedTree> removed)
        {
            if (removed == null) throw new ArgumentNullException(nameof(removed));

            _treesProcessed = true;
            _trees = removed.ToArray();
        }

        public void StorePlacedTrees(List<TreeRecord> placed)
        {
            if (placed == null) throw new ArgumentNullException(nameof(placed));

            _placedTrees = placed.ToArray();
        }

        public void Clear()
        {
            _hasData = false;
            _heights = null;
            _alphas = null;
            _details = null;
            _trees = null;
            _placedTrees = null;
            _treesProcessed = false;
        }

        private void CaptureDetails(TerrainData terrainData, RectInt rect)
        {
            _detailResolution = terrainData.detailResolution;
            _detailLayers = terrainData.detailPrototypes.Length;
            _detailRect = rect;

            var cellCount = rect.width * rect.height;
            _details = new int[cellCount * _detailLayers];

            for (var layer = 0; layer < _detailLayers; layer++)
            {
                var map = terrainData.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, layer);
                Buffer.BlockCopy(map, 0, _details, layer * cellCount * sizeof(int), cellCount * sizeof(int));
            }
        }

        #endregion

        #region Restore

        public bool Restore(TerrainData terrainData)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (!_hasData) return true;
            if (terrainData.heightmapResolution != _heightResolution) return false;

            var heights = new float[_heightRect.height, _heightRect.width];
            Buffer.BlockCopy(_heights, 0, heights, 0, _heights.Length * sizeof(float));
            terrainData.SetHeightsDelayLOD(_heightRect.x, _heightRect.y, heights);
            terrainData.SyncHeightmap();

            var success = RestoreAlphas(terrainData);
            success &= RestoreDetails(terrainData);
            RestoreTrees(terrainData);

            return success;
        }

        private bool RestoreAlphas(TerrainData terrainData)
        {
            if (_alphas == null || _alphas.Length == 0) return true;
            if (terrainData.alphamapResolution != _alphaResolution) return false;
            if (terrainData.alphamapLayers != _alphaLayers) return false;

            var alphas = new float[_alphaRect.height, _alphaRect.width, _alphaLayers];
            Buffer.BlockCopy(_alphas, 0, alphas, 0, _alphas.Length * sizeof(float));
            terrainData.SetAlphamaps(_alphaRect.x, _alphaRect.y, alphas);

            return true;
        }

        private bool RestoreDetails(TerrainData terrainData)
        {
            if (_details == null || _details.Length == 0) return true;
            if (terrainData.detailResolution != _detailResolution) return false;
            if (terrainData.detailPrototypes.Length != _detailLayers) return false;

            var cellCount = _detailRect.width * _detailRect.height;

            for (var layer = 0; layer < _detailLayers; layer++)
            {
                var map = new int[_detailRect.height, _detailRect.width];
                Buffer.BlockCopy(_details, layer * cellCount * sizeof(int), map, 0, cellCount * sizeof(int));
                terrainData.SetDetailLayer(_detailRect.x, _detailRect.y, layer, map);
            }

            return true;
        }

        private void RestoreTrees(TerrainData terrainData)
        {
            var hasPlaced = _placedTrees != null && _placedTrees.Length > 0;
            if (!_treesProcessed && !hasPlaced) return;

            var current = terrainData.treeInstances;
            var prototypeCount = terrainData.treePrototypes.Length;
            var removedCount = _trees != null ? _trees.Length : 0;
            var merged = new List<TreeInstance>(current.Length + removedCount);

            for (var i = 0; i < current.Length; i++)
            {
                if (hasPlaced && IsPlaced(current[i])) continue;

                merged.Add(current[i]);
            }

            for (var i = 0; i < removedCount; i++)
            {
                if (_trees[i].PrototypeIndex >= prototypeCount) continue;

                merged.Add(_trees[i].ToInstance());
            }

            terrainData.SetTreeInstances(merged.ToArray(), true);
        }

        private bool IsPlaced(TreeInstance tree)
        {
            for (var i = 0; i < _placedTrees.Length; i++)
            {
                if (_placedTrees[i].Matches(tree)) return true;
            }

            return false;
        }

        #endregion

        private static bool IsValid(RectInt rect) => rect.width > 0 && rect.height > 0;

        private static float[] Flatten(Array source)
        {
            var flat = new float[source.Length];
            Buffer.BlockCopy(source, 0, flat, 0, flat.Length * sizeof(float));
            return flat;
        }
    }
}
