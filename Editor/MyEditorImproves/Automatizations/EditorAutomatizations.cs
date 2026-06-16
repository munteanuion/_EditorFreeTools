#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Automatizations
{
    /// <summary>
    /// Colectie de mici automatizari de editor, expuse sub
    /// Tools/Automatizations/. Tot fisierul exista doar in editor.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorAutomatizations
    {
        private const string MenuRoot = "Tools/Automatizations/";

        private const string ClearOnPlayPref = "EditorAutomatizations.ClearSelectionOnPlay";

        private const string AutoplayOnSelectPref = "EditorAutomatizations.AutoplayOnSelect";

        // Fereastra de retry (secunde) cat incercam sa pornim preview-ul dupa
        // o selectie noua, pentru ca AvatarPreview e creat lazy (la primul draw).
        private const double AutoplayRetryWindow = 3.0;

        private static double _autoplayDeadline;
        private static bool _autoplayTicking;

        // ---------------------------------------------------------------------
        // Constructor static: hook-uri (ruleaza la load).
        // ---------------------------------------------------------------------
        static EditorAutomatizations()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        // =====================================================================
        // 1) Autoplay la animatia din preview
        // =====================================================================

        // ---- Toggle: autoplay automat la selectare -------------------------

        [MenuItem(MenuRoot + "Autoplay Preview On Select", false, 3)]
        private static void ToggleAutoplayOnSelect()
        {
            bool value = !EditorPrefs.GetBool(AutoplayOnSelectPref, true);
            EditorPrefs.SetBool(AutoplayOnSelectPref, value);
            if (value) BeginAutoplayRetry(); // aplica imediat pe selectia curenta
        }

        [MenuItem(MenuRoot + "Autoplay Preview On Select", true)]
        private static bool ToggleAutoplayOnSelectValidate()
        {
            Menu.SetChecked(MenuRoot + "Autoplay Preview On Select",
                EditorPrefs.GetBool(AutoplayOnSelectPref, true));
            return true;
        }

        private static void OnSelectionChanged()
        {
            if (!EditorPrefs.GetBool(AutoplayOnSelectPref, true)) return;
            BeginAutoplayRetry();
        }

        /// <summary>
        /// Porneste fereastra de retry: AvatarPreview se creeaza abia cand
        /// preview-ul e desenat o data, deci incercam repetat cateva frame-uri.
        /// </summary>
        private static void BeginAutoplayRetry()
        {
            _autoplayDeadline = EditorApplication.timeSinceStartup + AutoplayRetryWindow;

            if (_autoplayTicking) return;
            _autoplayTicking = true;
            EditorApplication.update += AutoplayRetryTick;
        }

        private static void AutoplayRetryTick()
        {
            bool expired = EditorApplication.timeSinceStartup > _autoplayDeadline;
            bool disabled = !EditorPrefs.GetBool(AutoplayOnSelectPref, true);

            int started = disabled ? 0 : SetAllPreviewsPlaying(true);
            if (started > 0) RepaintAllInspectors();

            if (started > 0 || expired || disabled)
            {
                _autoplayTicking = false;
                EditorApplication.update -= AutoplayRetryTick;
            }
        }

        /// <summary>
        /// Cauta in toti editorii activi orice AvatarPreview si ii forteaza
        /// starea de play via timeControl. Returneaza cate a gasit.
        /// </summary>
        private static int SetAllPreviewsPlaying(bool playing)
        {
            int count = 0;

            Type avatarPreviewType = FindType("UnityEditor.AvatarPreview");
            if (avatarPreviewType == null) return 0;

            foreach (UnityEditor.Editor editor in GetActiveEditors())
            {
                if (editor == null) continue;

                foreach (object preview in FindFieldValuesOfType(editor, avatarPreviewType))
                {
                    if (ApplyPlaying(preview, playing)) count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Seteaza playing/loop pe un obiect AvatarPreview prin field-ul timeControl.
        /// </summary>
        private static bool ApplyPlaying(object avatarPreview, bool playing)
        {
            if (avatarPreview == null) return false;

            bool any = false;

            object timeControl = GetMemberValue(avatarPreview, "timeControl")
                                 ?? GetMemberValue(avatarPreview, "m_TimeControl");
            if (timeControl != null)
            {
                any |= SetMemberValue(timeControl, "playing", playing);
                if (playing) SetMemberValue(timeControl, "loop", true);
            }

            // Unele versiuni tin si un flag propriu pe AvatarPreview.
            any |= SetMemberValue(avatarPreview, "playing", playing);

            return any;
        }

        // =====================================================================
        // 2) Golire selectie la intrarea in Play Mode (daca Inspector nelocked)
        // =====================================================================

        [MenuItem(MenuRoot + "Clear Selection On Play Enter", false, 20)]
        private static void ToggleClearOnPlay()
        {
            bool value = !EditorPrefs.GetBool(ClearOnPlayPref, true);
            EditorPrefs.SetBool(ClearOnPlayPref, value);
        }

        [MenuItem(MenuRoot + "Clear Selection On Play Enter", true)]
        private static bool ToggleClearOnPlayValidate()
        {
            Menu.SetChecked(MenuRoot + "Clear Selection On Play Enter",
                EditorPrefs.GetBool(ClearOnPlayPref, true));
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.GetBool(ClearOnPlayPref, true)) return;

            // Daca exista vreun inspector locked, nu atingem selectia
            // (ar goli si inspectorul lui, anuland scopul lock-ului).
            if (AnyInspectorLocked()) return;

            Selection.activeObject = null;
            Selection.objects = Array.Empty<UnityEngine.Object>();
        }

        /// <summary>
        /// True daca exista cel putin o fereastra de Inspector cu lock activ.
        /// </summary>
        private static bool AnyInspectorLocked()
        {
            Type inspectorType = FindType("UnityEditor.InspectorWindow");
            if (inspectorType == null) return false;

            UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(inspectorType);
            foreach (UnityEngine.Object win in windows)
            {
                object locked = GetMemberValue(win, "isLocked");
                if (locked is bool b && b) return true;
            }

            return false;
        }

        // =====================================================================
        // Helpers de reflectie
        // =====================================================================

        private static IEnumerable<UnityEditor.Editor> GetActiveEditors()
        {
            // Toate instantele de Editor incarcate (include si editorii nested,
            // ex. AnimationClipEditor dintr-un ModelImporter, care NU apar in
            // ActiveEditorTracker.sharedTracker.activeEditors).
            var seen = new HashSet<int>();

            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor));
            foreach (UnityEngine.Object o in all)
            {
                if (o is UnityEditor.Editor e && seen.Add(e.GetInstanceID()))
                    yield return e;
            }

            ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
            if (tracker?.activeEditors != null)
            {
                foreach (UnityEditor.Editor e in tracker.activeEditors)
                {
                    if (e != null && seen.Add(e.GetInstanceID()))
                        yield return e;
                }
            }
        }

        private static IEnumerable<object> FindFieldValuesOfType(object instance, Type wanted)
        {
            if (instance == null) yield break;

            Type t = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (t != null && t != typeof(object))
            {
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (!wanted.IsAssignableFrom(f.FieldType)) continue;

                    object value = null;
                    try { value = f.GetValue(instance); }
                    catch { /* ignoram field-uri inaccesibile */ }

                    if (value != null) yield return value;
                }

                t = t.BaseType;
            }
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null) return null;
            Type t = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (t != null && t != typeof(object))
            {
                PropertyInfo p = t.GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    try { return p.GetValue(instance); } catch { }
                }

                FieldInfo f = t.GetField(name, flags);
                if (f != null)
                {
                    try { return f.GetValue(instance); } catch { }
                }

                t = t.BaseType;
            }

            return null;
        }

        private static bool SetMemberValue(object instance, string name, object value)
        {
            if (instance == null) return false;
            Type t = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (t != null && t != typeof(object))
            {
                PropertyInfo p = t.GetProperty(name, flags);
                if (p != null && p.CanWrite)
                {
                    try { p.SetValue(instance, value); return true; } catch { }
                }

                FieldInfo f = t.GetField(name, flags);
                if (f != null)
                {
                    try { f.SetValue(instance, value); return true; } catch { }
                }

                t = t.BaseType;
            }

            return false;
        }

        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName + ", UnityEditor");
            if (t != null) return t;

            t = Type.GetType(fullName + ", UnityEditor.CoreModule");
            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }

            return null;
        }

        private static void RepaintAllInspectors()
        {
            Type inspectorType = FindType("UnityEditor.InspectorWindow");
            if (inspectorType == null) return;

            foreach (UnityEngine.Object win in Resources.FindObjectsOfTypeAll(inspectorType))
            {
                if (win is EditorWindow ew) ew.Repaint();
            }
        }
    }
}
#endif
