using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The Looksmith Sway pass: every visible surface the wind sways (a Looksmith material with Wind above 0), drawn with how far
    /// the wind has moved it. Styles that lay marks on the surfaces (paper, brush strokes) subtract it, so the marks lie
    /// where a leaf sits at rest and ride it as it sways; laid where the leaf is now, they stayed put while it swayed
    /// through them. One override draw of the opaque scene in which everything still collapses in the vertex stage.
    /// </summary>
    static class LooksmithSway
    {
        static readonly List<ShaderTagId> s_Tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit"),
        };
        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("Looksmith Sway");

        sealed class PassData
        {
            public RendererListHandle list;
        }

        // Off for side-by-side comparisons (publisher tooling sets it through reflection): marks laid where leaves are now.
        static bool s_Disabled;

        /// <summary>Records the pass for this camera; an invalid handle when it can't run (no depth texture, no shader).</summary>
        public static TextureHandle Record(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = LooksmithRuntimeResources.Instance;
            var shader = resources != null ? resources.swayShader : null;
            var resourceData = frameData.Get<UniversalResourceData>();
            if (s_Disabled || shader == null || !resourceData.cameraDepthTexture.IsValid())
                return TextureHandle.nullHandle;

            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var draw = RenderingUtils.CreateDrawingSettings(s_Tags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            draw.overrideShader = shader;
            draw.overrideShaderPassIndex = 0;
            // Not instanced: a terrain drawn instanced (Draw Instanced, the default) fills per-patch data this override
            // shader doesn't take, and Unity logged "Instancing: Unhandled property 'unity_ObjectToWorldArray'" for every
            // batch, thousands a second in the demo. Nor does the pass need lighting data per object.
            draw.enableInstancing = false;
#if !UNITY_6000_6_OR_NEWER
            draw.enableDynamicBatching = false;
#endif
            draw.perObjectData = PerObjectData.None;
            var filter = new FilteringSettings(RenderQueueRange.opaque);

            // the scene depth texture's size, which the shader's visibility test and the styles' uv reads share
            var desc = cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            var offsets = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_LooksmithSwayOffset", true);   // cleared: no sway

            // The camera's depth buffer, read-only, when it matches the target (no MSAA): the hardware depth test then drops
            // the leaf layers hidden behind nearer leaves before they're shaded. Alpha-clipped foliage is drawn layer
            // over layer, and every layer used to run the shader only to fail the visibility test against the depth
            // texture (which stays, for the targets the depth buffer can't serve).
            var depth = resourceData.cameraDepth;
            bool depthTest = false;
            if (depth.IsValid())
            {
                var depthDesc = renderGraph.GetTextureDesc(depth);
                depthTest = depthDesc.msaaSamples == MSAASamples.None && depthDesc.width == desc.width && depthDesc.height == desc.height;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Looksmith Sway", out var data, s_Sampler))
            {
                data.list = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                builder.UseRendererList(data.list);
                builder.UseTexture(resourceData.cameraDepthTexture);
                builder.SetRenderAttachment(offsets, 0);
                if (depthTest)
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
            }
            return offsets;
        }
    }
}
