using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

namespace NextnityStudio.SplineRoad
{
    [CreateAssetMenu(fileName = "SplineRoadData", menuName = "NextnityStudio/Spline Road Data")]
    public sealed class SplineRoadData : ScriptableObject
    {
        public const string ProfileFieldName = nameof(_profile);

        [SerializeField] private RoadProfile _profile = new RoadProfile();
        [SerializeField] private List<Spline> _splines = new List<Spline>();
        [SerializeField] private Vector3 _position;
        [SerializeField] private Quaternion _rotation = Quaternion.identity;
        [SerializeField] private Vector3 _localScale = Vector3.one;

        [FormerlySerializedAs("_snapshot")]
        [SerializeField, Tooltip("Snapshot of a road applied by the single-terrain version of this tool. New applies store one entry per terrain instead.")]
        private TerrainSnapshot _legacySnapshot = new TerrainSnapshot();

        [SerializeField] private List<TerrainSnapshotEntry> _terrainSnapshots = new List<TerrainSnapshotEntry>();

        public RoadProfile Profile => _profile;

        public IReadOnlyList<TerrainSnapshotEntry> TerrainSnapshots => _terrainSnapshots;

        public TerrainSnapshot LegacySnapshot => _legacySnapshot;

        public bool HasLegacySnapshot => _legacySnapshot != null && _legacySnapshot.HasData;

        public bool HasSavedSpline => _splines.Count > 0;

        public bool HasSnapshot
        {
            get
            {
                if (HasLegacySnapshot) return true;

                for (var i = 0; i < _terrainSnapshots.Count; i++)
                {
                    var entry = _terrainSnapshots[i];
                    if (entry != null && entry.HasData) return true;
                }

                return false;
            }
        }

        public int SnapshotTerrainCount
        {
            get
            {
                var count = 0;

                for (var i = 0; i < _terrainSnapshots.Count; i++)
                {
                    var entry = _terrainSnapshots[i];
                    if (entry != null && entry.HasData) count++;
                }

                return count;
            }
        }

        public void AddTerrainSnapshot(TerrainData terrainData, TerrainSnapshot snapshot)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _terrainSnapshots.Add(new TerrainSnapshotEntry(terrainData, snapshot));
        }

        public void ClearSnapshots()
        {
            _terrainSnapshots.Clear();

            if (_legacySnapshot != null)
            {
                _legacySnapshot.Clear();
            }
        }

        public void SaveSpline(SplineContainer container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            _splines.Clear();

            foreach (var spline in container.Splines)
            {
                _splines.Add(new Spline(spline, spline.Closed));
            }

            var containerTransform = container.transform;
            _position = containerTransform.position;
            _rotation = containerTransform.rotation;
            _localScale = containerTransform.localScale;
        }

        public bool LoadSpline(SplineContainer container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (_splines.Count == 0) return false;

            var restored = new List<Spline>(_splines.Count);

            foreach (var spline in _splines)
            {
                restored.Add(new Spline(spline, spline.Closed));
            }

            container.Splines = restored;

            var containerTransform = container.transform;
            containerTransform.SetPositionAndRotation(_position, _rotation);
            containerTransform.localScale = _localScale;

            return true;
        }
    }
}
