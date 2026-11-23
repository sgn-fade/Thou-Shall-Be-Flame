using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// A module that contributes render passes. Providers are asked per camera, in <see cref="order"/>.
    /// </summary>
    public interface ILooksmithPassProvider
    {
        int order { get; }
        void EnqueuePasses(ScriptableRenderer renderer, Camera camera, CameraRenderType renderType);
    }

    /// <summary>
    /// A world module's passes (water, atmosphere). They run for every camera Looksmith draws, with or without a Look: the
    /// world is the same whichever Look draws it, so water keeps its foam and the air its fog in a scene with no Look.
    /// Bypass still turns them off with everything else.
    /// </summary>
    public interface ILooksmithWorldPassProvider : ILooksmithPassProvider
    {
    }

    /// <summary>
    /// Injects Looksmith's passes per camera at runtime (P2): no URP asset or renderer edits are needed.
    /// Subscribes to <see cref="RenderPipelineManager.beginCameraRendering"/> in the editor and in players.
    /// </summary>
    public static class LooksmithPassInjector
    {
        static readonly List<ILooksmithPassProvider> s_Providers = new List<ILooksmithPassProvider>();
        static bool s_Installed;
        static LooksmithSetupPass s_SetupPass;
        static LooksmithPrepassDepthPass s_HiddenOn, s_HiddenOff;
        static LooksmithDebugViewPass s_DebugPass;
        static LooksmithLinesPass s_LinesPass;
        static LooksmithStylesPass s_StylesPass;

        // Renderers that carry an active LooksmithRendererFeature own their passes; the injector skips them.
        // Renderers rebuild their feature list when their data changes, so the lookup is cached per renderer instance.
        sealed class FeatureRef { public LooksmithRendererFeature feature; }
        static readonly ConditionalWeakTable<ScriptableRenderer, FeatureRef> s_FeatureCache = new ConditionalWeakTable<ScriptableRenderer, FeatureRef>();
        static readonly PropertyInfo s_RendererFeatures = typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool isInstalled => s_Installed;

        public static void Register(ILooksmithPassProvider provider)
        {
            if (provider == null || s_Providers.Contains(provider))
                return;
            s_Providers.Add(provider);
            s_Providers.Sort((a, b) => a.order.CompareTo(b.order));
        }

        public static void Unregister(ILooksmithPassProvider provider) => s_Providers.Remove(provider);

        public static void Install()
        {
            if (s_Installed)
                return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            s_Installed = true;
        }

        public static void Uninstall()
        {
            if (!s_Installed)
                return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            s_Installed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Uninstall();
            s_Providers.Clear();
            s_SetupPass = null;
            s_HiddenOn = s_HiddenOff = null;
            Shader.SetGlobalFloat(LooksmithShaderIDs.PrepassDepth, 0f);
            s_DebugPass = null;
            s_LinesPass = null;
            s_StylesPass?.Dispose();
            s_StylesPass = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallForPlayer() => Install();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallForEditor() => Install();
#endif

        /// <summary>Resolves the renderer URP will use for this camera without adding components to it.</summary>
        public static ScriptableRenderer GetRenderer(Camera camera, out CameraRenderType renderType)
        {
            renderType = CameraRenderType.Base;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                renderType = data.renderType;
                return data.scriptableRenderer;
            }
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return asset != null ? asset.scriptableRenderer : null;
        }

        /// <summary>
        /// True when this camera is the last Looksmith camera of its stack, where post-style passes (Art Styles, debug views) go.
        /// Overlays that opt out of Looksmith (a Looksmith Camera on a UI camera) don't count: they draw on top, unstyled.
        /// </summary>
        public static bool IsLastInStack(Camera camera, CameraRenderType renderType)
        {
            if (renderType == CameraRenderType.Base)
            {
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.cameraStack == null)
                    return true;
                foreach (var overlay in data.cameraStack)
                    if (overlay != null && overlay.isActiveAndEnabled && LooksmithRuntime.IsEnabledFor(overlay))
                        return false;
                return true;
            }

            foreach (var other in Camera.allCameras)
            {
                if (other == camera || !other.TryGetComponent(out UniversalAdditionalCameraData baseData))
                    continue;
                if (baseData.renderType != CameraRenderType.Base || baseData.cameraStack == null || !baseData.cameraStack.Contains(camera))
                    continue;
                Camera last = null;
                foreach (var overlay in baseData.cameraStack)
                    if (overlay != null && overlay.isActiveAndEnabled && LooksmithRuntime.IsEnabledFor(overlay))
                        last = overlay;
                return last == camera;
            }
            return false;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            PushUnityFog();
            LooksmithWaterPlanes.Push(camera);
            if (!LooksmithRuntime.IsWorldEnabledFor(camera))
                return;

            var renderer = GetRenderer(camera, out var renderType);
            if (renderer == null || HasLooksmithFeature(renderer))
                return;

            EnqueueForCamera(renderer, camera, renderType);
        }

        /// <summary>
        /// Enqueues what a camera gets: everything for a camera a Look styles; for one without a Look, the world modules'
        /// passes only (water, atmosphere). Also used by the opt-in Renderer Feature.
        /// </summary>
        public static void EnqueueForCamera(ScriptableRenderer renderer, Camera camera, CameraRenderType renderType)
        {
            if (LooksmithRuntime.IsEnabledFor(camera))
                EnqueueAllPasses(renderer, camera, renderType);
            else if (LooksmithRuntime.IsWorldEnabledFor(camera))
                EnqueueWorldPasses(renderer, camera, renderType);
        }

        /// <summary>Enqueues the world modules' passes only (a camera with no Look).</summary>
        public static void EnqueueWorldPasses(ScriptableRenderer renderer, Camera camera, CameraRenderType renderType)
        {
            for (int i = 0; i < s_Providers.Count; i++)
            {
                if (!(s_Providers[i] is ILooksmithWorldPassProvider))
                    continue;
                try
                {
                    s_Providers[i].EnqueuePasses(renderer, camera, renderType);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// The scene's own Unity fog, for Looksmith surfaces. URP's Unlit graphs never take it, so a restyled surface dropped
        /// out of the fog the rest of the scene sits in (ForestLake's blue distance went orange). Off while a Looksmith Sky
        /// sets that fog: it's meant for the other materials, Looksmith surfaces have the Look's haze. Set for every camera,
        /// Bypass included (Looksmith materials then render like URP Lit, fog and all).
        /// </summary>
        static void PushUnityFog()
        {
            var sky = LooksmithSky.active;
            bool on = RenderSettings.fog && !(sky != null && sky.drivesFog);
            Shader.SetGlobalVector(LooksmithShaderIDs.UnityFog, on
                ? new Vector4((float)RenderSettings.fogMode, RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance)
                : Vector4.zero);
            var c = RenderSettings.fogColor;
            Shader.SetGlobalColor(LooksmithShaderIDs.UnityFogColor, QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c);
        }

        /// <summary>True when the renderer has an active <see cref="LooksmithRendererFeature"/> (cached per renderer).</summary>
        public static bool HasLooksmithFeature(ScriptableRenderer renderer)
        {
            if (!s_FeatureCache.TryGetValue(renderer, out var entry))
            {
                entry = new FeatureRef();
                if (s_RendererFeatures?.GetValue(renderer) is List<ScriptableRendererFeature> features)
                    foreach (var f in features)
                        if (f is LooksmithRendererFeature looksmith)
                            entry.feature = looksmith;
                s_FeatureCache.Add(renderer, entry);
            }
            return entry.feature != null && entry.feature.isActive;
        }

        /// <summary>Enqueues the core passes and every module's passes for one camera.</summary>
        public static void EnqueueAllPasses(ScriptableRenderer renderer, Camera camera, CameraRenderType renderType)
        {
            EnqueueCorePasses(renderer, camera, renderType);
            for (int i = 0; i < s_Providers.Count; i++)
            {
                try
                {
                    s_Providers[i].EnqueuePasses(renderer, camera, renderType);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>Enqueues the passes every Looksmith camera gets. Also used by the opt-in Renderer Feature.</summary>
        public static void EnqueueCorePasses(ScriptableRenderer renderer, Camera camera, CameraRenderType renderType)
        {
            s_SetupPass ??= new LooksmithSetupPass();
            // Soft particles need scene depth: requested per camera, never through the URP asset (P2).
            s_SetupPass.ConfigureInput(LooksmithLookState.Get(camera).wantsDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            renderer.EnqueuePass(s_SetupPass);
            s_HiddenOn ??= new LooksmithPrepassDepthPass(true);
            s_HiddenOff ??= new LooksmithPrepassDepthPass(false);
            renderer.EnqueuePass(s_HiddenOn);
            renderer.EnqueuePass(s_HiddenOff);

            // Previews and reflections get the Look's shading only; the Looksmith preview camera (look thumbnails) gets everything.
            bool fullPasses = LooksmithRuntime.IsPreviewCamera(camera) || (camera.cameraType != CameraType.Preview && camera.cameraType != CameraType.Reflection);

            // Lines read scene depth and normals, so they run on the base camera of a stack (the camera-stack rule).
            // Inputs are requested before this frame's Volume update: planned from last frame (or the volumes, frame one).
            if (renderType == CameraRenderType.Base && fullPasses)
            {
                s_LinesPass ??= new LooksmithLinesPass();
                if (s_LinesPass.Setup(LooksmithLookState.PlannedLines(camera)))
                    renderer.EnqueuePass(s_LinesPass);
            }

            // Post-style passes run once per stack, on the last Looksmith camera of the stack.
            bool lastInStack = IsLastInStack(camera, renderType);
            if (lastInStack && fullPasses)
            {
                s_StylesPass ??= new LooksmithStylesPass();
                if (s_StylesPass.Setup(LooksmithLookState.PlannedStyles(camera)))
                    renderer.EnqueuePass(s_StylesPass);
            }

            if (LooksmithDebug.NeedsPass(LooksmithDebug.mode) && lastInStack && camera.cameraType != CameraType.Preview)
            {
                s_DebugPass ??= new LooksmithDebugViewPass();
                if (s_DebugPass.Setup(LooksmithDebug.mode))
                    renderer.EnqueuePass(s_DebugPass);
            }
        }
    }
}
