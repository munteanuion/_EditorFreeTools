using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NextnityStudio.SplineRoad.EditorTools
{
    public sealed class RoadPreviewDrawer
    {
        private const int ProfileSteps = 6;
        private const int MinLinePoints = 2;
        private const int MaxTreeMarkers = 4000;
        private const float LineWidth = 2f;
        private const float MarkerRadius = 0.6f;
        private const float LabelLift = 1f;
        private const float SidePositive = 1f;
        private const float SideNegative = -1f;

        private readonly Color _edgeColor = Color.cyan;
        private readonly Color _shoulderColor = new Color(1f, 0.6f, 0.1f);
        private readonly Color _textureColor = Color.yellow;
        private readonly Color _vegetationColor = Color.green;
        private readonly Color _cutColor = new Color(1f, 0.25f, 0.25f);
        private readonly Color _fillColor = new Color(0.3f, 0.55f, 1f);
        private readonly Color _surfaceColor = new Color(0.85f, 0.85f, 0.85f);
        private readonly Color _roadsideColor = new Color(1f, 0.3f, 0.8f);

        private readonly List<Vector3> _leftEdge = new List<Vector3>();
        private readonly List<Vector3> _rightEdge = new List<Vector3>();
        private readonly List<Vector3> _leftShoulder = new List<Vector3>();
        private readonly List<Vector3> _rightShoulder = new List<Vector3>();
        private readonly List<Vector3> _leftTexture = new List<Vector3>();
        private readonly List<Vector3> _rightTexture = new List<Vector3>();
        private readonly List<Vector3> _leftVegetation = new List<Vector3>();
        private readonly List<Vector3> _rightVegetation = new List<Vector3>();
        private readonly List<Vector3> _roadsideInnerLeft = new List<Vector3>();
        private readonly List<Vector3> _roadsideOuterLeft = new List<Vector3>();
        private readonly List<Vector3> _roadsideInnerRight = new List<Vector3>();
        private readonly List<Vector3> _roadsideOuterRight = new List<Vector3>();
        private readonly List<Vector3> _cutLines = new List<Vector3>();
        private readonly List<Vector3> _fillLines = new List<Vector3>();
        private readonly Vector3[] _quad = new Vector3[4];
        private readonly RoadsideTreeScatterer _treePreview = new RoadsideTreeScatterer();

        private Terrain _terrain;
        private RoadProfile _profile;
        private RoadPreviewSettings _settings;
        private float _baseHeight;

        #region Draw

        public void Draw(IReadOnlyList<Vector3[]> polylines, Terrain terrain, RoadProfile profile, RoadPreviewSettings settings)
        {
            if (polylines == null || terrain == null || profile == null || settings == null) return;

            _terrain = terrain;
            _profile = profile;
            _settings = settings;
            _baseHeight = terrain.transform.position.y;

            var originalColor = Handles.color;

            for (var p = 0; p < polylines.Count; p++)
            {
                DrawPolyline(polylines[p]);
            }

            if (_settings.ShowTreePreview)
            {
                DrawTreePlacements(polylines);
            }

            Handles.color = originalColor;
        }

        private void DrawPolyline(Vector3[] points)
        {
            ClearLists();

            var last = points.Length - 1;
            var half = _profile.HalfWidth;
            var hasPrevious = false;
            var previousLeft = Vector3.zero;
            var previousRight = Vector3.zero;

            for (var i = 0; i <= last; i = NextIndex(i, last))
            {
                var frame = new RoadFrame(points, i);
                var target = frame.Center.y + _profile.HeightOffset;
                var leftEdge = frame.Offset(-half, target);
                var rightEdge = frame.Offset(half, target);

                _leftEdge.Add(leftEdge);
                _rightEdge.Add(rightEdge);

                if (_settings.ShowRoad && hasPrevious)
                {
                    DrawSurface(previousLeft, previousRight, rightEdge, leftEdge);
                }

                previousLeft = leftEdge;
                previousRight = rightEdge;
                hasPrevious = true;

                CollectOutlines(frame);

                if (_settings.ShowCutFill)
                {
                    CollectVerticals(frame, target);
                }

                if (_settings.ShowCrossSections)
                {
                    CollectCrossSection(frame, target, SideNegative);
                    CollectCrossSection(frame, target, SidePositive);
                }
            }

            DrawLists();

            if (_settings.ShowRoadside)
            {
                DrawRoadsideBands(points);
            }
        }

        private int NextIndex(int index, int last)
        {
            return index >= last ? last + 1 : Mathf.Min(index + _settings.Stride, last);
        }

        private void DrawSurface(Vector3 previousLeft, Vector3 previousRight, Vector3 right, Vector3 left)
        {
            _quad[0] = previousLeft;
            _quad[1] = previousRight;
            _quad[2] = right;
            _quad[3] = left;

            Handles.color = new Color(_surfaceColor.r, _surfaceColor.g, _surfaceColor.b, _settings.SurfaceOpacity);
            Handles.DrawAAConvexPolygon(_quad);
        }

        #endregion

        #region Collect

        private void CollectOutlines(RoadFrame frame)
        {
            var half = _profile.HalfWidth;

            if (_settings.ShowShoulder)
            {
                AddOutline(frame, half + _profile.ShoulderWidth, _leftShoulder, _rightShoulder);
            }

            if (_settings.ShowTexture && _profile.PaintTexture)
            {
                AddOutline(frame, half + _profile.TextureFeather, _leftTexture, _rightTexture);
            }

            if (_settings.ShowVegetation && (_profile.ClearDetails || _profile.ClearTrees))
            {
                AddOutline(frame, _profile.VegetationRadius, _leftVegetation, _rightVegetation);
            }
        }

        private void AddOutline(RoadFrame frame, float distance, List<Vector3> left, List<Vector3> right)
        {
            left.Add(OnTerrain(frame.Offset(-distance, 0f)));
            right.Add(OnTerrain(frame.Offset(distance, 0f)));
        }

        private void DrawRoadsideBands(Vector3[] points)
        {
            var rules = _profile.Roadside;
            if (rules == null) return;

            for (var r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule == null || !rule.Enabled) continue;

                DrawRoadsideRule(points, rule, r);
            }
        }

        private void DrawRoadsideRule(Vector3[] points, RoadsideRule rule, int ruleIndex)
        {
            _roadsideInnerLeft.Clear();
            _roadsideOuterLeft.Clear();
            _roadsideInnerRight.Clear();
            _roadsideOuterRight.Clear();

            var last = points.Length - 1;
            var inner = _profile.HalfWidth + rule.OffsetMin;
            var outer = _profile.HalfWidth + rule.OffsetMax;

            for (var i = 0; i <= last; i = NextIndex(i, last))
            {
                var frame = new RoadFrame(points, i);

                if (rule.Side != RoadsideSide.Right)
                {
                    _roadsideInnerLeft.Add(OnTerrain(frame.Offset(-inner, 0f)));
                    _roadsideOuterLeft.Add(OnTerrain(frame.Offset(-outer, 0f)));
                }

                if (rule.Side != RoadsideSide.Left)
                {
                    _roadsideInnerRight.Add(OnTerrain(frame.Offset(inner, 0f)));
                    _roadsideOuterRight.Add(OnTerrain(frame.Offset(outer, 0f)));
                }
            }

            Handles.color = _roadsideColor;
            DrawBandLine(_roadsideInnerLeft);
            DrawBandLine(_roadsideOuterLeft);
            DrawBandLine(_roadsideInnerRight);
            DrawBandLine(_roadsideOuterRight);

            var labelLine = _roadsideOuterLeft.Count > 0 ? _roadsideOuterLeft : _roadsideOuterRight;
            if (labelLine.Count > 0)
            {
                var kind = rule.Kind == RoadsideKind.Tree ? "Tree" : "Detail";
                Handles.Label(labelLine[0] + Vector3.up * LabelLift, $"{kind} {ruleIndex}");
            }
        }

        private void DrawBandLine(List<Vector3> line)
        {
            if (line.Count < MinLinePoints) return;

            Handles.DrawAAPolyLine(LineWidth, line.ToArray());
        }

        private void DrawTreePlacements(IReadOnlyList<Vector3[]> polylines)
        {
            var placements = _treePreview.PreviewPlacements(_terrain.terrainData, _terrain.transform.position, polylines, _profile);
            if (placements.Count == 0) return;

            Handles.color = _roadsideColor;
            var step = Mathf.Max(1, Mathf.CeilToInt(placements.Count / (float)MaxTreeMarkers));

            for (var i = 0; i < placements.Count; i += step)
            {
                var placement = placements[i];
                Handles.DrawWireDisc(placement.WorldPosition, Vector3.up, MarkerRadius * placement.Scale);
            }
        }

        private void CollectVerticals(RoadFrame frame, float target)
        {
            var half = _profile.HalfWidth;

            AddVertical(frame.Offset(-half, 0f), target);
            AddVertical(frame.Offset(0f, 0f), target);
            AddVertical(frame.Offset(half, 0f), target);
        }

        private void AddVertical(Vector3 position, float target)
        {
            var ground = OnTerrain(position);
            var list = ground.y > target ? _cutLines : _fillLines;

            list.Add(ground);
            list.Add(new Vector3(ground.x, target, ground.z));
        }

        private void CollectCrossSection(RoadFrame frame, float target, float side)
        {
            if (_profile.ShoulderWidth <= 0f) return;

            var half = _profile.HalfWidth;
            var previous = frame.Offset(side * half, target);

            for (var step = 1; step <= ProfileSteps; step++)
            {
                var distance = half + _profile.ShoulderWidth * step / ProfileSteps;
                var ground = OnTerrain(frame.Offset(side * distance, 0f));
                var height = Mathf.Lerp(ground.y, target, _profile.HeightWeight(distance));
                var current = new Vector3(ground.x, height, ground.z);
                var list = ground.y > target ? _cutLines : _fillLines;

                list.Add(previous);
                list.Add(current);
                previous = current;
            }
        }

        private Vector3 OnTerrain(Vector3 position)
        {
            position.y = _terrain.SampleHeight(position) + _baseHeight;
            return position;
        }

        #endregion

        #region Lines

        private void DrawLists()
        {
            if (_settings.ShowRoad)
            {
                DrawLine(_leftEdge, _edgeColor);
                DrawLine(_rightEdge, _edgeColor);
            }

            DrawLine(_leftShoulder, _shoulderColor);
            DrawLine(_rightShoulder, _shoulderColor);
            DrawLine(_leftTexture, _textureColor);
            DrawLine(_rightTexture, _textureColor);
            DrawLine(_leftVegetation, _vegetationColor);
            DrawLine(_rightVegetation, _vegetationColor);
            DrawSegments(_cutLines, _cutColor);
            DrawSegments(_fillLines, _fillColor);
        }

        private void DrawLine(List<Vector3> points, Color color)
        {
            if (points.Count < MinLinePoints) return;

            Handles.color = color;
            Handles.DrawAAPolyLine(LineWidth, points.ToArray());
        }

        private void DrawSegments(List<Vector3> segments, Color color)
        {
            if (segments.Count < MinLinePoints) return;

            Handles.color = color;
            Handles.DrawLines(segments.ToArray());
        }

        private void ClearLists()
        {
            _leftEdge.Clear();
            _rightEdge.Clear();
            _leftShoulder.Clear();
            _rightShoulder.Clear();
            _leftTexture.Clear();
            _rightTexture.Clear();
            _leftVegetation.Clear();
            _rightVegetation.Clear();
            _cutLines.Clear();
            _fillLines.Clear();
        }

        #endregion
    }
}
