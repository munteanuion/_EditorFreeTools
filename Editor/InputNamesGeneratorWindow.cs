#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace __Scripts.__Editor
{
    /// <summary>
    /// EditorWindow (Tools/Input Names Generator) care generează / actualizează clasa cu constante
    /// pentru un InputActionAsset ales manual.
    /// - Sursa: alegi InputActionAsset din inspectorul ferestrei.
    /// - Țintă: alegi fișierul .cs existent care va fi suprascris (numele fișierului NU se schimbă,
    ///   numele clasei va fi luat din numele fișierului). Dacă lăsi câmpul gol (null), se generează
    ///   un fișier nou în folderul implicit, cu denumirea bazată pe numele InputActions map-ului.
    /// </summary>
    public class InputNamesGeneratorWindow : EditorWindow
    {
        private const string DefaultGeneratedFolder = "Assets/__Scripts/__Global_Constants/InputActions/";
        private const string GeneratedPrefix = "Const";
        private const string GeneratedSuffix = ".cs";
        private const string DefaultNamespace = "__Global_Constants";

        [SerializeField] private InputActionAsset _inputAsset;
        [SerializeField] private MonoScript _targetScript;
        [SerializeField] private string _namespaceName = DefaultNamespace;

        [MenuItem("Tools/Input Names Generator")]
        public static void Open()
        {
            var window = GetWindow<InputNamesGeneratorWindow>("Input Names Generator");
            window.minSize = new Vector2(420, 220);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Input Names Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Sursa: InputActionAsset din care se citesc map-urile și acțiunile.\n" +
                "Țintă (.cs): fișierul care va fi SUPRASCRIS. Numele fișierului rămâne neschimbat, " +
                "numele clasei se ia din numele fișierului.\n" +
                "Dacă țintă este gol, se generează un fișier nou în folderul implicit, denumit după map.",
                MessageType.Info);

            EditorGUILayout.Space();

            _inputAsset = (InputActionAsset)EditorGUILayout.ObjectField(
                "Input Action Asset", _inputAsset, typeof(InputActionAsset), false);

            _targetScript = (MonoScript)EditorGUILayout.ObjectField(
                new GUIContent("Target Script (.cs)", "Lasă gol pentru a genera un fișier nou."),
                _targetScript, typeof(MonoScript), false);

            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_inputAsset == null))
            {
                var label = _targetScript != null
                    ? "Overwrite Target Script"
                    : "Generate New File";
                if (GUILayout.Button(label, GUILayout.Height(30)))
                {
                    Generate();
                }
            }

            if (_inputAsset == null)
            {
                EditorGUILayout.HelpBox("Alege un InputActionAsset pentru a continua.", MessageType.Warning);
            }
        }

        private void Generate()
        {
            try
            {
                string filePath;
                string className;

                if (_targetScript != null)
                {
                    // Suprascriem fișierul existent, păstrând numele fișierului.
                    filePath = AssetDatabase.GetAssetPath(_targetScript);
                    if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorUtility.DisplayDialog("Input Names Generator",
                            "Fișierul țintă nu este un script .cs valid.", "OK");
                        return;
                    }
                    className = Path.GetFileNameWithoutExtension(filePath);
                }
                else
                {
                    // Fișier nou, denumit după InputActionAsset (map), în folderul implicit.
                    var assetName = _inputAsset.name;
                    className = GeneratedPrefix + Sanitize(assetName.Replace(" ", ""));
                    Directory.CreateDirectory(DefaultGeneratedFolder);
                    filePath = Path.Combine(DefaultGeneratedFolder, className + GeneratedSuffix).Replace("\\", "/");
                }

                var newContent = BuildContent(className, _namespaceName, _inputAsset);

                File.WriteAllText(filePath, newContent, Encoding.UTF8);
                AssetDatabase.ImportAsset(filePath);
                AssetDatabase.Refresh();

                Debug.Log($"InputNamesGenerator: constante generate pentru '{_inputAsset.name}' în {filePath}");

                // Dacă a fost un fișier nou, încercăm să-l selectăm ca țintă pentru rulări viitoare.
                if (_targetScript == null)
                {
                    _targetScript = AssetDatabase.LoadAssetAtPath<MonoScript>(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"InputNamesGenerator: Eroare la generare: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Input Names Generator", $"Eroare: {ex.Message}", "OK");
            }
        }

        private static string BuildContent(string className, string namespaceName, InputActionAsset asset)
        {
            var sb = new StringBuilder();

            var hasNamespace = !string.IsNullOrWhiteSpace(namespaceName);
            var indent = hasNamespace ? "    " : "";

            if (hasNamespace)
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"{indent}public static class {Sanitize(className)}");
            sb.AppendLine($"{indent}{{");

            foreach (var map in asset.actionMaps)
            {
                sb.AppendLine($"{indent}    public static class {Sanitize(map.name)}");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        public const string Map = \"{map.name}\";");
                foreach (var action in map.actions)
                {
                    sb.AppendLine($"{indent}        public const string {Sanitize(action.name)} = \"{action.name}\";");
                }
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}}}");

            if (hasNamespace)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static string Sanitize(string name)
        {
            // Eliminăm spațiile și caracterele invalide pentru identificatori C#.
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }
            if (sb.Length == 0 || (!char.IsLetter(sb[0]) && sb[0] != '_'))
                sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
#endif
