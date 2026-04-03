#if UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Plugins._EditorFreeTools.Editor.MyEditorImproves.Toolbar
{
    public class BuildProfilesToolbarButton
    {
        private const string BuildProfilesMenuPath = "File/Build Profiles";

        [MainToolbarElement("My Tools/Open Build Profiles Btn", defaultDockPosition = MainToolbarDockPosition.Left)]
        public static MainToolbarElement BuildProfilesButton()
        {
            var icon = EditorGUIUtility.IconContent("d_BuildSettings.Editor").image as Texture2D;
            var content = new MainToolbarContent(icon);
            return new MainToolbarButton(content, () => { EditorApplication.ExecuteMenuItem(BuildProfilesMenuPath); });
        }
    }
}
#endif

