using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    [Serializable]
    public sealed class RoadProfile
    {
        public const string WidthFieldName = nameof(_width);
        public const string ShoulderFieldName = nameof(_shoulderWidth);
        public const string HeightOffsetFieldName = nameof(_heightOffset);
        public const string TextureFeatherFieldName = nameof(_textureFeather);
        public const string VegetationClearanceFieldName = nameof(_vegetationClearance);
        public const string RoadsideFieldName = nameof(_roadside);

        private const float Half = 0.5f;

        [Header("Shape")]
        [SerializeField, Min(0.5f)] private float _width = 6f;
        [SerializeField, Min(0f)] private float _shoulderWidth = 4f;
        [SerializeField] private float _heightOffset;
        [SerializeField, Range(0f, 1f)] private float _heightStrength = 1f;
        [SerializeField] private AnimationCurve _shoulderFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Sampling")]
        [SerializeField] private bool _followTerrain = true;
        [SerializeField, Range(0, 256)] private int _smoothingPasses = 24;
        [SerializeField, Min(0.25f)] private float _sampleSpacing = 1f;

        [Header("Texture")]
        [SerializeField] private bool _paintTexture = true;
        [SerializeField] private TerrainLayer _roadLayer;
        [SerializeField, Min(0f)] private float _textureFeather = 1f;
        [SerializeField, Range(0f, 1f)] private float _textureOpacity = 1f;

        [Header("Vegetation")]
        [SerializeField] private bool _clearDetails = true;
        [SerializeField] private bool _clearTrees = true;
        [SerializeField, Min(0f)] private float _vegetationClearance = 1f;

        [Header("Roadside")]
        [SerializeField] private List<RoadsideRule> _roadside = new List<RoadsideRule>();

        public float HalfWidth => _width * Half;

        public float ShoulderWidth => _shoulderWidth;

        public float TextureFeather => _textureFeather;

        public float VegetationClearance => _vegetationClearance;

        public float HeightOffset => _heightOffset;

        public bool FollowTerrain => _followTerrain;

        public int SmoothingPasses => _smoothingPasses;

        public float SampleSpacing => _sampleSpacing;

        public bool PaintTexture => _paintTexture;

        public TerrainLayer RoadLayer => _roadLayer;

        public bool ClearDetails => _clearDetails;

        public bool ClearTrees => _clearTrees;

        public IReadOnlyList<RoadsideRule> Roadside => _roadside;

        public float VegetationRadius => HalfWidth + _vegetationClearance;

        public float Reach
        {
            get
            {
                var texture = _paintTexture ? _textureFeather : 0f;
                var vegetation = _clearDetails || _clearTrees ? _vegetationClearance : 0f;
                var reach = HalfWidth + Mathf.Max(_shoulderWidth, texture, vegetation);

                if (_roadside != null)
                {
                    for (var i = 0; i < _roadside.Count; i++)
                    {
                        var rule = _roadside[i];
                        if (rule == null || !rule.Enabled) continue;
                        reach = Mathf.Max(reach, HalfWidth + rule.OffsetMax);
                    }
                }

                return reach;
            }
        }

        public float HeightWeight(float distance)
        {
            if (distance <= HalfWidth) return _heightStrength;
            if (distance >= HalfWidth + _shoulderWidth) return 0f;

            var t = (distance - HalfWidth) / _shoulderWidth;
            return Mathf.Clamp01(_shoulderFalloff.Evaluate(t)) * _heightStrength;
        }

        public float TextureWeight(float distance)
        {
            if (distance <= HalfWidth) return _textureOpacity;
            if (distance >= HalfWidth + _textureFeather) return 0f;

            var t = (distance - HalfWidth) / _textureFeather;
            return (1f - Mathf.SmoothStep(0f, 1f, t)) * _textureOpacity;
        }
    }
}
