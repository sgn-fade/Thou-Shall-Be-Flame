using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// One full-screen material pass in the render graph, with its inputs declared and its properties handed to the draw in
    /// a property block. A material is shared by several passes of a frame, and a command buffer reads a material's own
    /// properties only when it executes, so values set on the material would leak: every pass would see the last pass's.
    /// A property block is copied when the draw is recorded, so each pass keeps its own.
    /// <code>using (var b = LooksmithStyleBlit.Begin(rg, "Name", material, pass, source, destination)) b.Texture(id, handle).Vector(id, v);</code>
    /// </summary>
    public readonly struct LooksmithStyleBlit : IDisposable
    {
        const int k_Max = 12;

        sealed class Data
        {
            public Material material;
            public int pass;
            public TextureHandle source;
            public readonly TextureHandle[] handles = new TextureHandle[k_Max];
            public readonly int[] handleIds = new int[k_Max];
            public int handleCount;
            public readonly Texture[] textures = new Texture[k_Max];
            public readonly int[] textureIds = new int[k_Max];
            public int textureCount;
            public readonly Vector4[] vectors = new Vector4[k_Max];
            public readonly int[] vectorIds = new int[k_Max];
            public int vectorCount;
            public LooksmithStyleMix mix;
            public bool hasMix;
            public Action<RasterGraphContext> after;
        }

        static readonly BaseRenderFunc<Data, RasterGraphContext> s_Execute = Execute;
        static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();
        static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        readonly IRasterRenderGraphBuilder m_Builder;
        readonly Data m_Data;

        LooksmithStyleBlit(IRasterRenderGraphBuilder builder, Data data)
        {
            m_Builder = builder;
            m_Data = data;
        }

        public static LooksmithStyleBlit Begin(RenderGraph renderGraph, string name, Material material, int pass, TextureHandle source, TextureHandle destination, ProfilingSampler sampler = null)
        {
            var builder = renderGraph.AddRasterRenderPass<Data>(name, out var data, sampler);
            data.material = material;
            data.pass = pass;
            data.source = source;
            data.handleCount = data.textureCount = data.vectorCount = 0;
            data.hasMix = false;
            data.after = null;
            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0);
            builder.SetRenderFunc(s_Execute);
            return new LooksmithStyleBlit(builder, data);
        }

        /// <summary>Another render-graph texture the pass samples.</summary>
        public LooksmithStyleBlit Texture(int id, TextureHandle handle)
        {
            if (!handle.IsValid())
                return this;
            m_Builder.UseTexture(handle);
            m_Data.handleIds[m_Data.handleCount] = id;
            m_Data.handles[m_Data.handleCount++] = handle;
            return this;
        }

        /// <summary>
        /// The camera's own depth for outline styles (<see cref="LooksmithStyleContext.OccluderDepth"/>): bound with its flag
        /// (_LooksmithOccluderParams.x = 1) when there is one.
        /// </summary>
        public LooksmithStyleBlit Occluders(LooksmithStyleContext context, RenderGraph renderGraph)
        {
            var d = context.OccluderDepth(renderGraph);
            if (!d.IsValid())
                return Vector(LooksmithShaderIDs.OccluderParams, Vector4.zero);
            return Texture(LooksmithShaderIDs.OccluderDepth, d).Vector(LooksmithShaderIDs.OccluderParams, new Vector4(1f, 0f, 0f, 0f));
        }

        /// <summary>
        /// The swaying surfaces' wind offsets (<see cref="LooksmithStyleContext.sway"/>), so marks laid on surfaces ride the
        /// leaves: bound with its flag (_LooksmithSwayParams.x = 1) when there is one.
        /// </summary>
        public LooksmithStyleBlit Sway(LooksmithStyleContext context)
        {
            if (!context.sway.IsValid())
                return Vector(LooksmithShaderIDs.SwayParams, Vector4.zero);
            return Texture(LooksmithShaderIDs.SwayOffset, context.sway).Vector(LooksmithShaderIDs.SwayParams, new Vector4(1f, 0f, 0f, 0f));
        }

        /// <summary>A texture that lives outside the render graph (paper, palette, a user's LUT).</summary>
        public LooksmithStyleBlit Texture(int id, Texture texture)
        {
            m_Data.textureIds[m_Data.textureCount] = id;
            m_Data.textures[m_Data.textureCount++] = texture;
            return this;
        }

        /// <summary>A camera texture the shader reads through its global name (depth, normals, motion vectors).</summary>
        public LooksmithStyleBlit Read(TextureHandle handle)
        {
            if (handle.IsValid())
                m_Builder.UseTexture(handle);
            return this;
        }

        public LooksmithStyleBlit Vector(int id, Vector4 value)
        {
            m_Data.vectorIds[m_Data.vectorCount] = id;
            m_Data.vectors[m_Data.vectorCount++] = value;
            return this;
        }

        /// <summary>A second colour target (SV_Target1) for passes that write two images at once.</summary>
        public LooksmithStyleBlit Target1(TextureHandle handle)
        {
            m_Builder.SetRenderAttachment(handle, 1);
            return this;
        }

        /// <summary>Blends the result with what was under it (intensity, layer mask). For a style's last pass.</summary>
        public LooksmithStyleBlit Mix(LooksmithStyleMix mix)
        {
            mix.Use(m_Builder);
            m_Data.mix = mix;
            m_Data.hasMix = true;
            return this;
        }

        /// <summary>Runs after the draw (diagnostics). Must not capture per-frame state.</summary>
        public LooksmithStyleBlit After(Action<RasterGraphContext> after)
        {
            m_Data.after = after;
            return this;
        }

        public void Dispose() => m_Builder.Dispose();

        static void Execute(Data data, RasterGraphContext context)
        {
            var block = s_Block;
            block.Clear();
            block.SetTexture(s_BlitTexture, data.source);
            block.SetVector(s_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
            for (int i = 0; i < data.handleCount; i++)
                block.SetTexture(data.handleIds[i], data.handles[i]);
            for (int i = 0; i < data.textureCount; i++)
                block.SetTexture(data.textureIds[i], data.textures[i]);
            for (int i = 0; i < data.vectorCount; i++)
                block.SetVector(data.vectorIds[i], data.vectors[i]);
            if (data.hasMix)
                data.mix.Apply(block);
            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.pass, MeshTopology.Triangles, 3, 1, block);
            data.after?.Invoke(context);
        }
    }
}
