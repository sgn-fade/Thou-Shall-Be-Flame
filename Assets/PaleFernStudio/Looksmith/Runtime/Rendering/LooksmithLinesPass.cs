using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Frame data the Pro object-ID pass fills for the lines pass (object edges and per-layer masks). Lite never sets it.
    /// </summary>
    public sealed class LooksmithLineInputs : ContextItem
    {
        public TextureHandle objectIds;
        public bool hasIds;

        public override void Reset()
        {
            objectIds = TextureHandle.nullHandle;
            hasIds = false;
        }
    }

    /// <summary>
    /// The Look's lines: screen-space edges from depth and normals (plus colour and object edges in Pro), drawn over the
    /// opaque image before transparents. Requests depth, normals and opaque colour itself (P2), on base cameras only
    /// (overlay cameras have their own depth; see the camera-stack rule).
    /// </summary>
    public sealed class LooksmithLinesPass : ScriptableRenderPass
    {
        /// <summary>Line weights are authored for 1080p and scale with the target height.</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>Smallest line scale on the Looksmith preview camera (look thumbnails), where 1080p widths would vanish.</summary>
        public const float PreviewScale = 0.75f;

        sealed class PassData
        {
            public Material material;
            public Vector4 p1, p2, p3, color, ids;
            public TextureHandle objectIds;
            public CameraType cameraType;
            public float targetHeight;
        }

        sealed class DebugData
        {
            public Material material;
            public Vector4 p1, p2, p3, color, ids;
            public TextureHandle objectIds;
        }

        Material m_Material;

        public LooksmithLinesPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            profilingSampler = new ProfilingSampler("Looksmith Lines");
        }

        public bool Setup(in LooksmithLookState.LineSettings lines)
        {
            if (!lines.enabled || lines.weight <= 0.01f)
                return false;
            if (m_Material == null)
            {
                var resources = LooksmithRuntimeResources.Instance;
                if (resources == null || resources.linesShader == null)
                    return false;
                m_Material = CoreUtils.CreateEngineMaterial(resources.linesShader);
            }
            bool wantsColor = lines.fromSurface > 0.001f || lines.lightWeight > 0.001f || lines.colour > 0.001f;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | (wantsColor ? ScriptableRenderPassInput.Color : 0));
            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lines = LooksmithLookState.Get(cameraData.camera).lines;
            if (!lines.enabled || lines.weight <= 0.01f || !resourceData.cameraDepthTexture.IsValid() || !resourceData.cameraNormalsTexture.IsValid())
                return;

            bool hasOpaque = resourceData.cameraOpaqueTexture.IsValid();
            var inputs = frameData.Contains<LooksmithLineInputs>() ? frameData.Get<LooksmithLineInputs>() : null;
            bool hasIds = inputs != null && inputs.hasIds && inputs.objectIds.IsValid();
            float scale = cameraData.cameraTargetDescriptor.height / ReferenceHeight;
            if (LooksmithRuntime.IsPreviewCamera(cameraData.camera))
                scale = Mathf.Max(scale, PreviewScale);   // look thumbnails are small: keep their lines legible

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Looksmith Lines", out var passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.p1 = new Vector4(lines.weight * scale, lines.depth, lines.normal, hasOpaque ? lines.colour : 0f);
                passData.p2 = new Vector4(lines.fadeStart, lines.fadeEnd, hasOpaque ? lines.lightWeight : 0f, lines.fromSurface);
                passData.p3 = new Vector4(lines.jitter, lines.wobble, lines.silhouettesOnly ? 1f : 0f, lines.objectEdges ? 1f : 0f);
                passData.color = new Vector4(lines.color.r, lines.color.g, lines.color.b, hasOpaque ? 1f : 0f);
                passData.ids = new Vector4(hasIds ? 1f : 0f, hasIds && lines.layerMask != -1 ? 1f : 0f, 0f, 0f);
                passData.objectIds = hasIds ? inputs.objectIds : TextureHandle.nullHandle;
                passData.cameraType = cameraData.cameraType;
                passData.targetHeight = cameraData.cameraTargetDescriptor.height;

                builder.UseTexture(resourceData.cameraDepthTexture);
                builder.UseTexture(resourceData.cameraNormalsTexture);
                if (hasOpaque)
                    builder.UseTexture(resourceData.cameraOpaqueTexture);
                if (hasIds)
                    builder.UseTexture(inputs.objectIds);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                // the camera's own depth (it holds what the depth-normals prepass leaves out, like a grass field): lines are
                // depth-tested against it, so they don't show through what stands in front of their surface
                if (resourceData.activeDepthTexture.IsValid() && !resourceData.activeDepthTexture.Equals(resourceData.cameraDepthTexture))
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var block = Block(data.p1, data.p2, data.p3, data.color, data.ids, data.objectIds);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, block);
                    LooksmithPassDiagnostics.RecordLines(data.cameraType, data.p1.x, data.targetHeight);
                });
            }

            if (LooksmithDebug.mode == LooksmithDebugView.LinesOnly && cameraData.cameraType != CameraType.Preview)
                RecordLinesOnly(renderGraph, frameData, resourceData, hasOpaque, hasIds, inputs, lines, scale);
        }

        // The Lines Only debug view: the same lines drawn on white into their own texture (the debug pass shows it).
        void RecordLinesOnly(RenderGraph renderGraph, ContextContainer frameData, UniversalResourceData resourceData,
            bool hasOpaque, bool hasIds, LooksmithLineInputs inputs, in LooksmithLookState.LineSettings lines, float scale)
        {
            var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
            desc.name = "_LooksmithLinesOnly";
            desc.clearBuffer = true;
            desc.clearColor = Color.white;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            var target = renderGraph.CreateTexture(desc);
            using (var builder = renderGraph.AddRasterRenderPass<DebugData>("Looksmith Lines Only", out var data, profilingSampler))
            {
                data.material = m_Material;
                data.p1 = new Vector4(lines.weight * scale, lines.depth, lines.normal, hasOpaque ? lines.colour : 0f);
                data.p2 = new Vector4(lines.fadeStart, lines.fadeEnd, hasOpaque ? lines.lightWeight : 0f, lines.fromSurface);
                data.p3 = new Vector4(lines.jitter, lines.wobble, lines.silhouettesOnly ? 1f : 0f, lines.objectEdges ? 1f : 0f);
                data.color = new Vector4(lines.color.r, lines.color.g, lines.color.b, hasOpaque ? 1f : 0f);
                data.ids = new Vector4(hasIds ? 1f : 0f, hasIds && lines.layerMask != -1 ? 1f : 0f, 1f, 0f);
                data.objectIds = hasIds ? inputs.objectIds : TextureHandle.nullHandle;
                builder.UseTexture(resourceData.cameraDepthTexture);
                builder.UseTexture(resourceData.cameraNormalsTexture);
                if (hasOpaque)
                    builder.UseTexture(resourceData.cameraOpaqueTexture);
                if (hasIds)
                    builder.UseTexture(inputs.objectIds);
                builder.SetRenderAttachment(target, 0);
                builder.SetRenderFunc((DebugData d, RasterGraphContext context) =>
                {
                    var block = Block(d.p1, d.p2, d.p3, d.color, d.ids, d.objectIds);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, block);
                });
            }
            frameData.GetOrCreate<LooksmithDebugInputs>().linesOnly = target;
        }

        // The draw's parameters in a property block (copied when the draw is recorded): set on the shared material, the
        // Lines Only draw's values would reach the main lines too, as the command buffer reads the material when it runs.
        static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();

        static MaterialPropertyBlock Block(Vector4 p1, Vector4 p2, Vector4 p3, Vector4 color, Vector4 ids, TextureHandle objectIds)
        {
            s_Block.Clear();
            s_Block.SetVector(LooksmithShaderIDs.LineParams, p1);
            s_Block.SetVector(LooksmithShaderIDs.LineParams2, p2);
            s_Block.SetVector(LooksmithShaderIDs.LineParams3, p3);
            s_Block.SetVector(LooksmithShaderIDs.LineColor, color);
            s_Block.SetVector(LooksmithShaderIDs.LineIdParams, ids);
            if (objectIds.IsValid())
                s_Block.SetTexture(LooksmithShaderIDs.LineIds, objectIds);
            return s_Block;
        }
    }
}
