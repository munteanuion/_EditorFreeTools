using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NextnityStudio.SplineRoad
{
    // Add this to the GameObject that holds the SplineContainer, assign the terrain tiles and a SplineRoadData asset
    // (use Create Data Asset in the inspector). Every tile the spline crosses is painted, so a world made of terrain
    // tiles can be crossed by one road. Tune the profile, press Apply Road: Revert restores every touched tile,
    // Bake keeps the road permanently, Restore Saved Spline reloads the stored spline.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class SplineRoadPainter : MonoBehaviour
    {
        public const string DataFieldName = nameof(_data);
        public const string TerrainsFieldName = nameof(_terrains);
        public const string LegacyTerrainFieldName = nameof(_terrain);
        public const string PreviewFieldName = nameof(_preview);

        private const string LogPrefix = "SplineRoad";
        private const string ApplyProgressTitle = "Spline Road - Apply";
        private const string RevertProgressTitle = "Spline Road - Revert";

        [SerializeField, Tooltip("Legacy single terrain, only kept so scenes painted before multi-terrain support still revert. The Terrains list wins when it is not empty.")]
        private Terrain _terrain;

        [SerializeField, Tooltip("Terrain tiles the road is painted on. Tiles the spline does not cross are skipped.")]
        private List<Terrain> _terrains = new List<Terrain>();

        [SerializeField] private SplineRoadData _data;
        [SerializeField] private RoadPreviewSettings _preview = new RoadPreviewSettings();

#if UNITY_EDITOR
        private readonly SplinePolylineSampler _sampler = new SplinePolylineSampler();
        private readonly RoadTerrainBuilder _builder = new RoadTerrainBuilder();
        private readonly List<TerrainTile> _resolvedTiles = new List<TerrainTile>();
#endif
        private readonly List<Terrain> _resolvedTerrains = new List<Terrain>();
        private SplineContainer _container;

        public SplineRoadData Data => _data;

        public RoadPreviewSettings Preview => _preview;

        /// <summary>Resolved terrain tiles: the list when filled, otherwise the legacy single terrain.</summary>
        public IReadOnlyList<Terrain> Terrains => ResolveTerrains();

        public Terrain Terrain
        {
            get
            {
                var terrains = ResolveTerrains();

                return terrains.Count > 0 ? terrains[0] : null;
            }
        }

        public bool HasLegacyTerrain => (_terrains == null || _terrains.Count == 0) && _terrain != null;

        public bool IsReady => ResolveTerrains().Count > 0 && _data != null;

        public bool HasAppliedRoad => _data != null && _data.HasSnapshot;

        public SplineContainer Container
        {
            get
            {
                if (_container == null)
                {
                    _container = GetComponent<SplineContainer>();
                }

                return _container;
            }
        }

        public List<Terrain> ResolveTerrains()
        {
            _resolvedTerrains.Clear();
            if (_terrains != null)
            {
                for (var i = 0; i < _terrains.Count; i++)
                {
                    var terrain = _terrains[i];
                    if (terrain == null || _resolvedTerrains.Contains(terrain)) continue;

                    _resolvedTerrains.Add(terrain);
                }
            }

            if (_resolvedTerrains.Count == 0 && _terrain != null)
            {
                _resolvedTerrains.Add(_terrain);
            }

            return _resolvedTerrains;
        }

        private void Reset()
        {
#if UNITY_EDITOR
            _terrains.Clear();
            _terrains.AddRange(FindObjectsByType<Terrain>(FindObjectsInactive.Exclude));
            _terrain = null;
            SortByPosition(_terrains);
#endif
        }

#if UNITY_EDITOR
        private static void SortByPosition(List<Terrain> terrains)
        {
            terrains.Sort(CompareByPosition);
        }

        private static int CompareByPosition(Terrain left, Terrain right)
        {
            var leftPosition = left.transform.position;
            var rightPosition = right.transform.position;
            var row = rightPosition.z.CompareTo(leftPosition.z);

            return row != 0 ? row : leftPosition.x.CompareTo(rightPosition.x);
        }

        /// <summary>Tiles of the resolved terrains, in the order they are applied.</summary>
        public List<TerrainTile> ResolveTiles()
        {
            _resolvedTiles.Clear();

            var terrains = ResolveTerrains();
            for (var i = 0; i < terrains.Count; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null) continue;

                _resolvedTiles.Add(new TerrainTile(terrain));
            }

            return _resolvedTiles;
        }

        public void ApplyRoad()
        {
            if (!Validate()) return;

            if (!RevertRoad())
            {
                LogWarning("Apply aborted: reverting the previous road was cancelled.");
                return;
            }

            var tiles = ResolveTiles();
            if (tiles.Count == 0)
            {
                LogError("No terrain tile has TerrainData. Assign the terrain tiles in the Terrains list.");
                return;
            }

            var profile = _data.Profile;
            var polylines = _sampler.Sample(Container, new TerrainHeightSampler(tiles), profile);

            if (polylines.Count == 0)
            {
                LogError("The spline needs at least two knots.");
                return;
            }

            var built = 0;
            var cancelled = false;

            using (var progress = new RoadProgressReporter(tiles.Count, ApplyProgressTitle))
            {
                built = _builder.Build(tiles, polylines, profile, _data, progress.Report);
                cancelled = progress.Cancelled;
            }

            if (cancelled)
            {
                LogWarning($"Apply cancelled after {built} of {tiles.Count} tile(s). The tiles already painted keep the road; press Revert to undo them.");
                return;
            }

            if (built == 0)
            {
                LogError("The spline does not cross any assigned terrain. Nothing was painted.");
                return;
            }

            _data.SaveSpline(Container);
        }

        public List<Vector3[]> SamplePolylines()
        {
            if (!IsReady) return new List<Vector3[]>();

            return _sampler.Sample(Container, new TerrainHeightSampler(ResolveTiles()), _data.Profile);
        }
#endif

        /// <summary>
        /// Restores every tile painted by the current snapshot list.
        /// Returns false when the user cancelled, in which case the snapshots are kept so Revert can be run again.
        /// </summary>
        public bool RevertRoad()
        {
            if (_data == null) return true;

            var entries = _data.TerrainSnapshots;
            var cancelled = false;

            using (var progress = new RoadProgressReporter(entries.Count, RevertProgressTitle))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!progress.Report(i, DescribeEntry(entries[i]))) break;

                    var entry = entries[i];
                    if (entry == null || entry.Snapshot == null || !entry.Snapshot.HasData) continue;

                    var terrainData = entry.TerrainData;
                    if (terrainData == null)
                    {
                        LogError("A terrain painted by this road no longer exists, so its part of the road could not be reverted.");
                        continue;
                    }

                    if (!entry.Snapshot.Restore(terrainData))
                    {
                        LogError($"Terrain '{terrainData.name}' resolution or layers changed since the road was applied. Its snapshot was discarded.");
                    }
                }

                cancelled = progress.Cancelled;
            }

            if (cancelled)
            {
                LogWarning("Revert cancelled. Tiles already restored are back to their original state, the rest still hold the road. The snapshot was kept, so press Revert again to finish.");
                return false;
            }

            RestoreLegacySnapshot();
            _data.ClearSnapshots();

            return true;
        }

        private static string DescribeEntry(TerrainSnapshotEntry entry)
        {
            return entry != null && entry.TerrainData != null ? entry.TerrainData.name : "missing terrain";
        }

        public void BakeRoad()
        {
            if (_data == null) return;

            _data.ClearSnapshots();
        }

        public void LoadSavedSpline()
        {
            if (_data == null) return;

            if (!_data.LoadSpline(Container))
            {
                LogError("The data asset has no saved spline yet.");
            }
        }

        private void RestoreLegacySnapshot()
        {
            if (_data.TerrainSnapshots.Count > 0 || !_data.HasLegacySnapshot) return;

            var terrain = Terrain;
            if (terrain == null || terrain.terrainData == null || !_data.LegacySnapshot.Restore(terrain.terrainData))
            {
                LogError("The road was applied by an older single-terrain version and that terrain is no longer available. The snapshot was discarded.");
            }
        }

        private bool Validate()
        {
            if (ResolveTerrains().Count == 0)
            {
                LogError("No terrain assigned. Add the terrain tiles to the Terrains list.");
                return false;
            }

            if (_data == null)
            {
                LogError("Spline Road Data is not assigned.");
                return false;
            }

            if (_data.Profile.PaintTexture && _data.Profile.RoadLayer == null)
            {
                LogError("Texture painting is on but Road Layer is empty.");
                return false;
            }

            return true;
        }

        private void LogError(string message)
        {
            Debug.LogError($"[{LogPrefix}] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[{LogPrefix}] {message}", this);
        }
    }
}
