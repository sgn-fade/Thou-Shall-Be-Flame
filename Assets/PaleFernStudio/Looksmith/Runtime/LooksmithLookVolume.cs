using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Marks a Volume as a Looksmith Look Volume. While one is enabled in a loaded scene, Looksmith
    /// injects its passes and styles Looksmith materials; the look itself lives in the Volume's
    /// profile (the Looksmith Look override), so it blends like any other Volume override.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    [AddComponentMenu("Looksmith/Looksmith Look Volume")]
    public sealed class LooksmithLookVolume : MonoBehaviour
    {
        void OnEnable() => LooksmithRuntime.Register(this);
        void OnDisable() => LooksmithRuntime.Unregister(this);
    }
}
