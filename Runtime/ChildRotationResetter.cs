using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Plugins._EditorFreeTools.Runtime
{
    // Resets chosen rotation axes (X/Y/Z) to zero for every child object, invoked from the
    // component context menu. Useful to straighten converted terrain trees that must stay
    // upright while other groups keep their terrain-slope tilt.
    [AddComponentMenu("Terrain Tools/Child Rotation Resetter")]
    public class ChildRotationResetter : MonoBehaviour
    {
        private const string UndoName = "Reset child rotations";

        #region Context Menu

        [ContextMenu("Reset Rotation X")]
        private void ResetRotationX()
        {
            ResetRotation(true, false, false);
        }

        [ContextMenu("Reset Rotation Y")]
        private void ResetRotationY()
        {
            ResetRotation(false, true, false);
        }

        [ContextMenu("Reset Rotation Z")]
        private void ResetRotationZ()
        {
            ResetRotation(false, false, true);
        }

        [ContextMenu("Reset Rotation XZ (Keep Yaw)")]
        private void ResetRotationXz()
        {
            ResetRotation(true, false, true);
        }

        [ContextMenu("Reset Rotation XYZ")]
        private void ResetRotationXyz()
        {
            ResetRotation(true, true, true);
        }

        #endregion

        #region Reset Logic

        private void ResetRotation(bool resetX, bool resetY, bool resetZ)
        {
            Transform[] descendants = GetComponentsInChildren<Transform>(true);
            int changed = 0;

#if UNITY_EDITOR
            int undoGroup = 0;

            if (!Application.isPlaying)
            {
                undoGroup = Undo.GetCurrentGroup();
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                Undo.RecordObjects(descendants, UndoName);
            }
#endif

            for (int i = 0; i < descendants.Length; i++)
            {
                Transform target = descendants[i];

                if (target == transform)
                {
                    continue;
                }

                Vector3 euler = target.localEulerAngles;
                Vector3 resetEuler = new Vector3(
                    resetX ? 0f : euler.x,
                    resetY ? 0f : euler.y,
                    resetZ ? 0f : euler.z);

                if (resetEuler == euler)
                {
                    continue;
                }

                target.localEulerAngles = resetEuler;
                changed++;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
#endif
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.CollapseUndoOperations(undoGroup);
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif

            Debug.Log($"[ChildRotationResetter] Reset rotation on {changed} of {descendants.Length - 1} child object(s).");
        }

        #endregion
    }
}
