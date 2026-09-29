using System;
using System.Collections.Generic;
using UnityEngine;
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
        [SerializeField] private TerrainSnapshot _snapshot = new TerrainSnapshot();

        public RoadProfile Profile => _profile;

        public TerrainSnapshot Snapshot => _snapshot;

        public bool HasSavedSpline => _splines.Count > 0;

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
