#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace NextnityStudio.SplineRoad
{
    // Add this to the GameObject that holds the SplineContainer, assign the Terrain and a SplineRoadData asset (use Create Data Asset in the inspector).
    // Tune the profile, press Apply Road: Revert restores the original terrain, Bake keeps the road permanently, Restore Saved Spline reloads the stored spline.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class SplineRoadPainter : MonoBehaviour
    {
        public const string DataFieldName = nameof(_data);

        private const string LogPrefix = "SplineRoad";

        [SerializeField] private Terrain _terrain;
        [SerializeField] private SplineRoadData _data;
        [SerializeField] private RoadPreviewSettings _preview = new RoadPreviewSettings();

        private readonly SplinePolylineSampler _sampler = new SplinePolylineSampler();
        private readonly RoadTerrainBuilder _builder = new RoadTerrainBuilder();
        private SplineContainer _container;

        public Terrain Terrain => _terrain;

        public SplineRoadData Data => _data;

        public RoadPreviewSettings Preview => _preview;

        public bool IsReady => _terrain != null && _data != null;

        public bool HasAppliedRoad => _data != null && _data.Snapshot.HasData;

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

        private void Reset()
        {
            _terrain = Terrain.activeTerrain;
        }

        public void ApplyRoad()
        {
            if (!Validate()) return;

            RevertRoad();

            var profile = _data.Profile;
            var polylines = _sampler.Sample(Container, _terrain, profile);

            if (polylines.Count == 0)
            {
                LogError("The spline needs at least two knots.");
                return;
            }

            _builder.Build(_terrain, polylines, profile, _data.Snapshot);
            _data.SaveSpline(Container);
        }

        public void RevertRoad()
        {
            if (!HasAppliedRoad || _terrain == null) return;

            if (!_data.Snapshot.Restore(_terrain.terrainData))
            {
                LogError("Terrain resolution or layers changed since the road was applied. Snapshot discarded.");
            }

            _data.Snapshot.Clear();
        }

        public void BakeRoad()
        {
            if (_data == null) return;

            _data.Snapshot.Clear();
        }

        public void LoadSavedSpline()
        {
            if (_data == null) return;

            if (!_data.LoadSpline(Container))
            {
                LogError("The data asset has no saved spline yet.");
            }
        }

        public List<Vector3[]> SamplePolylines()
        {
            if (!IsReady) return new List<Vector3[]>();

            return _sampler.Sample(Container, _terrain, _data.Profile);
        }

        private bool Validate()
        {
            if (_terrain == null)
            {
                LogError("Terrain is not assigned.");
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
    }
}

#endif
