using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    public enum LooksmithDebugView
    {
        None = 0,
        Normals = 1,
        Depth = 2,
        OpaqueColor = 3,
        /// <summary>Four quadrants: final colour, normals, depth, inverted opaque colour.</summary>
        InputsQuad = 4,
        /// <summary>The final image in greys: checks the value structure (lights, mids, darks) without colour.</summary>
        ValueCheck = 5,
        /// <summary>Looksmith surfaces with a mid-grey albedo and no emission: the Look's lighting alone.</summary>
        LightingOnly = 6,
        /// <summary>The Look's lines alone, on white.</summary>
        LinesOnly = 7,
    }

    /// <summary>Frame data the lines pass fills for the Lines Only debug view.</summary>
    public sealed class LooksmithDebugInputs : ContextItem
    {
        public TextureHandle linesOnly;

        public override void Reset() => linesOnly = TextureHandle.nullHandle;
    }

    /// <summary>Global debug-view selection (Scene overlay and tools).</summary>
    public static class LooksmithDebug
    {
        public static LooksmithDebugView mode { get; set; }

        /// <summary>True for the views drawn by the full-screen debug pass (Lighting Only is drawn by the surfaces).</summary>
        public static bool NeedsPass(LooksmithDebugView view) => view != LooksmithDebugView.None && view != LooksmithDebugView.LightingOnly;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => mode = LooksmithDebugView.None;
    }

    /// <summary>
    /// Full-screen debug views. Requests depth, normals and opaque colour through ConfigureInput,
    /// so they work without the URP asset's Depth/Opaque Texture settings (P2).
    /// </summary>
    public sealed class LooksmithDebugViewPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public TextureHandle source;
            public Material material;
            public float mode;
            public CameraType cameraType;
            public bool hasDepth, hasNormals, hasOpaque;
        }

        Material m_Material;
        LooksmithDebugView m_Mode;

        public LooksmithDebugViewPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            profilingSampler = new ProfilingSampler("Looksmith Debug View");
            requiresIntermediateTexture = true;
        }

        public bool Setup(LooksmithDebugView mode)
        {
            m_Mode = mode;
            if (m_Material == null)
            {
                var resources = LooksmithRuntimeResources.Instance;
                if (resources == null || resources.debugViewShader == null)
                    return false;
                m_Material = CoreUtils.CreateEngineMaterial(resources.debugViewShader);
            }
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            float mode = (float)m_Mode;
            TextureHandle copy;
            var debugInputs = frameData.Contains<LooksmithDebugInputs>() ? frameData.Get<LooksmithDebugInputs>() : null;
            if (m_Mode == LooksmithDebugView.LinesOnly && debugInputs != null && debugInputs.linesOnly.IsValid())
            {
                copy = debugInputs.linesOnly;   // the lines pass drew the Look's lines on white
            }
            else
            {
                if (m_Mode == LooksmithDebugView.LinesOnly)
                    mode = 8f;   // no lines this frame: plain white
                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                desc.name = "_LooksmithDebugSource";
                desc.clearBuffer = false;
                copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(resourceData.activeColorTexture, copy, Vector2.one, Vector2.zero, passName: "Looksmith Debug Copy");
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Looksmith Debug View", out var passData, profilingSampler))
            {
                passData.source = copy;
                passData.material = m_Material;
                passData.mode = mode;
                passData.cameraType = cameraData.cameraType;
                passData.hasDepth = resourceData.cameraDepthTexture.IsValid();
                passData.hasNormals = resourceData.cameraNormalsTexture.IsValid();
                passData.hasOpaque = resourceData.cameraOpaqueTexture.IsValid();

                builder.UseTexture(copy);
                if (passData.hasDepth)
                    builder.UseTexture(resourceData.cameraDepthTexture);
                if (passData.hasNormals)
                    builder.UseTexture(resourceData.cameraNormalsTexture);
                if (passData.hasOpaque)
                    builder.UseTexture(resourceData.cameraOpaqueTexture);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(LooksmithShaderIDs.DebugMode, data.mode);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    LooksmithPassDiagnostics.RecordDebug(data.cameraType, data.hasDepth, data.hasNormals, data.hasOpaque);
                });
            }
        }
    }
}
