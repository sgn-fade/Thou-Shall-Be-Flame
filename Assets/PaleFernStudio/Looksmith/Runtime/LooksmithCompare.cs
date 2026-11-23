using System;
using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// A/B compare: a vertical split across Game and Scene view cameras. Left of the split renders plain, exactly as
    /// under Bypass (standard PBR, no lines, no Art Styles); right of it renders the Look. Driven by the Scene view
    /// overlay and the Hub; a player never sets it unless a script does.
    /// </summary>
    public static class LooksmithCompare
    {
        static bool s_Active;
        static float s_Split = 0.5f;

        /// <summary>Raised when the split is switched on or off or moved.</summary>
        public static event Action Changed;

        /// <summary>True while the A/B split is shown.</summary>
        public static bool active
        {
            get => s_Active;
            set
            {
                if (s_Active == value)
                    return;
                s_Active = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Where the split sits, from 0 (left edge) to 1 (right edge) of the view.</summary>
        public static float split
        {
            get => s_Split;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(s_Split, value))
                    return;
                s_Split = value;
                Changed?.Invoke();
            }
        }

        /// <summary>The <c>_LooksmithSplit</c> value for a camera: x the split position, y 1 when the split applies.</summary>
        public static Vector4 ForCamera(CameraType cameraType) =>
            s_Active && (cameraType == CameraType.Game || cameraType == CameraType.SceneView)
                ? new Vector4(s_Split, 1f, 0f, 0f)
                : Vector4.zero;

        // Domain reload may be disabled: reset statics explicitly (editor listeners such as the overlay stay subscribed).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Active = false;
            s_Split = 0.5f;
        }
    }
}
