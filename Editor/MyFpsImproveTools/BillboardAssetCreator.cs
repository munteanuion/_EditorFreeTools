#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class BillboardAssetCreator
{
    [MenuItem("Assets/Create/Rendering/Billboard Asset", priority = 301)]
    private static void CreateBillboardAsset()
    {
        BillboardAsset billboard = new BillboardAsset();

        billboard.width = 2f;
        billboard.height = 4f;
        billboard.bottom = 0f;

        billboard.SetVertices(new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        });

        billboard.SetIndices(new ushort[]
        {
            0, 1, 2,
            0, 2, 3
        });

        billboard.SetImageTexCoords(new Vector4[]
        {
            new Vector4(0, 0, 1, 1)
        });

        string path = GetSelectedPath();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{path}/New Billboard Asset.asset");

        AssetDatabase.CreateAsset(billboard, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();

        Object createdAsset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        Selection.activeObject = createdAsset;
    }

    private static string GetSelectedPath()
    {
        string path = "Assets";

        foreach (Object obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            path = AssetDatabase.GetAssetPath(obj);

            if (!string.IsNullOrEmpty(path) &&
                System.IO.Directory.Exists(path))
            {
                return path;
            }

            if (!string.IsNullOrEmpty(path))
            {
                return System.IO.Path.GetDirectoryName(path);
            }
        }

        return path;
    }
}
#endif