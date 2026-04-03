#if UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Plugins._EditorFreeTools.Editor.MyEditorImproves.Toolbar
{
    public class AddressablesGroupsToolbarButton
    {
        private const string AddressablesGroupsMenuPath = "Window/Asset Management/Addressables/Groups";

        [MainToolbarElement("My Tools/Open Addressables Groups Btn", defaultDockPosition = MainToolbarDockPosition.Left)]
        public static MainToolbarElement AddressablesGroupsButton()
        {
            var icon = EditorGUIUtility.IconContent("d_FolderOpened Icon").image as Texture2D;
            var content = new MainToolbarContent(icon);
            return new MainToolbarButton(content, () => { EditorApplication.ExecuteMenuItem(AddressablesGroupsMenuPath); });
        }
    }
}
#endif

