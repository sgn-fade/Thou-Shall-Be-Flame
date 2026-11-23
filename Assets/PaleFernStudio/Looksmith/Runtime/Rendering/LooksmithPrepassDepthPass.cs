using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Hidden layers: tells the Looksmith surfaces drawn in the opaque pass that the camera's depth texture holds this
    /// frame's depth prepass, so an alpha-clipped fragment lying behind the depth found at its pixel (a leaf card behind
    /// nearer leaves) skips its lighting and is dropped: the nearer surface draws that pixel. Foliage is drawn layer
    /// over layer, and each layer used to run the whole lighting (soft shadow filter included) only to be covered.
    /// Switched on after the prepass when URP ran one in Forward (the depth-normals prepass, no depth priming, no MSAA,
    /// whose depth texture is resolved), and off again after the opaques. The picture is the same either way.
    /// </summary>
    public sealed class LooksmithPrepassDepthPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public float value;
        }

        const BindingFlags k_Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly PropertyInfo s_DepthPriming = typeof(ScriptableRenderer).GetProperty("useDepthPriming", k_Internal);
        static readonly PropertyInfo s_Deferred = typeof(UniversalRenderer).GetProperty("usesDeferredLighting", k_Internal);

        readonly bool m_On;

        // Off for side-by-side comparisons (publisher tooling sets it through reflection): every layer shaded.
        static bool s_Disabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Disabled = false;

        /// <param name="on">True: the pass after the prepass (switches the skip on where it can); false: after the
        /// opaques.</param>
        public LooksmithPrepassDepthPass(bool on)
        {
            m_On = on;
            renderPassEvent = on ? RenderPassEvent.AfterRenderingPrePasses : RenderPassEvent.AfterRenderingOpaques;
            profilingSampler = new ProfilingSampler(Name);
        }

        string Name => m_On ? "Looksmith Hidden Layers On" : "Looksmith Hidden Layers Off";

        /// <summary>
        /// Whether the opaques of this camera can skip hidden layers: a Forward depth-normals prepass wrote the depth
        /// texture.
        /// </summary>
        public static bool CanSkip(UniversalResourceData resources, UniversalCameraData cameraData)
        {
            if (!resources.cameraNormalsTexture.IsValid() || !resources.cameraDepthTexture.IsValid())
                return false;
            if (cameraData.cameraTargetDescriptor.msaaSamples > 1)
                return false;
            var renderer = cameraData.renderer;
            if (renderer == null || s_DepthPriming == null || s_Deferred == null || !(renderer is UniversalRenderer))
                return false;
            return !(bool)s_DepthPriming.GetValue(renderer) && !(bool)s_Deferred.GetValue(renderer);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddUnsafePass<PassData>(Name, out var data, profilingSampler))
            {
                data.value = m_On && !s_Disabled && CanSkip(resources, cameraData) ? 1f : 0f;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, UnsafeGraphContext context) =>
                    context.cmd.SetGlobalFloat(LooksmithShaderIDs.PrepassDepth, d.value));
            }
        }
    }
}
