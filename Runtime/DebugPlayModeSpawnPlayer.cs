using UnityEngine;

#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
#endif

namespace Plugins._EditorFreeTools.Runtime
{
    public class DebugSpawnPlayer : MonoBehaviour
    {
#if UNITY_EDITOR
        private async void Start()
        {
            try
            {
                await Task.Delay(100);
            
                if (!PlayModeSpawnData.HasSpawn)
                    return;

                transform.SetPositionAndRotation(
                    PlayModeSpawnData.Position,
                    PlayModeSpawnData.Rotation
                );

                PlayModeSpawnData.HasSpawn = false;
            }
            catch (Exception)
            {
                // ignore
            }
        }
#endif
    }
}
