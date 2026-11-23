using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Base of every Art Style override. Styles stack after URP's post-processing in <see cref="order"/> (lowest first),
    /// each mixed in by <see cref="intensity"/>; the Look's Finish always runs last. Add them to a Look Profile with
    /// Add Override > Looksmith > Art Styles. A style is off until its Intensity is above 0.
    /// </summary>
    [Serializable]
    public abstract class LooksmithStyle : VolumeComponent
    {
        [Tooltip("How strongly this style is applied. 0 = off.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Position in the style stack: lower runs first. The Look's Finish (grain, paper, vignette…) always runs last.")]
        public NoInterpIntParameter order;
        [Tooltip("Pro: rendering layers that get this style. Objects on other layers stay crisp (characters, props, world-space UI). Everything = all.")]
        public NoInterpIntParameter layers = new NoInterpIntParameter(-1);

        protected LooksmithStyle(int defaultOrder) => order = new NoInterpIntParameter(defaultOrder);

        /// <summary>True when the override is enabled and its Intensity is above 0.</summary>
        public virtual bool IsStyleActive() => active && intensity.value > 0.001f;

        /// <summary>Camera inputs this style reads with its current settings (requested per camera, never through the URP asset).</summary>
        public virtual ScriptableRenderPassInput inputs => ScriptableRenderPassInput.None;

        /// <summary>
        /// True when the style lays marks on the surfaces (paper, brush strokes): the stack then draws the wind's offsets
        /// (Looksmith Sway) so the marks ride swaying leaves.
        /// </summary>
        public virtual bool laysMarksOnSurfaces => false;

        /// <summary>Creates the object that records this style's render passes (one per style type, shared by cameras).</summary>
        public abstract LooksmithStyleRenderer CreateRenderer();
    }

    /// <summary>Records one style's passes into the render graph. Created by <see cref="LooksmithStyle.CreateRenderer"/>.</summary>
    public abstract class LooksmithStyleRenderer
    {
        /// <summary>Loads shaders and materials. False when they aren't available (a Lite build without this style).</summary>
        public abstract bool Prepare();

        /// <summary>
        /// Records the style reading <paramref name="source"/> and returns the texture holding the result, already mixed with the
        /// source by intensity and the layer mask (<see cref="LooksmithStyleContext.Mix"/>).
        /// </summary>
        public abstract TextureHandle Record(RenderGraph renderGraph, LooksmithStyle style, LooksmithStyleContext context, TextureHandle source);

        /// <summary>Releases materials and per-camera history.</summary>
        public virtual void Dispose() { }
    }

    /// <summary>A style drawn with one hidden shader from <see cref="LooksmithRuntimeResources"/>.</summary>
    public abstract class LooksmithShaderStyleRenderer : LooksmithStyleRenderer
    {
        protected Material material;

        protected abstract Shader GetShader(LooksmithRuntimeResources resources);

        public override bool Prepare()
        {
            if (material != null)
                return true;
            var resources = LooksmithRuntimeResources.Instance;
            var shader = resources != null ? GetShader(resources) : null;
            if (shader == null)
                return false;
            material = CoreUtils.CreateEngineMaterial(shader);
            return true;
        }

        public override void Dispose() => CoreUtils.Destroy(material);

        /// <summary>(1/width, 1/height, width, height) of a size.</summary>
        protected static Vector4 SizeVector(Vector2Int size) => new Vector4(1f / Mathf.Max(size.x, 1), 1f / Mathf.Max(size.y, 1), size.x, size.y);
    }

    /// <summary>Frame data the Pro mask pass fills for the style stack: the rendering-layer bits of every visible surface.</summary>
    public sealed class LooksmithStyleInputs : ContextItem
    {
        public TextureHandle mask;
        public bool hasMask;

        public override void Reset()
        {
            mask = TextureHandle.nullHandle;
            hasMask = false;
        }
    }

    /// <summary>How a style's last pass blends with what was under it: intensity, and the rendering-layer mask (Pro).</summary>
    public struct LooksmithStyleMix
    {
        /// <summary>x intensity, y 1 = mask bound, zw mask size in pixels.</summary>
        public Vector4 parameters;
        public int layers;
        public TextureHandle mask;
        /// <summary>x 1 = the sky keeps its rendered look, y counts as sky beyond (m); with depth to tell it apart.</summary>
        public Vector4 sky;
        public TextureHandle depth;

        public void Use(IBaseRenderGraphBuilder builder)
        {
            if (mask.IsValid())
                builder.UseTexture(mask);
            if (depth.IsValid())
                builder.UseTexture(depth);
        }

        public void Apply(MaterialPropertyBlock block)
        {
            block.SetVector(LooksmithShaderIDs.StyleMix, parameters);
            block.SetInteger(LooksmithShaderIDs.StyleLayers, layers);
            if (mask.IsValid())
                block.SetTexture(LooksmithShaderIDs.StyleMask, mask);
            bool keepSky = sky.x > 0.5f && depth.IsValid();
            block.SetVector(LooksmithShaderIDs.MixSky, keepSky ? sky : Vector4.zero);
            if (keepSky)
                block.SetTexture(LooksmithShaderIDs.MixDepth, depth);
        }
    }

    /// <summary>Per-camera information handed to every style while the stack is recorded.</summary>
    public sealed class LooksmithStyleContext
    {
        public ContextContainer frameData;
        public UniversalResourceData resources;
        public UniversalCameraData cameraData;
        public LooksmithLookState look;
        /// <summary>Description of the colour target styles read and write (the camera colour after post-processing).</summary>
        public TextureDesc colorDesc;
        /// <summary>Target height / 1080: sizes authored in pixels at 1080p scale by this.</summary>
        public float scale;
        public TextureHandle mask;
        public Vector2 maskSize;
        /// <summary>
        /// The wind offsets of swaying surfaces this frame (Looksmith Sway), for styles that lay marks on the surfaces; invalid
        /// when no style needs them.
        /// </summary>
        public TextureHandle sway;

        public Camera camera => cameraData.camera;

        /// <summary>Pixel size of the colour target.</summary>
        public Vector2Int size => new Vector2Int(colorDesc.width, colorDesc.height);

        /// <summary>
        /// The camera's own depth, when it differs from the depth texture styles read (the depth-normals prepass leaves
        /// some things out, like a grass field, so their blades aren't outlined): outline styles test against it so an
        /// edge hidden behind them isn't drawn over them. Invalid when there's no separate, single-sampled one.
        /// </summary>
        public TextureHandle OccluderDepth(RenderGraph renderGraph)
        {
            var d = resources.activeDepthTexture;
            if (!d.IsValid() || resources.isActiveTargetBackBuffer || d.Equals(resources.cameraDepthTexture))
                return TextureHandle.nullHandle;
            return renderGraph.GetTextureDesc(d).msaaSamples == MSAASamples.None ? d : TextureHandle.nullHandle;
        }

        /// <summary>A colour texture like the camera's (for a style's intermediate or result), optionally 1/divisor the size.</summary>
        public TextureHandle CreateColor(RenderGraph renderGraph, string name, int divisor = 1, GraphicsFormatChoice format = GraphicsFormatChoice.Camera)
        {
            var desc = colorDesc;
            desc.name = name;
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.sizeMode = TextureSizeMode.Explicit;
            if (divisor > 1)
            {
                desc.width = Mathf.Max(1, (desc.width + divisor - 1) / divisor);
                desc.height = Mathf.Max(1, (desc.height + divisor - 1) / divisor);
            }
            return Create(renderGraph, desc, format);
        }

        /// <summary>A colour texture of an exact size (low-resolution styles).</summary>
        public TextureHandle CreateColor(RenderGraph renderGraph, string name, Vector2Int pixels, GraphicsFormatChoice format = GraphicsFormatChoice.Camera)
        {
            var desc = colorDesc;
            desc.name = name;
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.sizeMode = TextureSizeMode.Explicit;
            desc.width = Mathf.Max(1, pixels.x);
            desc.height = Mathf.Max(1, pixels.y);
            return Create(renderGraph, desc, format);
        }

        static TextureHandle Create(RenderGraph renderGraph, TextureDesc desc, GraphicsFormatChoice format)
        {
            if (format == GraphicsFormatChoice.Half)
                desc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            desc.filterMode = FilterMode.Bilinear;
            desc.wrapMode = TextureWrapMode.Clamp;
            return renderGraph.CreateTexture(desc);
        }

        /// <summary>The intensity/mask blend for a style's final pass.</summary>
        public LooksmithStyleMix Mix(LooksmithStyle style)
        {
            bool masked = mask.IsValid() && style.layers.value != -1;
            bool keepSky = look != null && look.lookActive && look.finish.keepSky && resources.cameraDepthTexture.IsValid();
            return new LooksmithStyleMix
            {
                parameters = new Vector4(style.intensity.value, masked ? 1f : 0f, maskSize.x, maskSize.y),
                layers = style.layers.value,
                mask = masked ? mask : TextureHandle.nullHandle,
                sky = keepSky ? new Vector4(1f, look.finish.skyDistance, 0f, 0f) : Vector4.zero,
                depth = keepSky ? resources.cameraDepthTexture : TextureHandle.nullHandle,
            };
        }
    }

    public enum GraphicsFormatChoice
    {
        /// <summary>The camera colour's own format.</summary>
        Camera,
        /// <summary>RGBA half float (structure tensors, history).</summary>
        Half,
    }
}
