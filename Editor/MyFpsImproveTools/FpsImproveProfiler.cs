/*
// FpsImproveProfiler.cs (UPDATED: adds Replace Shader/Material for audit rules)
// Place this file under: Assets/Editor/FpsImproveProfiler.cs

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tools.FpsImproveProfiler
{
    public static class FpsImproveProfiler
    {
        [MenuItem("Tools/FPS Improve Profiler")]
        private static void Open()
        {
            FpsImproveProfilerWindow.ShowWindow();
        }
    }

    internal sealed class FpsImproveProfilerWindow : EditorWindow
    {
        #region Attributes

        private OptimizationRuleType _ruleType;
        private readonly RuleRegistry _registry = new RuleRegistry();

        private IOptimizationRule _activeRule;
        private RuleSettings _settings = new RuleSettings();

        private SearchField _searchField;
        private string _searchText = string.Empty;

        private FindingsTreeView _tree;
        private TreeViewState _treeState;
        private MultiColumnHeader _treeHeader;

        private Vector2 _rightPanelScroll;

        private int _lastScanCount;
        private double _lastScanMs;

        #endregion

        #region Unity Lifecycle

        public static void ShowWindow()
        {
            var window = GetWindow<FpsImproveProfilerWindow>("FPS Improve Profiler");
            window.minSize = new Vector2(1100, 520);
            window.Show();
        }

        private void OnEnable()
        {
            // Explicit null checks instead of C# 8 '??=' for Unity compatibility
            if (_searchField == null) _searchField = new SearchField();

            if (_treeState == null) _treeState = new TreeViewState();
            if (_treeHeader == null) _treeHeader = FindingsTreeView.CreateHeader();

            _tree = new FindingsTreeView(_treeState, _treeHeader);
            _tree.OnSelectionChanged += OnTreeSelectionChanged;

            _registry.RegisterDefaults();
            SetActiveRule(_ruleType);
        }

        private void OnGUI()
        {
            DrawTopBar();
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawRightPanel();
            }

            EditorGUILayout.Space(6);
            DrawBottomBar();
        }

        #endregion

        #region UI

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var newType = (OptimizationRuleType)EditorGUILayout.EnumPopup(_ruleType, EditorStyles.toolbarPopup, GUILayout.Width(320));
                if (newType != _ruleType)
                {
                    _ruleType = newType;
                    SetActiveRule(_ruleType);
                }

                GUILayout.Space(8);

                _searchText = _searchField.OnToolbarGUI(_searchText);
                _tree.SetSearchText(_searchText);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_activeRule == null))
                {
                    if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(90)))
                        Scan();
                }

                using (new EditorGUI.DisabledScope(_activeRule == null || _tree.TotalCount == 0))
                {
                    if (GUILayout.Button("Optimize Selected", EditorStyles.toolbarButton, GUILayout.Width(160)))
                        OptimizeSelected();
                }
            }
        }

        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.62f)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Findings", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"Last scan: {_lastScanCount} items, {_lastScanMs:0.0} ms", EditorStyles.miniLabel, GUILayout.Width(260));
                }

                var rect = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                _tree.OnGUI(rect);
            }
        }

        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField("Rule Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space(2);

                _rightPanelScroll = EditorGUILayout.BeginScrollView(_rightPanelScroll);

                if (_activeRule == null)
                {
                    EditorGUILayout.HelpBox("No rule selected.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField(_activeRule.DisplayName, EditorStyles.largeLabel);
                    EditorGUILayout.Space(6);

                    EditorGUILayout.HelpBox(_activeRule.Description, MessageType.None);
                    EditorGUILayout.Space(10);

                    _activeRule.DrawSettingsGUI(ref _settings);

                    EditorGUILayout.Space(12);
                    DrawSelectionActions();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectionActions()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

                var selectedCount = _tree.SelectedCount;
                EditorGUILayout.LabelField($"Selected: {selectedCount}", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedCount == 0))
                    {
                        if (GUILayout.Button("Ping"))
                            PingSelected();

                        if (GUILayout.Button("Select In Hierarchy"))
                            SelectInHierarchy();
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(_tree.TotalCount == 0))
                    {
                        if (GUILayout.Button("Select All", GUILayout.Width(110)))
                            _tree.SelectAll();

                        if (GUILayout.Button("Clear", GUILayout.Width(110)))
                            _tree.ClearSelection();
                    }
                }
            }
        }

        private void DrawBottomBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Tip: Folosește Ctrl/Shift în listă pentru selecție multiplă (TreeView).", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_activeRule == null))
                {
                    if (GUILayout.Button("Re-scan", GUILayout.Width(110)))
                        Scan();
                }
            }
        }

        #endregion

        #region Core

        private void SetActiveRule(OptimizationRuleType type)
        {
            _activeRule = _registry.TryCreate(type);
            _settings = new RuleSettings(); // reset per rule
            _tree.SetItems(Array.Empty<FindingItem>());
            _lastScanCount = 0;
            _lastScanMs = 0;
        }

        private void Scan()
        {
            if (_activeRule == null) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ctx = new RuleContext(_settings);
            var findings = _activeRule.Scan(ctx).ToArray();
            sw.Stop();

            _lastScanCount = findings.Length;
            _lastScanMs = sw.Elapsed.TotalMilliseconds;

            _tree.SetItems(findings);
            Repaint();
        }

        private void OptimizeSelected()
        {
            if (_activeRule == null) return;

            var selected = _tree.GetSelectedItems();
            if (selected.Count == 0) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"FPS Improve Profiler - {_activeRule.DisplayName}");

            var ctx = new RuleContext(_settings);
            _activeRule.Apply(ctx, selected);

            Scan(); // refresh
        }

        private void PingSelected()
        {
            var items = _tree.GetSelectedItems();
            if (items.Count == 0) return;

            var first = items[0];
            var obj = first.Object;
            if (obj == null) return;

            EditorGUIUtility.PingObject(obj);
        }

        private void SelectInHierarchy()
        {
            var items = _tree.GetSelectedItems();
            if (items.Count == 0) return;

            var unityObjects = items.Select(i => i.Object).Where(o => o != null).ToArray();
            Selection.objects = unityObjects;
        }

        private void OnTreeSelectionChanged()
        {
            // optional
        }

        #endregion
    }

    #region TreeView

    internal sealed class FindingsTreeView : TreeView
    {
        #region Attributes

        private readonly List<TreeViewItem> _rows = new List<TreeViewItem>(2048);
        private FindingItem[] _items = Array.Empty<FindingItem>();
        private string _search = string.Empty;

        public int TotalCount => _items?.Length ?? 0;
        public int SelectedCount => GetSelection()?.Count ?? 0;

        public event Action OnSelectionChanged;

        private enum Col
        {
            Type = 0,
            Name = 1,
            Path = 2,
            Details = 3
        }

        #endregion

        #region Ctor

        public FindingsTreeView(TreeViewState state, MultiColumnHeader header) : base(state, header)
        {
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            rowHeight = 20;
            Reload();
        }

        #endregion

        #region Public API

        public void SetItems(FindingItem[] items)
        {
            _items = items ?? Array.Empty<FindingItem>();
            Reload();
        }

        public void SetSearchText(string search)
        {
            _search = search ?? string.Empty;
            Reload();
        }

        // Provide a small helper so the window code can Clear selection
        public void ClearSelection()
        {
            SetSelection(new List<int>(), TreeViewSelectionOptions.None);
        }

        public List<FindingItem> GetSelectedItems()
        {
            var sel = GetSelection();
            if (sel == null || sel.Count == 0) return new List<FindingItem>(0);

            var map = new Dictionary<int, FindingItem>(_items.Length);
            foreach (var it in _items)
                map[it.Id] = it;

            var result = new List<FindingItem>(sel.Count);
            foreach (var id in sel)
                if (map.TryGetValue(id, out var item)) result.Add(item);

            return result;
        }

        public void SelectAll()
        {
            if (_items == null || _items.Length == 0) return;
            SetSelection(_items.Select(i => i.Id).ToList(), TreeViewSelectionOptions.RevealAndFrame);
        }

        #endregion

        #region TreeView Overrides

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "root" };
            _rows.Clear();

            IEnumerable<FindingItem> src = _items;

            if (!string.IsNullOrWhiteSpace(_search))
            {
                var s = _search.Trim();
                src = src.Where(i =>
                    (i.Name?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.Path?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.Details?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                );
            }

            foreach (var item in src)
                _rows.Add(new TreeViewItem(item.Id, 0, item.Name));

            SetupParentsAndChildrenFromDepths(root, _rows);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var id = args.item.id;
            var item = FindItemById(id);
            if (item == null)
            {
                base.RowGUI(args);
                return;
            }

            for (var i = 0; i < args.GetNumVisibleColumns(); i++)
            {
                var col = (Col)args.GetColumn(i);
                var rect = args.GetCellRect(i);

                switch (col)
                {
                    case Col.Type:
                        EditorGUI.LabelField(rect, item.Kind.ToString(), EditorStyles.miniLabel);
                        break;

                    case Col.Name:
                        DrawNameCell(rect, item);
                        break;

                    case Col.Path:
                        EditorGUI.LabelField(rect, item.Path ?? "", EditorStyles.miniLabel);
                        break;

                    case Col.Details:
                        EditorGUI.LabelField(rect, item.Details ?? "", EditorStyles.miniLabel);
                        break;
                }
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            OnSelectionChanged?.Invoke();
        }

        #endregion

        #region Helpers

        private void DrawNameCell(Rect rect, FindingItem item)
        {
            var obj = item.Object;
            var icon = obj != null ? AssetPreview.GetMiniThumbnail(obj) : null;

            var r = rect;
            r.x += 2;

            if (icon != null)
            {
                var iconRect = new Rect(r.x, r.y + 1, 18, 18);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                r.x += 20;
                r.width -= 20;
            }

            EditorGUI.LabelField(r, item.Name ?? "", EditorStyles.label);

            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && rect.Contains(Event.current.mousePosition))
            {
                if (obj != null)
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
                Event.current.Use();
            }
        }

        private FindingItem FindItemById(int id)
        {
            if (_items == null) return null;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Id == id) return _items[i];
            return null;
        }

        public static MultiColumnHeader CreateHeader()
        {
            var cols = new[]
            {
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Type"), width = 90,  minWidth = 80,  canSort = false, autoResize = true },
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Name"), width = 260, minWidth = 180, canSort = false, autoResize = true },
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Path"), width = 280, minWidth = 220, canSort = false, autoResize = true },
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Details"), width = 360, minWidth = 260, canSort = false, autoResize = true }
            };

            var state = new MultiColumnHeaderState(cols);
            var header = new MultiColumnHeader(state) { height = 22 };
            header.ResizeToFit();
            return header;
        }

        #endregion
    }

    #endregion

    #region Models

    internal enum OptimizationRuleType
    {
        RendererMaterials_ReceiveShadowsOn,
        Renderer_CastShadowsOn,
        StaticBatching_NotEnabled,
        Lights_NotBaked,
        Lights_WithShadowsEnabled,
        Materials_ReflectionEnvironmentEnabled,
        Materials_TransparentToOpaque,

        Renderers_WithSelectedShader,          // now supports replace
        Renderers_WithShaderGraphShaders,      // now supports replace
        Materials_AlphaClippingEnabled,
        StaticShadowCaster_Enabled,
        Particles_WithSelectedShader,          // now supports replace
        Materials_ReceiveShadowsOn,            // new optimization for material receive shadows
    }

    internal enum FindingKind
    {
        GameObject,
        Renderer,
        Material,
        Light,
        ParticleSystem
    }

    internal sealed class FindingItem
    {
        public int Id;
        public FindingKind Kind;

        public UnityEngine.Object Object;
        public string Name;
        public string Path;
        public string Details;

        public Renderer Renderer;
        public Material Material;
        public Light Light;
        public ParticleSystem ParticleSystem;

        // For replace operations
        public int MaterialIndex = -1;

        public static FindingItem FromRenderer(int id, Renderer r, string details = null)
        {
            return new FindingItem
            {
                Id = id,
                Kind = FindingKind.Renderer,
                Object = r,
                Renderer = r,
                Name = r != null ? r.name : "<null>",
                Path = r != null ? HierarchyPath.Of(r.gameObject) : "",
                Details = details ?? ""
            };
        }

        public static FindingItem FromGameObject(int id, GameObject go, string details = null)
        {
            return new FindingItem
            {
                Id = id,
                Kind = FindingKind.GameObject,
                Object = go,
                Name = go != null ? go.name : "<null>",
                Path = go != null ? HierarchyPath.Of(go) : "",
                Details = details ?? ""
            };
        }

        public static FindingItem FromMaterial(int id, Material mat, Renderer owner, int materialIndex, string details = null)
        {
            return new FindingItem
            {
                Id = id,
                Kind = FindingKind.Material,
                Object = mat,
                Material = mat,
                Renderer = owner,
                MaterialIndex = materialIndex,
                Name = mat != null ? mat.name : "<null>",
                Path = owner != null ? HierarchyPath.Of(owner.gameObject) : "",
                Details = details ?? ""
            };
        }

        public static FindingItem FromLight(int id, Light light, string details = null)
        {
            return new FindingItem
            {
                Id = id,
                Kind = FindingKind.Light,
                Object = light,
                Light = light,
                Name = light != null ? light.name : "<null>",
                Path = light != null ? HierarchyPath.Of(light.gameObject) : "",
                Details = details ?? ""
            };
        }

        public static FindingItem FromParticleSystem(int id, ParticleSystem ps, string details = null)
        {
            return new FindingItem
            {
                Id = id,
                Kind = FindingKind.ParticleSystem,
                Object = ps,
                ParticleSystem = ps,
                Name = ps != null ? ps.name : "<null>",
                Path = ps != null ? HierarchyPath.Of(ps.gameObject) : "",
                Details = details ?? ""
            };
        }
    }

    internal sealed class RuleSettings
    {
        // Common toggles / values used by multiple rules
        public bool SetBooleanValue = false;

        // Shader filters
        public Shader TargetShader = null;
        public Shader TargetParticleShader = null;

        // Transparency -> Opaque
        public bool ForceOpaque = true;

        // Particle count filtering
        public bool EnableParticleCountThreshold = false;
        public int ParticleCountThreshold = 50;
        public bool UseLiveParticleCountInPlayMode = true;

        // Replace controls (NEW)
        public ReplaceMode ReplaceMode = ReplaceMode.ReplaceShaderOnly;
        public Shader ReplaceShader = null;
        public Material ReplaceMaterial = null;
        public bool AffectAllMaterialsOnRenderer = false;
    }

    internal enum ReplaceMode
    {
        ReplaceShaderOnly,
        ReplaceMaterialReference
    }

    internal readonly struct RuleContext
    {
        public readonly RuleSettings Settings;

        public RuleContext(RuleSettings settings)
        {
            Settings = settings;
        }
    }

    internal interface IOptimizationRule
    {
        OptimizationRuleType Type { get; }
        string DisplayName { get; }
        string Description { get; }

        void DrawSettingsGUI(ref RuleSettings settings);

        IEnumerable<FindingItem> Scan(RuleContext context);

        void Apply(RuleContext context, IReadOnlyList<FindingItem> selected);
    }

    internal sealed class RuleRegistry
    {
        // Explicit dictionary construction instead of target-typed 'new()' for compatibility
        private readonly Dictionary<OptimizationRuleType, Func<IOptimizationRule>> _factories = new Dictionary<OptimizationRuleType, Func<IOptimizationRule>>();

        public void RegisterDefaults()
        {
            Register(() => new Rule_ReceiveShadowsOn());
            Register(() => new Rule_CastShadowsOn());
            Register(() => new Rule_StaticBatchingNotEnabled());
            Register(() => new Rule_LightsNotBaked());
            Register(() => new Rule_LightsWithShadowsEnabled());
            Register(() => new Rule_MaterialsReflectionEnvironmentEnabled());
            Register(() => new Rule_TransparentToOpaque());

            Register(() => new Rule_RenderersWithSelectedShader_Replace());
            Register(() => new Rule_RenderersWithShaderGraphShaders_Replace());
            Register(() => new Rule_MaterialsAlphaClippingEnabled());
            Register(() => new Rule_StaticShadowCasterEnabled());
            Register(() => new Rule_ParticlesWithSelectedShader_Replace());
            Register(() => new Rule_MaterialsReceiveShadowsOn());
        }

        public void Register(Func<IOptimizationRule> factory)
        {
            var rule = factory();
            _factories[rule.Type] = factory;
        }

        public IOptimizationRule TryCreate(OptimizationRuleType type)
        {
            return _factories.TryGetValue(type, out var f) ? f() : null;
        }
    }

    #endregion

    #region Rules - Renderers / Materials (existing)

    internal sealed class Rule_ReceiveShadowsOn : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.RendererMaterials_ReceiveShadowsOn;
        public string DisplayName => "Renderers: Receive Shadows = ON";
        public string Description => "Găsește toate Renderer-urile care au receiveShadows activ și îți permite să le dezactivezi.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Set Receive Shadows (ON/OFF)", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("Dacă e bifat => Receive Shadows ON. Dacă e debifat => OFF.", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;
            foreach (var r in SceneQuery.FindAllRenderers())
            {
                if (r == null) continue;
                if (!r.receiveShadows) continue;
                yield return FindingItem.FromRenderer(id++, r, "receiveShadows = ON");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            foreach (var it in selected)
            {
                var r = it.Renderer;
                if (r == null) continue;

                Undo.RecordObject(r, "Set Receive Shadows");
                r.receiveShadows = context.Settings.SetBooleanValue;
                EditorUtility.SetDirty(r);
            }
        }
    }

    internal sealed class Rule_CastShadowsOn : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Renderer_CastShadowsOn;
        public string DisplayName => "Renderers: Cast Shadows enabled";
        public string Description => "Găsește Renderer-urile cu Shadow Casting Mode != Off și îți permite să le setezi Off/On.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Set Cast Shadows = ON (true) / OFF (false)", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("true => ShadowCastingMode.On, false => ShadowCastingMode.Off", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;
            foreach (var r in SceneQuery.FindAllRenderers())
            {
                if (r == null) continue;
                if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                yield return FindingItem.FromRenderer(id++, r, $"cast = {r.shadowCastingMode}");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            var target = context.Settings.SetBooleanValue ? ShadowCastingMode.On : ShadowCastingMode.Off;

            foreach (var it in selected)
            {
                var r = it.Renderer;
                if (r == null) continue;

                Undo.RecordObject(r, "Set Cast Shadows");
                r.shadowCastingMode = target;
                EditorUtility.SetDirty(r);
            }
        }
    }

    internal sealed class Rule_StaticBatchingNotEnabled : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.StaticBatching_NotEnabled;
        public string DisplayName => "GameObjects: BatchingStatic flag OFF";
        public string Description => "Găsește GameObject-urile fără StaticEditorFlags.BatchingStatic și îți permite să îl setezi ON/OFF.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Enable Batching Static", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("Atenție: Static Batching are impact pe memorie.", MessageType.Warning);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;
            foreach (var go in SceneQuery.FindAllGameObjects())
            {
                if (go == null) continue;

                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                var has = (flags & StaticEditorFlags.BatchingStatic) != 0;
                if (has) continue;

                yield return FindingItem.FromGameObject(id++, go, "BatchingStatic = OFF");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            foreach (var it in selected)
            {
                var go = it.Object as GameObject;
                if (go == null) continue;

                Undo.RecordObject(go, "Set Batching Static");

                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if (context.Settings.SetBooleanValue) flags |= StaticEditorFlags.BatchingStatic;
                else flags &= ~StaticEditorFlags.BatchingStatic;

                GameObjectUtility.SetStaticEditorFlags(go, flags);
                EditorUtility.SetDirty(go);
            }
        }
    }

    internal sealed class Rule_MaterialsReflectionEnvironmentEnabled : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Materials_ReflectionEnvironmentEnabled;
        public string DisplayName => "Materials: Reflection Environment enabled (heuristic)";
        public string Description => "Găsește materiale cu reflecții de mediu active (heuristic) și îți permite să le setezi OFF/ON.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Enable Reflection Environment", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("Heuristic: _EnvironmentReflections / _GlossyReflections / keyword-uri.", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;

            foreach (var pair in SceneQuery.FindRendererMaterialPairs())
            {
                var r = pair.Renderer;
                var mat = pair.Material;
                if (r == null || mat == null) continue;

                if (!MaterialHeuristics.TryGetReflectionEnvironment(mat, out var current)) continue;
                if (!current) continue;

                yield return FindingItem.FromMaterial(id++, mat, r, pair.MaterialIndex, "ReflectionEnv = ON");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            foreach (var it in selected)
            {
                var mat = it.Material;
                if (mat == null) continue;

                Undo.RecordObject(mat, "Set Reflection Environment");
                MaterialHeuristics.SetReflectionEnvironment(mat, context.Settings.SetBooleanValue);
                EditorUtility.SetDirty(mat);
            }
        }
    }

    internal sealed class Rule_TransparentToOpaque : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Materials_TransparentToOpaque;
        public string DisplayName => "Materials: Transparent -> Opaque";
        public string Description => "Găsește materiale transparente (heuristic) și îți permite să le forțezi Opaque.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.ForceOpaque = EditorGUILayout.ToggleLeft("Force Opaque", settings.ForceOpaque);
            EditorGUILayout.HelpBox("Pentru URP Lit: _Surface=0 + renderQueue reset.", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;

            foreach (var pair in SceneQuery.FindRendererMaterialPairs())
            {
                var r = pair.Renderer;
                var mat = pair.Material;
                if (r == null || mat == null) continue;

                if (!MaterialHeuristics.IsTransparent(mat)) continue;

                yield return FindingItem.FromMaterial(id++, mat, r, pair.MaterialIndex, $"Transparent (rq={mat.renderQueue})");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            if (!context.Settings.ForceOpaque) return;

            foreach (var it in selected)
            {
                var mat = it.Material;
                if (mat == null) continue;

                Undo.RecordObject(mat, "Force Opaque");
                MaterialHeuristics.ForceOpaque(mat);
                EditorUtility.SetDirty(mat);
            }
        }
    }

    internal sealed class Rule_MaterialsAlphaClippingEnabled : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Materials_AlphaClippingEnabled;
        public string DisplayName => "Materials: Alpha Clipping enabled";
        public string Description => "Găsește materiale cu Alpha Clipping ON și îți permite să îl setezi ON/OFF.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Enable Alpha Clipping", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("URP Lit: _AlphaClip + keyword _ALPHATEST_ON.", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;

            foreach (var pair in SceneQuery.FindRendererMaterialPairs())
            {
                var r = pair.Renderer;
                var mat = pair.Material;
                if (r == null || mat == null) continue;

                if (!MaterialHeuristics.IsAlphaClippingEnabled(mat)) continue;

                yield return FindingItem.FromMaterial(id++, mat, r, pair.MaterialIndex, "AlphaClip = ON");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            foreach (var it in selected)
            {
                var mat = it.Material;
                if (mat == null) continue;

                Undo.RecordObject(mat, "Set Alpha Clipping");
                MaterialHeuristics.SetAlphaClipping(mat, context.Settings.SetBooleanValue);
                EditorUtility.SetDirty(mat);
            }
        }
    }

    internal sealed class Rule_MaterialsReceiveShadowsOn : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Materials_ReceiveShadowsOn;
        public string DisplayName => "Materials: Receive Shadows = ON";
        public string Description => "Găsește toate materialele care au Receive Shadows activ și îți permite să le dezactivezi.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Set Receive Shadows (ON/OFF)", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("Dacă e bifat => Receive Shadows ON pe materiale. Dacă e debifat => OFF.", MessageType.Info);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;

            foreach (var pair in SceneQuery.FindRendererMaterialPairs())
            {
                var r = pair.Renderer;
                var mat = pair.Material;
                if (r == null || mat == null) continue;

                if (!MaterialHeuristics.IsReceiveShadowsEnabled(mat)) continue;

                yield return FindingItem.FromMaterial(id++, mat, r, pair.MaterialIndex, "Receive Shadows = ON");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            foreach (var it in selected)
            {
                var mat = it.Material;
                if (mat == null) continue;

                Undo.RecordObject(mat, "Set Material Receive Shadows");
                MaterialHeuristics.SetReceiveShadows(mat, context.Settings.SetBooleanValue);
                EditorUtility.SetDirty(mat);
            }
        }
    }

    #endregion

    #region Rules - Lights (existing)

    internal sealed class Rule_LightsNotBaked : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Lights_NotBaked;
        public string DisplayName => "Lights: Not Baked";
        public string Description => "Găsește lumini care NU sunt Baked/Mixed și îți permite să le setezi Baked (true) / Realtime (false).";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Set Light Bake Type = Baked (true) / Realtime (false)", settings.SetBooleanValue);
            EditorGUILayout.HelpBox("Schimbarea poate necesita rebake.", MessageType.Warning);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;
            foreach (var l in SceneQuery.FindAllLights())
            {
                if (l == null) continue;

#if UNITY_2020_1_OR_NEWER
                var bt = l.lightmapBakeType;
                if (bt == LightmapBakeType.Baked || bt == LightmapBakeType.Mixed) continue;
                yield return FindingItem.FromLight(id++, l, $"bakeType = {bt}");
#else
                yield return FindingItem.FromLight(id++, l, "bakeType check not supported on this Unity version");
#endif
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
#if UNITY_2020_1_OR_NEWER
            var target = context.Settings.SetBooleanValue ? LightmapBakeType.Baked : LightmapBakeType.Realtime;

            foreach (var it in selected)
            {
                var l = it.Light;
                if (l == null) continue;

                Undo.RecordObject(l, "Set Light Bake Type");
                l.lightmapBakeType = target;
                EditorUtility.SetDirty(l);
            }
#endif
        }
    }

    internal sealed class Rule_LightsWithShadowsEnabled : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Lights_WithShadowsEnabled;
        public string DisplayName => "Lights: Shadows enabled";
        public string Description => "Găsește lumini cu shadows != None și îți permite să setezi Soft (true) / None (false).";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Set Light Shadows = Soft (true) / None (false)", settings.SetBooleanValue);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;
            foreach (var l in SceneQuery.FindAllLights())
            {
                if (l == null) continue;
                if (l.shadows == LightShadows.None) continue;

                yield return FindingItem.FromLight(id++, l, $"shadows = {l.shadows}");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            var target = context.Settings.SetBooleanValue ? LightShadows.Soft : LightShadows.None;

            foreach (var it in selected)
            {
                var l = it.Light;
                if (l == null) continue;

                Undo.RecordObject(l, "Set Light Shadows");
                l.shadows = target;
                EditorUtility.SetDirty(l);
            }
        }
    }

    #endregion

    #region Rules - Static Shadow Caster (existing)

    internal sealed class Rule_StaticShadowCasterEnabled : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.StaticShadowCaster_Enabled;
        public string DisplayName => "StaticShadowCaster: Enabled";
        public string Description => "Găsește componentele StaticShadowCaster active și îți permite să le activezi/dezactivezi.";

        private static System.Type _staticShadowCasterType;

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            // Explicit null check assignment for compatibility
            if (_staticShadowCasterType == null) _staticShadowCasterType = System.Type.GetType("UnityEngine.Rendering.StaticShadowCaster, Unity.RenderPipelines.Core.Runtime");

            EditorGUILayout.LabelField("Optimize action", EditorStyles.boldLabel);
            settings.SetBooleanValue = EditorGUILayout.ToggleLeft("Enable StaticShadowCaster component", settings.SetBooleanValue);

            if (_staticShadowCasterType == null)
                EditorGUILayout.HelpBox("StaticShadowCaster type not found (pipeline/package missing?).", MessageType.Warning);
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            // Ensure type is resolved
            if (_staticShadowCasterType == null) _staticShadowCasterType = System.Type.GetType("UnityEngine.Rendering.StaticShadowCaster, Unity.RenderPipelines.Core.Runtime");
            if (_staticShadowCasterType == null) yield break;

            var id = 1;
            foreach (var go in SceneQuery.FindAllGameObjects())
            {
                if (go == null) continue;

                var comp = go.GetComponent(_staticShadowCasterType);
                if (comp == null) continue;

                var mb = comp as Behaviour;
                var enabled = mb != null ? mb.enabled : true;
                if (!enabled) continue;

                yield return FindingItem.FromGameObject(id++, go, "StaticShadowCaster enabled");
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            if (_staticShadowCasterType == null) _staticShadowCasterType = System.Type.GetType("UnityEngine.Rendering.StaticShadowCaster, Unity.RenderPipelines.Core.Runtime");
            if (_staticShadowCasterType == null) return;

            foreach (var it in selected)
            {
                var go = it.Object as GameObject;
                if (go == null) continue;

                var comp = go.GetComponent(_staticShadowCasterType);
                if (comp == null) continue;

                var mb = comp as Behaviour;
                if (mb == null) continue;

                Undo.RecordObject(mb, "Set StaticShadowCaster enabled");
                mb.enabled = context.Settings.SetBooleanValue;
                EditorUtility.SetDirty(mb);
            }
        }
    }

    #endregion

    #region NEW: Replace Rules (audit -> action)

    internal sealed class Rule_RenderersWithSelectedShader_Replace : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Renderers_WithSelectedShader;
        public string DisplayName => "Renderers: Using selected Shader (Replace)";
        public string Description =>
            "Găsește Renderer-urile care folosesc un shader ales. Poți face Replace fie la Shader (pe materialele existente), fie la referința de Material.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            if (settings.TargetShader == null)
                settings.TargetShader = Shader.Find("Universal Render Pipeline/Lit");

            EditorGUILayout.LabelField("Scan filter", EditorStyles.boldLabel);
            settings.TargetShader = (Shader)EditorGUILayout.ObjectField("Target Shader", settings.TargetShader, typeof(Shader), false);

            EditorGUILayout.Space(8);
            DrawReplaceBlock(ref settings, defaultReplaceShaderName: "Universal Render Pipeline/Lit");
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var targetShader = context.Settings.TargetShader;
            if (targetShader == null) yield break;

            var id = 1;
            foreach (var r in SceneQuery.FindAllRenderers())
            {
                if (r == null) continue;

                var mats = r.sharedMaterials;
                if (mats == null) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader != targetShader) continue;

                    yield return FindingItem.FromMaterial(id++, m, r, i, $"material[{i}] uses {targetShader.name}");
                }
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            ReplaceApplier.ApplyReplaceToSelected(context.Settings, selected);
        }

        private static void DrawReplaceBlock(ref RuleSettings settings, string defaultReplaceShaderName)
        {
            EditorGUILayout.LabelField("Replace", EditorStyles.boldLabel);

            settings.ReplaceMode = (ReplaceMode)EditorGUILayout.EnumPopup("Mode", settings.ReplaceMode);
            settings.AffectAllMaterialsOnRenderer = EditorGUILayout.ToggleLeft("Affect ALL materials on the renderer", settings.AffectAllMaterialsOnRenderer);

            if (settings.ReplaceMode == ReplaceMode.ReplaceShaderOnly)
            {
                if (settings.ReplaceShader == null)
                    settings.ReplaceShader = Shader.Find(defaultReplaceShaderName);

                settings.ReplaceShader = (Shader)EditorGUILayout.ObjectField("Replace Shader", settings.ReplaceShader, typeof(Shader), false);
                EditorGUILayout.HelpBox("Schimbă doar shader-ul materialului (materialul rămâne același asset).", MessageType.Info);
            }
            else
            {
                settings.ReplaceMaterial = (Material)EditorGUILayout.ObjectField("Replace Material", settings.ReplaceMaterial, typeof(Material), false);
                EditorGUILayout.HelpBox("Înlocuiește referința de material pe Renderer (sharedMaterials).", MessageType.Warning);
            }
        }
    }

    internal sealed class Rule_RenderersWithShaderGraphShaders_Replace : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Renderers_WithShaderGraphShaders;
        public string DisplayName => "Renderers: Using Shader Graph shaders (Replace)";
        public string Description =>
            "Heuristic: shader.name conține 'Shader Graph'. Poți face Replace fie la Shader (pe materialele existente), fie la referința de Material.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            EditorGUILayout.HelpBox("Detectarea Shader Graph este heuristică (după nume).", MessageType.Info);
            EditorGUILayout.Space(6);

            DrawReplaceBlock(ref settings, defaultReplaceShaderName: "Universal Render Pipeline/Lit");
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var id = 1;

            foreach (var r in SceneQuery.FindAllRenderers())
            {
                if (r == null) continue;

                var mats = r.sharedMaterials;
                if (mats == null) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;

                    var n = m.shader.name ?? "";
                    if (!MaterialHeuristics.IsShaderGraphName(n)) continue;

                    yield return FindingItem.FromMaterial(id++, m, r, i, $"material[{i}] shaderGraph = {n}");
                }
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            ReplaceApplier.ApplyReplaceToSelected(context.Settings, selected);
        }

        private static void DrawReplaceBlock(ref RuleSettings settings, string defaultReplaceShaderName)
        {
            EditorGUILayout.LabelField("Replace", EditorStyles.boldLabel);

            settings.ReplaceMode = (ReplaceMode)EditorGUILayout.EnumPopup("Mode", settings.ReplaceMode);
            settings.AffectAllMaterialsOnRenderer = EditorGUILayout.ToggleLeft("Affect ALL materials on the renderer", settings.AffectAllMaterialsOnRenderer);

            if (settings.ReplaceMode == ReplaceMode.ReplaceShaderOnly)
            {
                if (settings.ReplaceShader == null)
                    settings.ReplaceShader = Shader.Find(defaultReplaceShaderName);

                settings.ReplaceShader = (Shader)EditorGUILayout.ObjectField("Replace Shader", settings.ReplaceShader, typeof(Shader), false);
                EditorGUILayout.HelpBox("Schimbă doar shader-ul materialului (materialul rămâne același asset).", MessageType.Info);
            }
            else
            {
                settings.ReplaceMaterial = (Material)EditorGUILayout.ObjectField("Replace Material", settings.ReplaceMaterial, typeof(Material), false);
                EditorGUILayout.HelpBox("Înlocuiește referința de material pe Renderer (sharedMaterials).", MessageType.Warning);
            }
        }
    }

    internal sealed class Rule_ParticlesWithSelectedShader_Replace : IOptimizationRule
    {
        public OptimizationRuleType Type => OptimizationRuleType.Particles_WithSelectedShader;
        public string DisplayName => "Particles: Using selected shader (Replace)";
        public string Description =>
            "Găsește ParticleSystem-urile care au ParticleSystemRenderer cu material shader ales. Poți face Replace la Shader sau la Material (pe renderer). Include filtru după număr particule.";

        public void DrawSettingsGUI(ref RuleSettings settings)
        {
            if (settings.TargetParticleShader == null)
            {
                settings.TargetParticleShader = Shader.Find("Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            }

            EditorGUILayout.LabelField("Scan filter", EditorStyles.boldLabel);
            settings.TargetParticleShader = (Shader)EditorGUILayout.ObjectField("Target Particle Shader", settings.TargetParticleShader, typeof(Shader), false);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Particle Count Filter", EditorStyles.boldLabel);

            settings.EnableParticleCountThreshold = EditorGUILayout.ToggleLeft("Enable particle count threshold", settings.EnableParticleCountThreshold);
            using (new EditorGUI.DisabledScope(!settings.EnableParticleCountThreshold))
            {
                settings.ParticleCountThreshold = EditorGUILayout.IntField("Threshold", Mathf.Max(0, settings.ParticleCountThreshold));
                settings.UseLiveParticleCountInPlayMode = EditorGUILayout.ToggleLeft("Use live ParticleSystem.particleCount in Play Mode", settings.UseLiveParticleCountInPlayMode);
            }

            EditorGUILayout.Space(10);
            DrawReplaceBlock(ref settings, defaultReplaceShaderName: "Legacy Shaders/Particles/Additive");
        }

        public IEnumerable<FindingItem> Scan(RuleContext context)
        {
            var shader = context.Settings.TargetParticleShader;
            if (shader == null) yield break;

            var id = 1;

            foreach (var ps in SceneQuery.FindAllParticleSystems())
            {
                if (ps == null) continue;

                var pr = ps.GetComponent<ParticleSystemRenderer>();
                if (pr == null) continue;

                var mat = pr.sharedMaterial;
                if (mat == null || mat.shader != shader) continue;

                if (context.Settings.EnableParticleCountThreshold)
                {
                    var count = GetParticleCountForFilter(ps, context.Settings);
                    if (count < context.Settings.ParticleCountThreshold) continue;

                    // particle uses renderer too; store as "Material finding" for replace
                    yield return FindingItem.FromMaterial(id++, mat, pr, 0, $"shader={shader.name}, count={count}");
                }
                else
                {
                    yield return FindingItem.FromMaterial(id++, mat, pr, 0, $"shader={shader.name}");
                }
            }
        }

        public void Apply(RuleContext context, IReadOnlyList<FindingItem> selected)
        {
            ReplaceApplier.ApplyReplaceToSelected(context.Settings, selected);
        }

        private static int GetParticleCountForFilter(ParticleSystem ps, RuleSettings s)
        {
            if (Application.isPlaying && s.UseLiveParticleCountInPlayMode)
                return ps.particleCount;

            return ps.main.maxParticles;
        }

        private static void DrawReplaceBlock(ref RuleSettings settings, string defaultReplaceShaderName)
        {
            EditorGUILayout.LabelField("Replace", EditorStyles.boldLabel);

            settings.ReplaceMode = (ReplaceMode)EditorGUILayout.EnumPopup("Mode", settings.ReplaceMode);
            settings.AffectAllMaterialsOnRenderer = EditorGUILayout.ToggleLeft("Affect ALL materials on the renderer", settings.AffectAllMaterialsOnRenderer);

            if (settings.ReplaceMode == ReplaceMode.ReplaceShaderOnly)
            {
                if (settings.ReplaceShader == null)
                    settings.ReplaceShader = Shader.Find(defaultReplaceShaderName);

                settings.ReplaceShader = (Shader)EditorGUILayout.ObjectField("Replace Shader", settings.ReplaceShader, typeof(Shader), false);
                EditorGUILayout.HelpBox("Schimbă doar shader-ul materialului (materialul rămâne același asset).", MessageType.Info);
            }
            else
            {
                settings.ReplaceMaterial = (Material)EditorGUILayout.ObjectField("Replace Material", settings.ReplaceMaterial, typeof(Material), false);
                EditorGUILayout.HelpBox("Înlocuiește materialul pe ParticleSystemRenderer (sharedMaterials).", MessageType.Warning);
            }
        }
    }

    internal static class ReplaceApplier
    {
        #region Public Methods

        public static void ApplyReplaceToSelected(RuleSettings settings, IReadOnlyList<FindingItem> selected)
        {
            if (selected == null || selected.Count == 0) return;

            if (settings.ReplaceMode == ReplaceMode.ReplaceShaderOnly)
            {
                if (settings.ReplaceShader == null) return;

                foreach (var it in selected)
                    ApplyReplaceShader(settings, it, settings.ReplaceShader);
            }
            else
            {
                if (settings.ReplaceMaterial == null) return;

                foreach (var it in selected)
                    ApplyReplaceMaterial(settings, it, settings.ReplaceMaterial);
            }
        }

        #endregion

        #region Private Methods

        private static void ApplyReplaceShader(RuleSettings settings, FindingItem it, Shader newShader)
        {
            if (it == null) return;

            // Case: material asset reference exists
            var mat = it.Material != null ? it.Material : (it.Object as Material);
            if (mat == null) return;

            if (settings.AffectAllMaterialsOnRenderer && it.Renderer != null)
            {
                var r = it.Renderer;
                var mats = r.sharedMaterials;
                if (mats == null) return;

                // Clone array to avoid accidental external mutations
                mats = (Material[])mats.Clone();

                // IMPORTANT: we only change materials that match the scanned material's shader to avoid collateral damage
                var scannedShader = mat.shader;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.shader != scannedShader) continue;

                    Undo.RecordObject(m, "Replace Shader");
                    m.shader = newShader;
                    EditorUtility.SetDirty(m);
                }

                return;
            }

            Undo.RecordObject(mat, "Replace Shader");
            mat.shader = newShader;
            EditorUtility.SetDirty(mat);
        }

        private static void ApplyReplaceMaterial(RuleSettings settings, FindingItem it, Material newMaterial)
        {
            if (it == null) return;

            var r = it.Renderer;
            if (r == null) return;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return;

            // Clone array to avoid accidental external mutations
            mats = (Material[])mats.Clone();

            Undo.RecordObject(r, "Replace Material");

            if (settings.AffectAllMaterialsOnRenderer)
            {
                // Replace only slots that match the scanned material (sa fie safe)
                var scannedMat = it.Material != null ? it.Material : (it.Object as Material);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (scannedMat == null || mats[i] == scannedMat)
                        mats[i] = newMaterial;
                }
            }
            else
            {
                var idx = it.MaterialIndex;
                if (idx < 0 || idx >= mats.Length)
                {
                    // fallback: replace first match
                    var scannedMat = it.Material != null ? it.Material : (it.Object as Material);
                    var replaced = false;

                    if (scannedMat != null)
                    {
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == scannedMat)
                            {
                                mats[i] = newMaterial;
                                replaced = true;
                                break;
                            }
                        }
                    }

                    if (!replaced)
                        mats[0] = newMaterial;
                }
                else
                {
                    mats[idx] = newMaterial;
                }
            }

            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
        }

        #endregion
    }

    #endregion

    #region Utils (updated SceneQuery pairs)

    internal static class SceneQuery
    {
        public readonly struct RendererMaterialPair
        {
            public readonly Renderer Renderer;
            public readonly Material Material;
            public readonly int MaterialIndex;

            public RendererMaterialPair(Renderer r, Material m, int materialIndex)
            {
                Renderer = r;
                Material = m;
                MaterialIndex = materialIndex;
            }
        }

        public static IEnumerable<GameObject> FindAllGameObjects()
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go == null) continue;
                if (!IsInValidScene(go)) continue;
                yield return go;
            }
        }

        public static IEnumerable<Renderer> FindAllRenderers()
        {
            var all = Resources.FindObjectsOfTypeAll<Renderer>();
            foreach (var r in all)
            {
                if (r == null) continue;
                if (!IsInValidScene(r.gameObject)) continue;
                yield return r;
            }
        }

        public static IEnumerable<Light> FindAllLights()
        {
            var all = Resources.FindObjectsOfTypeAll<Light>();
            foreach (var l in all)
            {
                if (l == null) continue;
                if (!IsInValidScene(l.gameObject)) continue;
                yield return l;
            }
        }

        public static IEnumerable<ParticleSystem> FindAllParticleSystems()
        {
            var all = Resources.FindObjectsOfTypeAll<ParticleSystem>();
            foreach (var ps in all)
            {
                if (ps == null) continue;
                if (!IsInValidScene(ps.gameObject)) continue;
                yield return ps;
            }
        }

        public static IEnumerable<RendererMaterialPair> FindRendererMaterialPairs()
        {
            foreach (var r in FindAllRenderers())
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    yield return new RendererMaterialPair(r, m, i);
                }
            }
        }

        private static bool IsInValidScene(GameObject go)
        {
            if (!go.scene.IsValid()) return false;
            if (!go.scene.isLoaded) return false;
            return true;
        }
    }

    internal static class HierarchyPath
    {
        public static string Of(GameObject go)
        {
            if (go == null) return "";
            var t = go.transform;
            var stack = new Stack<string>(16);
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }
    }

    internal static class MaterialHeuristics
    {
        private static readonly int P_EnvironmentReflections = Shader.PropertyToID("_EnvironmentReflections");
        private static readonly int P_GlossyReflections = Shader.PropertyToID("_GlossyReflections");

        private static readonly int P_Surface = Shader.PropertyToID("_Surface");
        private static readonly int P_AlphaClip = Shader.PropertyToID("_AlphaClip");

        public static bool TryGetReflectionEnvironment(Material mat, out bool enabled)
        {
            enabled = false;
            if (mat == null) return false;

            if (mat.HasProperty(P_EnvironmentReflections))
            {
                enabled = mat.GetFloat(P_EnvironmentReflections) > 0.5f;
                return true;
            }

            if (mat.HasProperty(P_GlossyReflections))
            {
                enabled = mat.GetFloat(P_GlossyReflections) > 0.5f;
                return true;
            }

            var k = mat.shaderKeywords;
            if (k != null && k.Any(s => s.IndexOf("REFLECTION", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                enabled = true;
                return true;
            }

            return false;
        }

        public static void SetReflectionEnvironment(Material mat, bool enabled)
        {
            if (mat == null) return;

            if (mat.HasProperty(P_EnvironmentReflections))
                mat.SetFloat(P_EnvironmentReflections, enabled ? 1f : 0f);

            if (mat.HasProperty(P_GlossyReflections))
                mat.SetFloat(P_GlossyReflections, enabled ? 1f : 0f);
        }

        public static bool IsTransparent(Material mat)
        {
            if (mat == null) return false;

            if (mat.HasProperty(P_Surface))
                return mat.GetFloat(P_Surface) > 0.5f;

            return mat.renderQueue >= (int)RenderQueue.Transparent;
        }

        public static void ForceOpaque(Material mat)
        {
            if (mat == null) return;

            if (mat.HasProperty(P_Surface))
                mat.SetFloat(P_Surface, 0f);

            if (mat.renderQueue >= (int)RenderQueue.Transparent)
                mat.renderQueue = -1;

            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_SURFACE_TYPE_OPAQUE");
        }

        public static bool IsShaderGraphName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            return shaderName.IndexOf("Shader Graph", StringComparison.OrdinalIgnoreCase) >= 0
                   || shaderName.IndexOf("ShaderGraph", StringComparison.OrdinalIgnoreCase) >= 0
                   || shaderName.IndexOf("Shader Graphs", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsAlphaClippingEnabled(Material mat)
        {
            if (mat == null) return false;

            if (mat.HasProperty(P_AlphaClip))
                return mat.GetFloat(P_AlphaClip) > 0.5f;

            return mat.IsKeywordEnabled("_ALPHATEST_ON");
        }

        public static void SetAlphaClipping(Material mat, bool enabled)
        {
            if (mat == null) return;

            if (mat.HasProperty(P_AlphaClip))
                mat.SetFloat(P_AlphaClip, enabled ? 1f : 0f);

            if (enabled) mat.EnableKeyword("_ALPHATEST_ON");
            else mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static readonly int P_ReceiveShadows = Shader.PropertyToID("_ReceiveShadows");

        public static bool IsReceiveShadowsEnabled(Material mat)
        {
            if (mat == null) return false;

            // Check for _ReceiveShadows property (common in URP)
            if (mat.HasProperty(P_ReceiveShadows))
                return mat.GetFloat(P_ReceiveShadows) > 0.5f;

            // Check for _RECEIVE_SHADOWS_OFF keyword (if disabled)
            if (mat.IsKeywordEnabled("_RECEIVE_SHADOWS_OFF"))
                return false;

            // Default to true if property/keyword not found
            return true;
        }

        public static void SetReceiveShadows(Material mat, bool enabled)
        {
            if (mat == null) return;

            // Set _ReceiveShadows property if available
            if (mat.HasProperty(P_ReceiveShadows))
                mat.SetFloat(P_ReceiveShadows, enabled ? 1f : 0f);

            // Manage keyword
            if (enabled)
                mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            else
                mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        }
    }

    #endregion
}
#endif
*/
