using System;
using System.Collections.Generic;
using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Global on/off state for Looksmith: Bypass, the registry of active Look Volumes, and the
    /// shader globals that every Looksmith shader reads to decide between stylized and plain PBR.
    /// </summary>
    public static class LooksmithRuntime
    {
        static readonly List<LooksmithLookVolume> s_Volumes = new List<LooksmithLookVolume>();
        static bool s_Bypass;
        static Camera s_PreviewCamera;

        /// <summary>Raised when Bypass changes or a Look Volume is enabled/disabled.</summary>
        public static event Action StateChanged;

        /// <summary>
        /// When true, Looksmith materials render as standard URP-style PBR and every Looksmith pass is skipped (P7).
        /// </summary>
        public static bool Bypass
        {
            get => s_Bypass;
            set
            {
                if (s_Bypass == value)
                    return;
                s_Bypass = value;
                Shader.SetGlobalFloat(LooksmithShaderIDs.Bypass, value ? 1f : 0f);
                if (value)
                    Shader.SetGlobalFloat(LooksmithShaderIDs.Enabled, 0f);
                StateChanged?.Invoke();
            }
        }

        /// <summary>Look Volumes currently enabled in loaded scenes.</summary>
        public static IReadOnlyList<LooksmithLookVolume> ActiveVolumes => s_Volumes;

        /// <summary>
        /// The one Preview camera Looksmith styles: look thumbnails and previews render through it with their own Volume, even
        /// under Bypass or with no Look Volume in the open scenes. It never shows the A/B split or debug views. Set it only
        /// while rendering, then clear it.
        /// </summary>
        public static Camera previewCamera
        {
            get => s_PreviewCamera;
            set => s_PreviewCamera = value;
        }

        /// <summary>True for <see cref="previewCamera"/>.</summary>
        public static bool IsPreviewCamera(Camera camera) => camera != null && camera == s_PreviewCamera;

        /// <summary>True when Looksmith should style this camera: a Look Volume is active and Bypass is off.</summary>
        public static bool IsEnabledFor(Camera camera) =>
            IsWorldEnabledFor(camera) && (IsPreviewCamera(camera) || s_Volumes.Count > 0);

        /// <summary>
        /// True when Looksmith's world modules (water, atmosphere) draw for this camera: Bypass is off and it isn't a camera
        /// Looksmith leaves alone (a reflection or preview camera, a HUD overlay, a Looksmith Camera that opts out). Unlike
        /// <see cref="IsEnabledFor"/> no Look Volume is needed: the world is the same whichever Look draws it, or none.
        /// </summary>
        public static bool IsWorldEnabledFor(Camera camera)
        {
            if (IsPreviewCamera(camera))
                return true;
            if (s_Bypass || camera == null)
                return false;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return false;
            // a Looksmith Camera is the camera's own say; without one, a HUD camera (an overlay drawing only the UI layer)
            // stays crisp and unstyled on top, and the art styles run on the scene under it
            if (camera.TryGetComponent(out LooksmithCamera choice) && choice.isActiveAndEnabled)
                return !choice.excludeFromLooksmith;
            return !IsUiOverlay(camera);
        }

        /// <summary>An overlay camera that draws only the UI layer: a game's HUD.</summary>
        public static bool IsUiOverlay(Camera camera)
        {
            if (camera == null || !camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data)
                || data.renderType != UnityEngine.Rendering.Universal.CameraRenderType.Overlay)
                return false;
            int ui = LayerMask.NameToLayer("UI");
            return ui >= 0 && camera.cullingMask != 0 && (camera.cullingMask & ~(1 << ui)) == 0;
        }

        internal static void Register(LooksmithLookVolume volume)
        {
            if (!s_Volumes.Contains(volume))
            {
                s_Volumes.Add(volume);
                StateChanged?.Invoke();
            }
        }

        internal static void Unregister(LooksmithLookVolume volume)
        {
            if (s_Volumes.Remove(volume))
            {
                if (s_Volumes.Count == 0)
                    Shader.SetGlobalFloat(LooksmithShaderIDs.Enabled, 0f);
                StateChanged?.Invoke();
            }
        }

        // Domain reload may be disabled: reset statics explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Volumes.Clear();
            StateChanged = null;
            s_Bypass = false;
            s_PreviewCamera = null;
            Shader.SetGlobalFloat(LooksmithShaderIDs.Bypass, 0f);
            Shader.SetGlobalFloat(LooksmithShaderIDs.Enabled, 0f);
        }
    }
}
