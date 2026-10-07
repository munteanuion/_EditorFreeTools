#if UNITY_EDITOR
using System;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    [Serializable]
    public sealed class RoadPreviewSettings
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private bool _showHandles = true;
        [SerializeField] private bool _showRoad = true;
        [SerializeField] private bool _showCrossSections = true;
        [SerializeField] private bool _showCutFill = true;
        [SerializeField] private bool _showShoulder = true;
        [SerializeField] private bool _showTexture = true;
        [SerializeField] private bool _showVegetation = true;
        [SerializeField] private bool _showRoadside = true;
        [SerializeField] private bool _showTreePreview = true;
        [SerializeField, Range(1, 32)] private int _stride = 3;
        [SerializeField, Range(0.05f, 1f)] private float _surfaceOpacity = 0.35f;

        public bool Enabled => _enabled;

        public bool ShowHandles => _showHandles;

        public bool ShowRoad => _showRoad;

        public bool ShowCrossSections => _showCrossSections;

        public bool ShowCutFill => _showCutFill;

        public bool ShowShoulder => _showShoulder;

        public bool ShowTexture => _showTexture;

        public bool ShowVegetation => _showVegetation;

        public bool ShowRoadside => _showRoadside;

        public bool ShowTreePreview => _showTreePreview;

        public int Stride => _stride;

        public float SurfaceOpacity => _surfaceOpacity;
    }
}

#endif
