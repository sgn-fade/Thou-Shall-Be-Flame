using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The Art Style stack: every active style override in <see cref="LooksmithStyle.order"/>, then the Look's Finish, after
    /// URP's post-processing and before the final blit. Runs once per camera stack, on the last styled camera (an
    /// overlay that opts out with a Looksmith Camera, such as a UI camera, draws on top unstyled).
    /// </summary>
    public sealed class LooksmithStylesPass : ScriptableRenderPass
    {
        readonly Dictionary<Type, LooksmithStyleRenderer> m_Renderers = new Dictionary<Type, LooksmithStyleRenderer>();
        readonly List<LooksmithStyle> m_Active = new List<LooksmithStyle>();
        readonly LooksmithStyleContext m_Context = new LooksmithStyleContext();
        readonly LooksmithFinishRenderer m_Finish = new LooksmithFinishRenderer();
        readonly List<string> m_Names = new List<string>();
        readonly List<string> m_LastNames = new List<string>();
        string m_OrderText = "";

        sealed class DiagnosticsData
        {
            public CameraType cameraType;
            public string order;
            public bool masked;
            public int cameraId;
        }

        public LooksmithStylesPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            profilingSampler = new ProfilingSampler("Looksmith Art Styles");
            requiresIntermediateTexture = true;
        }

        public bool Setup(in LooksmithLookState.StylePlan plan)
        {
            if (!plan.active)
                return false;
            ConfigureInput(plan.inputs);
            return true;
        }

        static int Compare(LooksmithStyle a, LooksmithStyle b)
        {
            int c = a.order.value.CompareTo(b.order.value);
            return c != 0 ? c : string.CompareOrdinal(a.GetType().Name, b.GetType().Name);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            var look = LooksmithLookState.Get(cameraData.camera);
            var stack = VolumeManager.instance.stack;
            m_Active.Clear();
            foreach (var type in LooksmithLookState.StyleTypes)
                if (stack.GetComponent(type) is LooksmithStyle style && style.IsStyleActive())
                    m_Active.Add(style);
            m_Active.Sort(Compare);
            bool finish = look.lookActive && look.finish.active;
            if (m_Active.Count == 0 && !finish)
                return;

            var source = resourceData.activeColorTexture;
            var ctx = m_Context;
            ctx.frameData = frameData;
            ctx.resources = resourceData;
            ctx.cameraData = cameraData;
            ctx.look = look;
            ctx.colorDesc = renderGraph.GetTextureDesc(source);
            if (ctx.colorDesc.sizeMode != TextureSizeMode.Explicit || ctx.colorDesc.width <= 0)
            {
                ctx.colorDesc.width = cameraData.cameraTargetDescriptor.width;
                ctx.colorDesc.height = cameraData.cameraTargetDescriptor.height;
            }
            ctx.scale = ctx.colorDesc.height / LooksmithLinesPass.ReferenceHeight;
            var inputs = frameData.Contains<LooksmithStyleInputs>() ? frameData.Get<LooksmithStyleInputs>() : null;
            bool hasMask = inputs != null && inputs.hasMask && inputs.mask.IsValid();
            ctx.mask = hasMask ? inputs.mask : TextureHandle.nullHandle;
            if (hasMask)
            {
                var maskDesc = renderGraph.GetTextureDesc(inputs.mask);
                ctx.maskSize = new Vector2(maskDesc.width, maskDesc.height);
            }

            // Marks laid on the surfaces (paper, brush strokes) ride swaying leaves: the wind's offsets, once for the stack.
            bool marks = finish && look.finish.paper > 0.001f && look.finish.paperOnSurfaces;
            foreach (var style in m_Active)
                marks |= style.laysMarksOnSurfaces;
            ctx.sway = marks ? LooksmithSway.Record(renderGraph, frameData) : TextureHandle.nullHandle;

            var current = source;
            bool changed = false;
            m_Names.Clear();
            foreach (var style in m_Active)
            {
                var type = style.GetType();
                if (!m_Renderers.TryGetValue(type, out var renderer))
                {
                    renderer = style.CreateRenderer();
                    m_Renderers.Add(type, renderer);
                }
                if (renderer == null || !renderer.Prepare())
                    continue;
                current = renderer.Record(renderGraph, style, ctx, current);
                changed = true;
                m_Names.Add(type.Name);
            }
            if (finish && m_Finish.Prepare())
            {
                current = m_Finish.Record(renderGraph, look, ctx, current);
                changed = true;
                m_Names.Add("Finish");
            }
            if (!changed)
                return;
            // A/B compare: left of the split shows the image before the stack (lines and materials split themselves).
            if (LooksmithCompare.ForCamera(cameraData.cameraType).y > 0.5f && m_Finish.Prepare())
                current = m_Finish.RecordSplit(renderGraph, ctx, source, current);
            LooksmithCameraColor.Set(renderGraph, resourceData, cameraData, source, current, "Looksmith Styles (to stack)");

            if (!SameNames())
            {
                m_LastNames.Clear();
                m_LastNames.AddRange(m_Names);
                m_OrderText = string.Join(" > ", m_Names);
            }
            using (var builder = renderGraph.AddUnsafePass<DiagnosticsData>("Looksmith Art Styles (diagnostics)", out var data))
            {
                data.cameraType = cameraData.cameraType;
                data.order = m_OrderText;
                data.masked = hasMask;
                data.cameraId = LooksmithObjectIds.Hash(cameraData.camera);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DiagnosticsData d, UnsafeGraphContext _) => LooksmithPassDiagnostics.RecordStyles(d.cameraType, d.order, d.masked, d.cameraId));
            }
        }

        bool SameNames()
        {
            if (m_Names.Count != m_LastNames.Count)
                return false;
            for (int i = 0; i < m_Names.Count; i++)
                if (m_Names[i] != m_LastNames[i])
                    return false;
            return true;
        }

        /// <summary>Releases every style's materials and history.</summary>
        public void Dispose()
        {
            foreach (var renderer in m_Renderers.Values)
                renderer?.Dispose();
            m_Renderers.Clear();
            m_Finish.Dispose();
        }
    }

    /// <summary>The Look's Finish group: chromatic fringe, grading LUT, paper, vignette and grain. Always the last layer.</summary>
    sealed class LooksmithFinishRenderer
    {
        Material m_Material;
        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("Looksmith Finish");

        public bool Prepare()
        {
            if (m_Material != null)
                return true;
            var resources = LooksmithRuntimeResources.Instance;
            if (resources == null || resources.finishShader == null)
                return false;
            m_Material = CoreUtils.CreateEngineMaterial(resources.finishShader);
            return true;
        }

        public TextureHandle Record(RenderGraph renderGraph, LooksmithLookState look, LooksmithStyleContext context, TextureHandle source)
        {
            var f = look.finish;
            var result = context.CreateColor(renderGraph, "_LooksmithFinish");
            var size = context.size;
            var shadow = look.shadowColor;
            var vignetteColor = Color.Lerp(Color.black, shadow, f.vignetteFromPalette);
            Texture paper = f.paperTexture != null ? f.paperTexture : LooksmithStyleTextures.Paper();
            float paperUv = (LooksmithLinesPass.ReferenceHeight / Mathf.Max(size.y, 1)) / (paper.width * Mathf.Max(f.paperScale, 0.01f));
            var lut = f.lut;
            var lutParams = lut != null ? new Vector4(1f / lut.width, 1f / lut.height, lut.height - 1f, 0f) : Vector4.zero;

            // Paper on the surfaces: one paper texel covers paperScale pixels (at 1080p) wherever the surface is.
            var resources = context.resources;
            bool onSurfaces = f.paper > 0.001f && f.paperOnSurfaces && resources.cameraDepthTexture.IsValid() && resources.cameraNormalsTexture.IsValid();
            var cam = context.camera;
            float metresPerPixel = cam.orthographic
                ? 2f * cam.orthographicSize / Mathf.Max(size.y, 1)
                : 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(size.y, 1);
            float pixelsPerTexel = Mathf.Max(f.paperScale, 0.01f) * size.y / LooksmithLinesPass.ReferenceHeight;

            using (var blit = LooksmithStyleBlit.Begin(renderGraph, "Looksmith Finish", m_Material, 0, source, result, s_Sampler))
            {
                blit.Vector(LooksmithShaderIDs.Params[0], new Vector4(f.grain, f.grainSize * context.scale, Time.frameCount % 61, f.vignette))
                    .Vector(LooksmithShaderIDs.Params[1], new Vector4(vignetteColor.r, vignetteColor.g, vignetteColor.b, f.paper))
                    .Vector(LooksmithShaderIDs.Params[2], new Vector4(paperUv, f.fringe, lut != null ? f.lutContribution : 0f, size.x / (float)Mathf.Max(size.y, 1)))
                    .Vector(LooksmithShaderIDs.Params[3], lutParams)
                    .Vector(LooksmithShaderIDs.Params[4], new Vector4(size.x, size.y, onSurfaces ? 1f : 0f, cam.orthographic ? 1f : 0f))
                    .Vector(LooksmithShaderIDs.Params[5], new Vector4(metresPerPixel, pixelsPerTexel, paper.width, 0f))
                    .Texture(LooksmithShaderIDs.Paper, paper)
                    .Sway(context);
                // the sky keeps its rendered look (Style Sky off): no paper or grain on it
                if (f.keepSky && resources.cameraDepthTexture.IsValid())
                    blit.Texture(LooksmithShaderIDs.MixDepth, resources.cameraDepthTexture)
                        .Vector(LooksmithShaderIDs.MixSky, new Vector4(1f, f.skyDistance, 0f, 0f));
                else
                    blit.Vector(LooksmithShaderIDs.MixSky, Vector4.zero);
                if (onSurfaces)
                    blit.Read(resources.cameraDepthTexture).Read(resources.cameraNormalsTexture);
                if (lut != null)
                    blit.Texture(LooksmithShaderIDs.GradingLut, lut);
            }
            return result;
        }

        static readonly ProfilingSampler s_SplitSampler = new ProfilingSampler("Looksmith A/B Split");

        /// <summary>A/B compare: the unstyled image left of the split, the styled one right of it.</summary>
        public TextureHandle RecordSplit(RenderGraph renderGraph, LooksmithStyleContext context, TextureHandle unstyled, TextureHandle styled)
        {
            var result = context.CreateColor(renderGraph, "_LooksmithSplit");
            using (var blit = LooksmithStyleBlit.Begin(renderGraph, "Looksmith A/B Split", m_Material, 1, styled, result, s_SplitSampler))
                blit.Texture(LooksmithShaderIDs.SplitSource, unstyled);
            return result;
        }

        public void Dispose() => CoreUtils.Destroy(m_Material);
    }
}
