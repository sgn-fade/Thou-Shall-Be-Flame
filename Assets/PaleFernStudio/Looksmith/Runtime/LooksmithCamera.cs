using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Per-camera Looksmith options. Add it to opt a camera out of every Looksmith pass. A HUD camera (an overlay drawing only the
    /// UI layer) is left out without it; add one with Exclude off to style such a camera anyway.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Looksmith/Looksmith Camera")]
    public sealed class LooksmithCamera : MonoBehaviour
    {
        [Tooltip("Skip every Looksmith pass for this camera (Looksmith materials still render, in plain PBR). Off: include it, even when it's a UI-only overlay, which Looksmith otherwise leaves unstyled.")]
        public bool excludeFromLooksmith = true;
    }
}
