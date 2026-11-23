using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Art Style (Lite): Posterize &amp; Palette. Steps lightness, chroma and hue in OKLab (so bands look even), maps
    /// lightness onto a three-stop gradient, and snaps colours to a palette texture.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Looksmith/Art Styles/Posterize & Palette")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LooksmithPosterize : LooksmithStyle
    {
        [Tooltip("Lightness bands (0 = keep the image's lightness).")]
        public ClampedIntParameter lightnessLevels = new ClampedIntParameter(4, 0, 16);
        [Tooltip("Colourfulness steps (0 = keep).")]
        public ClampedIntParameter chromaLevels = new ClampedIntParameter(0, 0, 8);
        [Tooltip("Hue steps around the colour wheel (0 = keep).")]
        public ClampedIntParameter hueLevels = new ClampedIntParameter(0, 0, 24);
        [Tooltip("Soft edges between bands (0 = hard steps).")]
        public ClampedFloatParameter bandSoftness = new ClampedFloatParameter(0.15f, 0f, 1f);
        [Tooltip("Ordered dither across band edges.")]
        public ClampedFloatParameter dither = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Gradient map: lightness picks a colour from a three-stop ramp.")]
        public ClampedFloatParameter gradientMap = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Build the ramp from the Look's palette (Shadow → Accent → Key hues, dark to light) instead of the colours below.")]
        public BoolParameter gradientFromLook = new BoolParameter(true);
        [Tooltip("Gradient map colour for the darkest tones (when Gradient From Look is off).")]
        public ColorParameter gradientShadow = new ColorParameter(new Color(0.12f, 0.1f, 0.25f), false, false, true);
        [Tooltip("Gradient map colour for the mid-tones (when Gradient From Look is off).")]
        public ColorParameter gradientMid = new ColorParameter(new Color(0.75f, 0.4f, 0.4f), false, false, true);
        [Tooltip("Gradient map colour for the lightest tones (when Gradient From Look is off).")]
        public ColorParameter gradientLight = new ColorParameter(new Color(1f, 0.93f, 0.8f), false, false, true);

        [Tooltip("Palette: a small texture (up to 64 texels; point filter, no compression) whose colours the image snaps to.")]
        public Texture2DParameter palette = new Texture2DParameter(null);
        [Tooltip("How much the image snaps to the palette.")]
        public ClampedFloatParameter paletteAmount = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Ordered dither between neighbouring palette colours.")]
        public ClampedFloatParameter paletteDither = new ClampedFloatParameter(0.3f, 0f, 1f);

        public LooksmithPosterize() : base(200) { }

        public override LooksmithStyleRenderer CreateRenderer() => new LooksmithPosterizeRenderer();
    }

    sealed class LooksmithPosterizeRenderer : LooksmithStyleRenderer
    {
        Material m_Material;
        static readonly ProfilingSampler s_Sampler = new ProfilingSampler("Looksmith Posterize & Palette");

        public override bool Prepare()
        {
            if (m_Material != null)
                return true;
            var resources = LooksmithRuntimeResources.Instance;
            if (resources == null || resources.posterizeShader == null)
                return false;
            m_Material = CoreUtils.CreateEngineMaterial(resources.posterizeShader);
            return true;
        }

        public override TextureHandle Record(RenderGraph renderGraph, LooksmithStyle style, LooksmithStyleContext context, TextureHandle source)
        {
            var s = (LooksmithPosterize)style;
            var result = context.CreateColor(renderGraph, "_LooksmithPosterize");
            Texture palette = s.palette.value;
            bool hasPalette = palette != null && s.paletteAmount.value > 0.001f;
            var lut = hasPalette ? LooksmithPaletteLut.Get(renderGraph, palette, m_Material, 1) : TextureHandle.nullHandle;

            Vector3 shadow, mid, light;
            if (s.gradientFromLook.value && context.look.lookActive)
            {
                // The palette's hues on a full tonal range: Shadow dark, Accent in the middle, Key light (the roles
                // themselves are mostly mid-to-light colours, which would wash the ramp out).
                shadow = Stop(context.look.paletteLch[1], 0.28f, 1f);
                mid = Stop(context.look.paletteLch[3], 0.62f, 0.75f);
                light = Stop(context.look.paletteLch[0], 0.95f, 1f);
            }
            else
            {
                shadow = Linear(s.gradientShadow.value);
                mid = Linear(s.gradientMid.value);
                light = Linear(s.gradientLight.value);
            }

            using (var blit = LooksmithStyleBlit.Begin(renderGraph, "Looksmith Posterize & Palette", m_Material, 0, source, result, s_Sampler))
            {
                blit.Vector(LooksmithShaderIDs.Params[0], new Vector4(s.lightnessLevels.value, s.chromaLevels.value, s.hueLevels.value, s.bandSoftness.value))
                    .Vector(LooksmithShaderIDs.Params[1], new Vector4(s.dither.value, s.gradientMap.value, hasPalette ? s.paletteAmount.value : 0f, s.paletteDither.value))
                    .Vector(LooksmithShaderIDs.Params[2], Lab(shadow))
                    .Vector(LooksmithShaderIDs.Params[3], Lab(mid))
                    .Vector(LooksmithShaderIDs.Params[4], Lab(light))
                    .Vector(LooksmithShaderIDs.Params[5], new Vector4(context.size.x, context.size.y, 0f, 0f))
                    .Texture(LooksmithShaderIDs.PaletteLut, lut)
                    .Mix(context.Mix(style));
            }
            return result;
        }

        static Vector3 Linear(Color c) => new Vector3(c.linear.r, c.linear.g, c.linear.b);

        static Vector3 Stop(Vector3 lch, float lightness, float chroma) =>
            LooksmithColor.OklchToLinearInGamut(new Vector3(lightness, lch.y * chroma, lch.z));

        static Vector4 Lab(Vector3 linear) => LooksmithColor.LinearToOklab(linear);

        public override void Dispose() => CoreUtils.Destroy(m_Material);
    }
}
