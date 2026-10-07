#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    public enum RoadsideKind
    {
        Tree,
        Detail
    }

    public enum RoadsideSide
    {
        Both,
        Left,
        Right
    }

    [Serializable]
    public sealed class RoadsideRule : ISerializationCallbackReceiver
    {
        public const string EnabledFieldName = nameof(_enabled);
        public const string KindFieldName = nameof(_kind);
        public const string PrototypeFieldName = nameof(_prototypeIndex);
        public const string PrototypesFieldName = nameof(_prototypeIndexes);
        public const string SeparationFieldName = nameof(_minSeparation);
        public const string SideFieldName = nameof(_side);
        public const string OffsetMinFieldName = nameof(_offsetMin);
        public const string OffsetMaxFieldName = nameof(_offsetMax);
        public const string ChanceFieldName = nameof(_chance);
        public const string MaxSlopeFieldName = nameof(_maxSlope);
        public const string SeedFieldName = nameof(_seed);
        public const string SpacingFieldName = nameof(_spacing);
        public const string AlongJitterFieldName = nameof(_alongJitter);
        public const string ScaleMinFieldName = nameof(_scaleMin);
        public const string ScaleMaxFieldName = nameof(_scaleMax);
        public const string HeightVariationFieldName = nameof(_heightVariation);
        public const string RandomRotationFieldName = nameof(_randomRotation);
        public const string TintVariationFieldName = nameof(_tintVariation);
        public const string HeightOffsetFieldName = nameof(_heightOffset);
        public const string DensityFieldName = nameof(_density);
        public const string EdgeFadeFieldName = nameof(_edgeFade);

        private const float MinSpacing = 0.1f;

        [SerializeField] private bool _enabled = true;
        [SerializeField] private RoadsideKind _kind = RoadsideKind.Tree;
        [HideInInspector, SerializeField] private int _prototypeIndex = -1;
        [SerializeField, Tooltip("Prototypes to pick from per placement.")] private List<int> _prototypeIndexes = new List<int> { 0 };
        [SerializeField, Min(0f), Tooltip("Trees only: minimum distance between new placements.")] private float _minSeparation = 2f;
        [SerializeField] private RoadsideSide _side = RoadsideSide.Both;
        [SerializeField, Min(0f)] private float _offsetMin = 1f;
        [SerializeField, Min(0f)] private float _offsetMax = 4f;
        [SerializeField, Range(0f, 1f)] private float _chance = 0.8f;
        [SerializeField, Range(0f, 90f)] private float _maxSlope = 45f;
        [SerializeField] private int _seed = 1;
        [SerializeField, Min(0.25f)] private float _spacing = 4f;
        [SerializeField, Range(0f, 1f)] private float _alongJitter = 0.5f;
        [SerializeField, Min(0.01f)] private float _scaleMin = 0.8f;
        [SerializeField, Min(0.01f)] private float _scaleMax = 1.2f;
        [SerializeField, Range(0f, 1f)] private float _heightVariation = 0.15f;
        [SerializeField] private bool _randomRotation = true;
        [SerializeField, Range(0f, 1f)] private float _tintVariation = 0.1f;
        [SerializeField] private float _heightOffset;
        [SerializeField, Range(0, 16)] private int _density = 6;
        [SerializeField, Range(0f, 1f)] private float _edgeFade = 0.3f;

        public RoadsideRule()
        {
        }

        public RoadsideRule(RoadsideKind kind)
        {
            _kind = kind;
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if ((_prototypeIndexes == null || _prototypeIndexes.Count == 0) && _prototypeIndex >= 0)
            {
                _prototypeIndexes = new List<int> { _prototypeIndex };
                _prototypeIndex = -1;
            }
        }

        public List<int> ValidPrototypes(int prototypeCount)
        {
            var valid = new List<int>();
            if (_prototypeIndexes == null) return valid;

            for (var i = 0; i < _prototypeIndexes.Count; i++)
            {
                var index = _prototypeIndexes[i];
                if (index < 0 || index >= prototypeCount || valid.Contains(index)) continue;

                valid.Add(index);
            }

            return valid;
        }

        public bool Enabled => _enabled;

        public RoadsideKind Kind => _kind;

        public int PrototypeIndex => _prototypeIndex;

        public IReadOnlyList<int> PrototypeIndexes => _prototypeIndexes;

        public float MinSeparation => _minSeparation;

        public RoadsideSide Side => _side;

        public float OffsetMin => _offsetMin;

        public float OffsetMax => Mathf.Max(_offsetMin, _offsetMax);

        public float Chance => _chance;

        public float MaxSlope => _maxSlope;

        public int Seed => _seed;

        public float Spacing => Mathf.Max(_spacing, MinSpacing);

        public float AlongJitter => _alongJitter;

        public float ScaleMin => _scaleMin;

        public float ScaleMax => Mathf.Max(_scaleMin, _scaleMax);

        public float HeightVariation => _heightVariation;

        public bool RandomRotation => _randomRotation;

        public float TintVariation => _tintVariation;

        public float HeightOffset => _heightOffset;

        public int Density => _density;

        public float EdgeFade => _edgeFade;
    }
}

#endif
