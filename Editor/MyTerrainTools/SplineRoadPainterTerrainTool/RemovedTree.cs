#if UNITY_EDITOR
using System;
using UnityEngine;

namespace NextnityStudio.SplineRoad
{
    [Serializable]
    public struct RemovedTree
    {
        [SerializeField] private Vector3 _position;
        [SerializeField] private float _widthScale;
        [SerializeField] private float _heightScale;
        [SerializeField] private float _rotation;
        [SerializeField] private int _prototypeIndex;
        [SerializeField] private Color32 _color;
        [SerializeField] private Color32 _lightmapColor;

        public RemovedTree(TreeInstance tree)
        {
            _position = tree.position;
            _widthScale = tree.widthScale;
            _heightScale = tree.heightScale;
            _rotation = tree.rotation;
            _prototypeIndex = tree.prototypeIndex;
            _color = tree.color;
            _lightmapColor = tree.lightmapColor;
        }

        public int PrototypeIndex => _prototypeIndex;

        public TreeInstance ToInstance()
        {
            return new TreeInstance
            {
                position = _position,
                widthScale = _widthScale,
                heightScale = _heightScale,
                rotation = _rotation,
                prototypeIndex = _prototypeIndex,
                color = _color,
                lightmapColor = _lightmapColor
            };
        }
    }
}

#endif
