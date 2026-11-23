using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The Look: one Volume override that drives lighting, colour, lines, atmosphere and finish for every Looksmith
    /// shader. Macro sliders (0.5 = neutral) sit on top of the advanced values and are what most people touch.
    /// It blends like any Volume override, by area (Look Zones) or over time.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Looksmith/Look")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LooksmithLook : VolumeComponent
    {
        [Tooltip("Style cameras that see this Volume. Off = Looksmith materials render as plain PBR.")]
        public BoolParameter style = new BoolParameter(false);

        // ------------------------------------------------------------------ Light
        [Header("Light")]
        [Tooltip("Macro: softer edges and wider wrap (left) or crisper bands (right is harder). 0.5 = as authored.")]
        public ClampedFloatParameter softness = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Macro: how far shadows drop below the light. 0.5 = as authored.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Macro: warms the key light and cools the shadows (or the reverse). 0.5 = as authored.")]
        public ClampedFloatParameter warmth = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Macro: rim light amount. 0.5 = as authored.")]
        public ClampedFloatParameter rim = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Macro: specular amount. 0.5 = as authored.")]
        public ClampedFloatParameter shine = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Number of lit bands above the shadow band (1 = two-tone cel).")]
        public ClampedIntParameter bands = new ClampedIntParameter(2, 1, 5);
        [Tooltip("0 = smooth wrapped falloff, 1 = hard bands.")]
        public ClampedFloatParameter bandMix = new ClampedFloatParameter(0.8f, 0f, 1f);
        [Tooltip("Width of each band edge, in N·L units.")]
        public ClampedFloatParameter edgeSoftness = new ClampedFloatParameter(0.04f, 0.002f, 0.4f);
        [Tooltip("Moves the light/shadow line toward the light (negative) or away (positive).")]
        public ClampedFloatParameter terminator = new ClampedFloatParameter(0f, -0.6f, 0.6f);
        [Tooltip("How far light wraps past the terminator (Soft Wrap).")]
        public ClampedFloatParameter wrap = new ClampedFloatParameter(0.2f, 0f, 1f);
        [Tooltip("How far up the lit side the extra bands spread.")]
        public ClampedFloatParameter bandSpread = new ClampedFloatParameter(0.7f, 0.2f, 1f);
        [Tooltip("Gradient Ramp: blend toward a three-colour ramp indexed by light amount.")]
        public ClampedFloatParameter gradientRamp = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Gradient Ramp colour where the surface is in shadow.")]
        public ColorParameter gradientShadow = new ColorParameter(new Color(0.25f, 0.25f, 0.45f), false, false, true);
        [Tooltip("Gradient Ramp colour across the light/shadow line.")]
        public ColorParameter gradientMid = new ColorParameter(new Color(0.8f, 0.55f, 0.5f), false, false, true);
        [Tooltip("Gradient Ramp colour where the surface faces the light.")]
        public ColorParameter gradientLit = new ColorParameter(new Color(1f, 0.95f, 0.85f), false, false, true);
        [Tooltip("Painted: breaks the terminator up with a brush-stroke texture.")]
        public ClampedFloatParameter painted = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Brush texture scale (strokes per metre).")]
        public ClampedFloatParameter brushScale = new ClampedFloatParameter(0.35f, 0.02f, 4f);
        [Tooltip("Flat shading: each face lit as one plane (the low-poly look on any mesh). 0 = smooth normals.")]
        public ClampedFloatParameter facets = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("PBR Blend: mix toward URP's physically based lighting.")]
        public ClampedFloatParameter pbrBlend = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Brightness of the lit band relative to the light's intensity.")]
        public ClampedFloatParameter keyLevel = new ClampedFloatParameter(0.85f, 0.2f, 1.6f);
        [Tooltip("How much the palette's Key colour tints the sun (0 keeps the light's own colour).")]
        public ClampedFloatParameter keyTint = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Hardens shadow-map edges to the band edge.")]
        public ClampedFloatParameter shadowHardness = new ClampedFloatParameter(0.8f, 0f, 1f);
        [Tooltip("Terminator hue shift toward the Accent colour where light turns to shadow.")]
        public ClampedFloatParameter terminatorAccent = new ClampedFloatParameter(0.2f, 0f, 1f);
        [Tooltip("Brightness of the rim light around silhouettes.")]
        public ClampedFloatParameter rimStrength = new ClampedFloatParameter(0.4f, 0f, 2f);
        [Tooltip("How far the rim reaches in from the silhouette.")]
        public ClampedFloatParameter rimWidth = new ClampedFloatParameter(0.3f, 0.02f, 0.9f);
        [Tooltip("0 = a crisp rim band, higher = a soft glow.")]
        public ClampedFloatParameter rimSoftness = new ClampedFloatParameter(0.1f, 0.001f, 0.6f);
        [Tooltip("0 = rim all around (fresnel), 1 = only on the side facing the light.")]
        public ClampedFloatParameter rimLightFacing = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("Rim colour: 0 = a pale key colour, 1 = the palette's Accent.")]
        public ClampedFloatParameter rimFromAccent = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Stepped: a crisp cartoon highlight shape. Blinn: a smooth highlight. Anisotropic Hair: streaks along the hair.")]
        public SpecularStyleParameter specularStyle = new SpecularStyleParameter(LooksmithSpecularStyle.Stepped);
        [Tooltip("Brightness of the specular highlight (materials scale it with Specular Amount and Smoothness).")]
        public ClampedFloatParameter specularStrength = new ClampedFloatParameter(0.4f, 0f, 2f);
        [Tooltip("Size of the highlight.")]
        public ClampedFloatParameter specularSize = new ClampedFloatParameter(0.4f, 0f, 1f);
        [Tooltip("Edge softness of the highlight (0 = crisp).")]
        public ClampedFloatParameter specularSoftness = new ClampedFloatParameter(0.08f, 0.001f, 0.6f);
        [Tooltip("Ambient light (light probes / ambient probe) strength.")]
        public ClampedFloatParameter ambient = new ClampedFloatParameter(0.35f, 0f, 2f);
        [Tooltip("Flattens directional ambient toward its average (stylized, painterly ambient).")]
        public ClampedFloatParameter ambientFlatten = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("Pulls ambient toward the palette's Sky colour.")]
        public ClampedFloatParameter ambientFromPalette = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Reflection probe strength on smooth surfaces.")]
        public ClampedFloatParameter reflections = new ClampedFloatParameter(0.2f, 0f, 1f);
        [Tooltip("Intensity of additional (point/spot) lights through the ramp.")]
        public ClampedFloatParameter additionalLights = new ClampedFloatParameter(1f, 0f, 3f);

        // ------------------------------------------------------------------ Colour (OKLCH: lightness, chroma, hue)
        [Header("Colour")]
        [Tooltip("Macro: surface saturation (1 = as authored).")]
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1f, 0f, 2f);
        [Tooltip("Palette: the colour of the light. Tints the sun by Key Tint.")]
        public OklchParameter keyColor = OklchParameter.FromHex("#FFE2B3");
        [Tooltip("Palette: the colour shadows take, relative to the light (usually cooler and darker than the key).")]
        public OklchParameter shadowColor = OklchParameter.FromHex("#7A86C2");
        [Tooltip("Palette: tints ambient light (Ambient From Palette), keeping the environment's brightness.")]
        public OklchParameter skyColor = OklchParameter.FromHex("#A8D8EA");
        [Tooltip("Palette: an accent for the light/shadow line, rims and gradient maps.")]
        public OklchParameter accentColor = OklchParameter.FromHex("#E86F5C");
        [Tooltip("Palette: the colour distant things fade into (aerial perspective).")]
        public OklchParameter fogColor = OklchParameter.FromHex("#C9D8E0");

        // ------------------------------------------------------------------ Lines (drawn by the Outlines module)
        [Header("Lines")]
        [Tooltip("Macro: line weight in pixels at 1080p (0 = no lines).")]
        public ClampedFloatParameter lineWeight = new ClampedFloatParameter(0f, 0f, 4f);
        [Tooltip("Line colour (blended with a dark shade of the surface by Line From Surface).")]
        public ColorParameter lineColor = new ColorParameter(new Color(0.12f, 0.1f, 0.14f), false, false, true);
        [Tooltip("0 = fixed line colour, 1 = a darker, more saturated shade of the surface.")]
        public ClampedFloatParameter lineFromSurface = new ClampedFloatParameter(0.7f, 0f, 1f);
        [Tooltip("Only outer silhouettes (depth edges); no crease lines inside shapes.")]
        public BoolParameter silhouettesOnly = new BoolParameter(false);
        [Tooltip("How readily depth changes draw lines (silhouettes, overlaps).")]
        public ClampedFloatParameter lineDepthSensitivity = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("How readily creases (changes in surface direction) draw lines.")]
        public ClampedFloatParameter lineNormalSensitivity = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Pro: lines where colours meet (colour stripes, texture borders).")]
        public ClampedFloatParameter lineColorSensitivity = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Pro: a line wherever one object meets another, even when they touch at the same depth.")]
        public BoolParameter lineObjectEdges = new BoolParameter(false);
        [Tooltip("Pro: rendering layers that get lines; objects on other layers stay line-free (characters, UI props…).")]
        public NoInterpIntParameter lineLayerMask = new NoInterpIntParameter(-1);
        [Tooltip("Lines start fading at this distance (m)…")]
        public MinFloatParameter lineFadeStart = new MinFloatParameter(40f, 0f);
        [Tooltip("…and are gone at this distance (m).")]
        public MinFloatParameter lineFadeEnd = new MinFloatParameter(160f, 0f);
        [Tooltip("Heavier lines in shadow, lighter in light (an ink-drawing habit).")]
        public ClampedFloatParameter lineLightWeight = new ClampedFloatParameter(0.3f, 0f, 1f);
        [Tooltip("Width variation along lines, like a pen's (stable in the world, doesn't swim).")]
        public ClampedFloatParameter lineJitter = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Wobble: lines drift a little off the exact edge, like a sketch (stable in the world).")]
        public ClampedFloatParameter lineWobble = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Pro: inverted-hull outline in pixels at 1080p for objects on the Looksmith outline rendering layer (Looksmith Outline component). 0 = off.")]
        public ClampedFloatParameter outlineWidth = new ClampedFloatParameter(0f, 0f, 8f);

        // ------------------------------------------------------------------ Atmosphere
        [Header("Atmosphere")]
        [Tooltip("Macro: aerial perspective amount. 0.5 = as authored.")]
        public ClampedFloatParameter haze = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Haze starts this far from the camera (m).")]
        public MinFloatParameter hazeStart = new MinFloatParameter(20f, 0f);
        [Tooltip("How quickly haze thickens with distance.")]
        public ClampedFloatParameter hazeDensity = new ClampedFloatParameter(0.015f, 0f, 0.3f);
        [Tooltip("How quickly haze thins with height above Haze Height Base (0 = the same at every height).")]
        public ClampedFloatParameter hazeHeightFalloff = new ClampedFloatParameter(0.06f, 0f, 1f);
        [Tooltip("The most haze anything gets, however far away (keeps distant shapes readable).")]
        public ClampedFloatParameter hazeMax = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("How much colour distant things lose as they fade.")]
        public ClampedFloatParameter hazeDesaturation = new ClampedFloatParameter(0.4f, 0f, 1f);
        [Tooltip("World height (m) where haze is thickest; it thins above it.")]
        public FloatParameter hazeHeightBase = new FloatParameter(0f);
        [Tooltip("With a Looksmith Sky in the scene: 1 = haze takes the sky's horizon colour, 0 = the palette's Fog colour at the sky's brightness.")]
        public ClampedFloatParameter hazeFromSky = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Requests the depth texture so Looksmith particles fade softly into geometry.")]
        public BoolParameter softParticles = new BoolParameter(false);

        // ------------------------------------------------------------------ Finish (Art Styles) and vertex hooks
        [Header("Finish")]
        [Tooltip("Macro: film grain (drawn by Art Styles > Finish).")]
        public ClampedFloatParameter grain = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Film grain size, in pixels at 1080p.")]
        public ClampedFloatParameter grainSize = new ClampedFloatParameter(1.2f, 0.5f, 4f);
        [Tooltip("Darkens the frame edges (drawn by Art Styles > Finish).")]
        public ClampedFloatParameter vignette = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Vignette colour: 0 = black, 1 = the palette's Shadow colour.")]
        public ClampedFloatParameter vignetteFromPalette = new ClampedFloatParameter(0.6f, 0f, 1f);
        [Tooltip("Paper overlay: the image sits on paper with visible grain and fibres (drawn by Art Styles > Finish).")]
        public ClampedFloatParameter paper = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Paper texture (red = surface height). Empty = Looksmith's built-in paper.")]
        public Texture2DParameter paperTexture = new Texture2DParameter(null);
        [Tooltip("Paper size: 1 = one paper texel per screen pixel at 1080p; larger = coarser paper.")]
        public ClampedFloatParameter paperScale = new ClampedFloatParameter(1f, 0.25f, 6f);
        [Tooltip("On: the paper grain sits on the surfaces (it moves with the scene, like a texture on the models; the sky keeps a flat sheet). Off: one flat sheet over the screen.")]
        public BoolParameter paperOnSurfaces = new BoolParameter(true);
        [Tooltip("Chromatic fringe: a lens-like colour split toward the frame edges.")]
        public ClampedFloatParameter chromaticFringe = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Grading LUT in URP's strip layout (for example 1024×32). Empty = none.")]
        public Texture2DParameter gradingLut = new Texture2DParameter(null);
        [Tooltip("How much of the grading LUT is applied.")]
        public ClampedFloatParameter gradingLutContribution = new ClampedFloatParameter(1f, 0f, 1f);
        [Header("Sky")]
        [Tooltip("The Art Styles paint the sky too (on), or leave it as it's rendered (off: only the scene's surfaces are styled; the sky keeps its own gradient, clouds and stars).")]
        public BoolParameter styleSky = new BoolParameter(true);
        [Tooltip("With Style Sky off: anything farther than this (m) is kept as sky too, for skies drawn as a dome or far backdrops. 0 = only where nothing was drawn.")]
        public MinFloatParameter skyDistance = new MinFloatParameter(0f, 0f);
        [Tooltip("Retro: snaps vertices to a coarse pixel grid (pixels; 0 = off).")]
        public ClampedFloatParameter vertexSnap = new ClampedFloatParameter(0f, 0f, 8f);
        [Tooltip("Retro: affine (non-perspective) texture warp, drawn by Art Styles > Retro.")]
        public ClampedFloatParameter affineWarp = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>True when this Look styles cameras (the override is enabled and Style is on).</summary>
        public bool IsActive() => active && style.value;
    }
}
