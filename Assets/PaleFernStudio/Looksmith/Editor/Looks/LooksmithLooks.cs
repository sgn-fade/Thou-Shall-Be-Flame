using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The shipped looks (design doc "Shipped looks", plus Oil Painting, Silent Fog and Analog Horror from the polish
    /// round), from cozy to childish to creepy: one definition each, written into Look Profiles (a Volume Profile with
    /// the Looksmith Look plus the URP grading the look relies on).
    /// </summary>
    public static class LooksmithLooks
    {
        public sealed class Def
        {
            public string name;
            public string key, shadow, sky, accent, fog;              // palette roles (hex sRGB)
            public int bands = 2;
            public float bandMix = 0.8f, edge = 0.04f, terminator, wrap = 0.2f, spread = 0.7f;
            public float painted, brushScale = 0.35f, pbrBlend, keyLevel = 0.85f, keyTint = 1f, shadowHardness = 0.8f, accentAmt = 0.2f, facets;
            public float rimStrength = 0.4f, rimWidth = 0.3f, rimSoftness = 0.1f, rimFacing = 0.6f, rimAccent;
            public LooksmithSpecularStyle specStyle = LooksmithSpecularStyle.Stepped;
            public float specStrength = 0.3f, specSize = 0.4f, specSoftness = 0.08f;
            public float ambient = 0.35f, ambientFlatten = 0.6f, ambientPalette = 0.5f, reflections = 0.15f, addLights = 1f;
            public float saturation = 1f;
            public float lineWeight, lineFromSurface = 0.7f;
            public bool silhouettesOnly;
            public float lineDepth = 0.5f, lineNormal = 0.5f, lineLight = 0.3f, lineJitter, lineWobble, outlineWidth;
            public float lineFadeStart = 40f, lineFadeEnd = 160f;
            public string lineColor = "#1F1A24";
            public float hazeStart = 20f, hazeDensity = 0.012f, hazeFalloff = 0.05f, hazeMax = 0.45f, hazeDesat = 0.4f, hazeFromSky = 0.5f;
            public float grain, vignette, paper, vertexSnap, affine;
            public bool paperOnSurfaces = true;
            public float vignetteFromPalette = 0.6f, fringe;
            public float gradientRamp;
            public int posterLevels;                                      // > 0: a Posterize (Lite style) with these steps
            public float posterSoftness = 0.3f;
            public Color gradShadow = new Color(0.25f, 0.25f, 0.45f), gradMid = new Color(0.8f, 0.55f, 0.5f), gradLit = new Color(1f, 0.95f, 0.85f);
            // URP grading the look relies on
            public TonemappingMode tonemap = TonemappingMode.Neutral;
            public float bloom, bloomThreshold = 1f, postExposure, contrast, gradeSaturation, urpVignette, filmGrain, chromatic;
            public string description;
            public bool lite;                                             // ships in Looksmith Lite (the rest show there, locked)

            public Def Clone() => (Def)MemberwiseClone();
        }

        /// <summary>
        /// A variant of a shipped look for a kind of game or scene (presets of presets): the same look tuned for night,
        /// for realistic assets, for low-poly games or for cozy ones, plus a few look-specific ones (Analog Horror without
        /// its rolling tracking band, Lo-Fi Horror without the pixels). Written as its own read-only profile.
        /// </summary>
        public sealed class Variant
        {
            public string look;      // the shipped look it varies
            public string kind;      // Night, Realistic, Low Poly, Cozy, or a look-specific one
            public string description;
            public string name => $"{look} ({kind})";
        }

        public const string Night = "Night", Realistic = "Realistic", LowPoly = "Low Poly", Cozy = "Cozy";
        public const string NoPixels = "No Pixels", CleanTape = "Clean Tape", NoTape = "No Tape", NoCrt = "No CRT";
        public const string Poster = "Poster", Paper = "Paper";
        static readonly string[] k_Horror = { "Dark Fable", "Silent Fog", "Lo-Fi Horror", "Analog Horror", "Retro 32-bit" };

        static Variant[] s_Variants;
        public static Variant[] Variants => s_Variants ??= BuildVariants();   // after All (static fields initialise in order)

        static Variant[] BuildVariants()
        {
            var list = new List<Variant>();
            foreach (var d in All)
            {
                list.Add(new Variant { look = d.name, kind = Night, description = "For night and dark interiors: brighter ambient, lighter shadows and a lift in exposure, so the dark stays readable." });
                if (d.name != "Stylized Realism")
                    list.Add(new Variant { look = d.name, kind = Realistic, description = "For detailed, realistic assets: their materials and light come through, with the look laid on more gently." });
                list.Add(new Variant { look = d.name, kind = LowPoly, description = "For low-poly and flat-coloured games: crisp facets, clean bands, outlines that follow the shapes." });
                if (!k_Horror.Contains(d.name))
                    list.Add(new Variant { look = d.name, kind = Cozy, description = "Warm and soft for cozy games: honey light, lavender shade, a gentle glow." });
            }
            list.Add(new Variant { look = "Lo-Fi Horror", kind = NoPixels, description = "The full-resolution image: posterized light, grain and the tape, without the chunky pixels." });
            list.Add(new Variant { look = "Analog Horror", kind = CleanTape, description = "The tape without the tracking band rolling up the frame." });
            list.Add(new Variant { look = "Analog Horror", kind = NoTape, description = "The grade, grain and fog without the VHS tape at all." });
            list.Add(new Variant { look = "Retro 32-bit", kind = NoCrt, description = "The 32-bit pixels and colour without the CRT screen (no curvature or scanlines)." });
            // two that show Lite's own Art Styles on Lite's looks: Posterize, and the paper on the surfaces
            list.Add(new Variant { look = "Stylized Realism", kind = Poster, description = "Flat poster colour: the light in a few clean steps, inked edges and a touch of paper, like a printed travel poster." });
            list.Add(new Variant { look = "Cel Anime", kind = Paper, description = "A printed illustration: the paper's grain on every surface, moving with the world as you walk." });
            return list.ToArray();
        }

        public static Variant FindVariant(string name) => Variants.FirstOrDefault(v => v.name == name);

        /// <summary>The variants of a shipped look, in the gallery's order.</summary>
        public static IEnumerable<Variant> VariantsOf(string look) => Variants.Where(v => v.look == look);

        /// <summary>A variant's definition: its look's, tuned.</summary>
        public static Def VariantDef(Variant v)
        {
            var d = Find(v.look).Clone();
            switch (v.kind)
            {
                case Night:
                    // a night's ambient is tiny, so exposure does most of the lifting (like the Advisor's "too dark")
                    d.ambient = Mathf.Max(d.ambient + 0.25f, 0.65f);
                    d.keyLevel = Mathf.Min(1f, d.keyLevel + 0.08f);
                    d.postExposure += 1.1f;
                    d.shadow = Tweak(d.shadow, 0.12f);
                    d.hazeMax *= 0.7f;
                    d.hazeDensity *= 0.8f;
                    d.vignette *= 0.6f;
                    d.urpVignette *= 0.6f;
                    d.grain *= 0.8f;
                    if (d.posterLevels > 0)
                        d.posterLevels += 2;
                    d.bloom = Mathf.Max(d.bloom, 0.3f);
                    d.bloomThreshold = Mathf.Min(d.bloomThreshold, 0.95f);
                    break;
                case Realistic:
                    d.pbrBlend = Mathf.Max(d.pbrBlend, 0.6f);
                    d.bandMix *= 0.5f;
                    d.edge = Mathf.Max(d.edge, 0.15f);
                    d.saturation = Mathf.Lerp(d.saturation, 1f, 0.6f);
                    d.keyTint *= 0.5f;
                    d.ambientPalette *= 0.5f;
                    d.rimStrength *= 0.5f;
                    if (d.specStyle == LooksmithSpecularStyle.Stepped)
                        d.specStyle = LooksmithSpecularStyle.Blinn;
                    d.painted *= 0.5f;
                    d.gradientRamp *= 0.4f;
                    d.facets = 0f;
                    d.lineWeight *= 0.6f;
                    d.lineFromSurface *= 0.4f;
                    d.paper *= 0.4f;
                    d.grain *= 0.6f;
                    d.contrast *= 0.7f;
                    d.gradeSaturation *= 0.6f;
                    break;
                case LowPoly:
                    d.facets = 1f;
                    d.edge = Mathf.Min(d.edge, 0.03f);
                    d.bandMix = Mathf.Max(d.bandMix, 0.9f);
                    d.pbrBlend = 0f;
                    d.reflections = 0f;
                    d.specStrength *= 0.5f;
                    d.saturation *= 1.06f;
                    d.ambientFlatten = 1f;
                    d.painted *= 0.5f;
                    d.lineWeight = Mathf.Max(d.lineWeight, 1.5f);
                    d.lineFromSurface = 0f;
                    d.lineNormal = Mathf.Max(d.lineNormal, 0.5f);
                    break;
                case Cozy:
                    d.key = Tweak(d.key, 0.02f, 75f, 0.6f, 0.02f);
                    d.shadow = Tweak(d.shadow, 0.08f, 295f, 0.5f);
                    d.accent = Tweak(d.accent, 0f, 45f, 0.4f);
                    d.edge = Mathf.Max(d.edge, 0.1f);
                    d.bandMix *= 0.8f;
                    d.ambient += 0.08f;
                    d.ambientPalette = Mathf.Max(d.ambientPalette, 0.6f);
                    d.bloom = Mathf.Max(d.bloom, 0.35f);
                    d.bloomThreshold = Mathf.Min(d.bloomThreshold, 0.9f);
                    d.contrast -= 4f;
                    d.gradeSaturation += 6f;
                    d.postExposure += 0.1f;
                    d.vignette *= 0.5f;
                    d.grain = 0f;
                    d.rimAccent = Mathf.Max(d.rimAccent, 0.3f);
                    break;
                case Poster:
                    d.posterLevels = 7;
                    d.posterSoftness = 0.22f;
                    d.pbrBlend *= 0.5f;
                    d.bandMix = Mathf.Max(d.bandMix, 0.6f);
                    d.saturation *= 1.08f;
                    d.gradeSaturation += 6f;
                    d.contrast += 4f;
                    d.lineWeight = Mathf.Max(d.lineWeight, 1.4f);
                    d.lineFromSurface = Mathf.Max(d.lineFromSurface, 0.5f);
                    d.paper = Mathf.Max(d.paper, 0.15f);
                    d.grain = 0f;
                    break;
                case Paper:
                    d.paper = Mathf.Max(d.paper, 0.45f);
                    d.paperOnSurfaces = true;
                    d.vignette = Mathf.Max(d.vignette, 0.2f);
                    break;
            }
            d.description = v.description;
            return d;
        }

        /// <summary>A palette colour moved in OKLCH: lightness, toward a hue (by a share of the way), chroma.</summary>
        static string Tweak(string hex, float dL, float hue = 0f, float toHue = 0f, float dC = 0f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            var lch = LooksmithColor.ColorToOklch(c);
            lch.x = Mathf.Clamp01(lch.x + dL);
            lch.y = Mathf.Max(0f, lch.y + dC);
            if (toHue > 0f)
                lch.z = Mathf.Repeat(lch.z + LooksmithColor.HueDelta(lch.z, hue) * toHue, 360f);
            return "#" + ColorUtility.ToHtmlStringRGB(LooksmithColor.OklchToColor(lch));
        }

        public static readonly Def[] All =
        {
            // The natural one: PBR with a soft stylized ramp; the reference the others push away from.
            new Def
            {
                lite = true, name = "Stylized Realism", description = "Hero shooters, open world. PBR with a soft ramp blend, palette-tinted ambient, a clean filmic grade.",
                key = "#FFF1DA", shadow = "#4F6A8F", sky = "#8EC5E8", accent = "#F2B134", fog = "#C8DCEA",
                bands = 3, bandMix = 0.2f, edge = 0.2f, wrap = 0.2f, pbrBlend = 0.55f, keyLevel = 0.9f, accentAmt = 0.12f,
                rimStrength = 0.2f, rimSoftness = 0.3f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.6f, specSize = 0.5f,
                ambient = 0.45f, ambientFlatten = 0.4f, ambientPalette = 0.35f, reflections = 0.35f, hazeStart = 20f, hazeDensity = 0.012f,
                hazeMax = 0.5f, hazeDesat = 0.4f, tonemap = TonemappingMode.ACES, postExposure = 0.25f, bloom = 0.25f, contrast = 6f,
                gradeSaturation = 4f,
            },
            // A gouache picture book: soft pastel bands through a warm-to-lavender colour ramp, soft wobbly outlines round
            // the shapes, a touch of pigment at the edges.
            new Def
            {
                name = "Storybook", description = "Cozy adventure, pastoral, family. A gouache picture book: pastel light ramped from cream to lavender, soft wobbly outlines.",
                lineWobble = 0.35f, lineJitter = 0.3f, lineLight = 0.4f, lineColor = "#3E2C3A", lineFromSurface = 0.45f,
                lineDepth = 0.6f, lineNormal = 0.45f,
                key = "#FFE6BC", shadow = "#8E86C0", sky = "#B4DCEC", accent = "#EE8A72", fog = "#E2E6F0",
                bands = 3, bandMix = 0.7f, edge = 0.09f, wrap = 0.35f, spread = 0.75f, keyLevel = 0.86f, accentAmt = 0.25f,
                gradientRamp = 0.35f, gradShadow = new Color(0.42f, 0.4f, 0.66f), gradMid = new Color(0.93f, 0.66f, 0.6f), gradLit = new Color(1f, 0.95f, 0.84f),
                rimStrength = 0.25f, rimWidth = 0.35f, rimSoftness = 0.25f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.12f,
                ambient = 0.4f, ambientFlatten = 0.8f, ambientPalette = 0.65f, saturation = 1f, hazeStart = 30f, hazeDensity = 0.008f, hazeMax = 0.35f,
                lineWeight = 2.2f, paper = 0.18f, postExposure = 0.1f, gradeSaturation = 4f, contrast = 4f,
            },
            // Oil on canvas: the image broken into paint patches that follow the forms, bristle strokes with impasto on top.
            new Def
            {
                name = "Oil Painting", description = "Painterly adventures, fairy tales, art-house. Thick oil paint: patches that follow the forms, bristle strokes with impasto, warm glazed light.",
                key = "#FFE6B8", shadow = "#56607E", sky = "#9CC3DE", accent = "#E08A4A", fog = "#D9D2BF",
                bands = 3, bandMix = 0.35f, edge = 0.15f, wrap = 0.25f, painted = 0.6f, brushScale = 0.4f, keyLevel = 0.9f, accentAmt = 0.3f,
                rimStrength = 0.2f, rimSoftness = 0.3f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.2f,
                ambient = 0.42f, ambientFlatten = 0.6f, ambientPalette = 0.5f, saturation = 0.95f, hazeStart = 20f, hazeDensity = 0.012f, hazeMax = 0.45f,
                bloom = 0.2f, gradeSaturation = -4f, contrast = 8f, postExposure = 0.05f,
                paper = 0.25f,   // the canvas's grain on the surfaces: what you see walking up to a wall
            },
            // The European clear line (Tintin, Moebius): flat, slightly pastel fills, one clean step of shade with a cool
            // tint, an even thin outline round every shape, airy distance, a touch of print paper. Nothing glossy or rim-lit.
            new Def
            {
                name = "Ligne Claire", description = "Adventure, exploration, mystery, puzzle. The clear-line comic: flat pastel colour, one clean step of cool shade, an even thin outline round every shape.",
                lineDepth = 0.7f, lineNormal = 0.55f, lineLight = 0f, lineJitter = 0f, lineWobble = 0f, lineFromSurface = 0f, lineColor = "#2A2530",
                lineFadeStart = 45f, lineFadeEnd = 160f, lineWeight = 2.2f,
                key = "#FFF3DE", shadow = "#7F90BC", sky = "#A8D3EE", accent = "#E8734C", fog = "#E3ECF1",
                bands = 1, bandMix = 1f, edge = 0.03f, terminator = 0.03f, wrap = 0.12f, keyLevel = 0.95f, shadowHardness = 0.9f, accentAmt = 0.06f,
                rimStrength = 0f, specStyle = LooksmithSpecularStyle.Stepped, specStrength = 0.04f, specSoftness = 0.02f,
                ambient = 0.5f, ambientFlatten = 1f, ambientPalette = 0.8f, saturation = 0.92f,
                hazeStart = 30f, hazeDensity = 0.01f, hazeMax = 0.45f, hazeDesat = 0.25f, hazeFromSky = 0.6f,
                paper = 0.1f, postExposure = 0.15f,
            },
            // Character anime: two hard tones with a cool, saturated shadow, crisp ink line art, rim and stepped highlights.
            new Def
            {
                lite = true, name = "Cel Anime", description = "Action RPG, JRPG, anime. Hard two-tone light with cool shadows, crisp line art, rim light, specular streaks.",
                lineDepth = 0.8f, lineNormal = 0.7f, lineLight = 0.15f, lineFromSurface = 0.2f, outlineWidth = 3.5f, lineColor = "#16131F",
                lineFadeStart = 30f, lineFadeEnd = 110f,
                key = "#FFF6E6", shadow = "#5A62A8", sky = "#62A6E8", accent = "#FF5D73", fog = "#BFDDF7",
                bands = 1, bandMix = 1f, edge = 0.01f, terminator = 0.05f, wrap = 0f, keyLevel = 0.95f, shadowHardness = 1f, accentAmt = 0.12f,
                rimStrength = 0.75f, rimWidth = 0.22f, rimSoftness = 0.03f, rimFacing = 0.75f, specStyle = LooksmithSpecularStyle.Stepped,
                specStrength = 0.9f, specSize = 0.35f, specSoftness = 0.02f, ambient = 0.3f, ambientFlatten = 0.95f, ambientPalette = 0.4f,
                saturation = 1.08f, hazeStart = 35f, hazeDensity = 0.008f, hazeMax = 0.35f, hazeDesat = 0.2f, lineWeight = 3.2f,
                bloom = 0.5f, bloomThreshold = 0.9f, gradeSaturation = 10f,
            },
            // A graphic novel: heavy black ink, flat colour in three bands, halftone in the shadows, cool slate shade.
            new Def
            {
                name = "Comic Line", description = "Graphic novels, exploration, action. Heavy black ink, flat colour in a few bands, halftone dots in the shadows.",
                lineDepth = 0.7f, lineNormal = 0.6f, lineLight = 0.5f, lineJitter = 0.2f, lineFromSurface = 0.0f, lineColor = "#0E0E14",
                lineFadeStart = 35f, lineFadeEnd = 110f,
                key = "#FFF4E2", shadow = "#4E5A82", sky = "#8FCBEA", accent = "#F29A3C", fog = "#DCE6EE",
                bands = 2, bandMix = 1f, edge = 0.01f, terminator = 0.04f, keyLevel = 0.95f, shadowHardness = 1f, accentAmt = 0.05f,
                rimStrength = 0f, specStrength = 0.25f, specSoftness = 0.01f, ambient = 0.32f, ambientFlatten = 1f, ambientPalette = 0.7f,
                saturation = 1.05f, hazeStart = 30f, hazeDensity = 0.01f, hazeMax = 0.4f, lineWeight = 2.8f, postExposure = 0.1f,
            },
            new Def
            {
                name = "Pixel Diorama", description = "Tactics, roguelite, cozy sim. Hard three-band light, palette-quantised pixels with outlines.",
                lineNormal = 0.35f, lineLight = 0f, lineFromSurface = 0.85f,
                key = "#FFD9A0", shadow = "#4C5680", sky = "#6EC1E4", accent = "#F25C54", fog = "#BFE0EE",
                bands = 2, bandMix = 1f, edge = 0.004f, terminator = 0f, spread = 0.65f, keyLevel = 0.92f, shadowHardness = 1f, accentAmt = 0.1f,
                rimStrength = 0.3f, rimWidth = 0.18f, rimSoftness = 0.005f, specStrength = 0.2f, specSoftness = 0.005f,
                ambient = 0.34f, ambientFlatten = 1f, ambientPalette = 0.7f, saturation = 1.15f, hazeStart = 40f, hazeDensity = 0.008f, hazeMax = 0.3f,
                lineWeight = 1.5f, postExposure = 0.1f,
            },
            new Def
            {
                name = "Ink & Wash", description = "Narrative, mystery, puzzle. Value-only light, fine hatched ink from the mid shadows down over a soft grey wash, a wash in the distance.",
                lineNormal = 0.6f, lineLight = 0.7f, lineJitter = 0.55f, lineWobble = 0.45f, lineFromSurface = 0.1f, lineColor = "#141414",
                key = "#F2EBDD", shadow = "#3E4651", sky = "#D9D4C7", accent = "#B23A3A", fog = "#E4DDCD",
                bands = 3, bandMix = 0.85f, edge = 0.05f, wrap = 0.15f, painted = 0.45f, brushScale = 0.5f, keyLevel = 0.9f, keyTint = 0.6f,
                accentAmt = 0.05f, rimStrength = 0.1f, specStrength = 0f, ambient = 0.42f, ambientFlatten = 1f, ambientPalette = 1f,
                saturation = 0.22f, hazeStart = 18f, hazeDensity = 0.016f, hazeMax = 0.45f, hazeDesat = 0.8f, hazeFromSky = 0.85f,
                lineWeight = 3.4f, paper = 0.35f, contrast = 18f, gradeSaturation = -20f,
            },
            // East Asian ink wash: no hatching; grey washes in a few soft values, bold brushy outlines, the paper left bare in
            // the light, and mist that fades into the paper rather than into a sky colour.
            new Def
            {
                name = "Sumi-e", description = "Narrative, East Asian settings, contemplative. Ink wash: grey washes in a few soft values, bold brushy outlines, paper in the light, mist fading into the paper.",
                lineDepth = 0.8f, lineNormal = 0.75f, lineLight = 0.7f, lineJitter = 0.65f, lineWobble = 0.8f, lineFromSurface = 0.1f, lineColor = "#0A0A0D",
                lineFadeStart = 40f, lineFadeEnd = 160f,
                key = "#F6F1E6", shadow = "#3A3F47", sky = "#D9D4C7", accent = "#B23A3A", fog = "#EFE9DC",
                bands = 3, bandMix = 0.55f, edge = 0.05f, wrap = 0.15f, painted = 0.45f, brushScale = 0.5f, keyLevel = 1.1f, keyTint = 0.6f,
                accentAmt = 0.05f, rimStrength = 0.1f, specStrength = 0f, ambient = 0.65f, ambientFlatten = 1f, ambientPalette = 1f,
                saturation = 0.06f, hazeStart = 8f, hazeDensity = 0.035f, hazeMax = 0.9f, hazeDesat = 1f, hazeFromSky = 1f,
                lineWeight = 3.8f, paper = 0.5f, contrast = 18f, gradeSaturation = -20f,
            },
            new Def
            {
                name = "Retro 32-bit", description = "Horror, retro, boomer shooter. Banded vertex-lit feel, snapping vertices, distance fog.",
                key = "#E0D2B0", shadow = "#2C2A4A", sky = "#5B6E8C", accent = "#C0392B", fog = "#4B5670",
                bands = 3, bandMix = 1f, edge = 0.004f, spread = 0.8f, keyLevel = 0.9f, shadowHardness = 1f, accentAmt = 0.05f,
                rimStrength = 0f, specStrength = 0f, ambient = 0.32f, ambientFlatten = 1f, ambientPalette = 0.8f, saturation = 0.9f,
                hazeStart = 12f, hazeDensity = 0.04f, hazeFalloff = 0f, hazeMax = 0.75f, hazeDesat = 0.3f, hazeFromSky = 1f, vertexSnap = 3f, affine = 0.6f,
                gradeSaturation = -10f,
            },
            new Def
            {
                name = "Dark Fable", description = "Soulslike, dark fantasy, stylized horror. Soft light, golden rims, deep cool shadows, heavy fog.",
                lineLight = 0.5f, lineFromSurface = 0.85f, lineWobble = 0.15f,
                key = "#E8C79A", shadow = "#1E2433", sky = "#4A5568", accent = "#D9A441", fog = "#3E4757",
                bands = 2, bandMix = 0.4f, edge = 0.12f, wrap = 0.1f, keyLevel = 0.95f, accentAmt = 0.3f,
                rimStrength = 0.75f, rimWidth = 0.3f, rimSoftness = 0.12f, rimFacing = 0.35f, rimAccent = 0.6f, specStrength = 0.4f, specStyle = LooksmithSpecularStyle.Blinn,
                ambient = 0.3f, ambientFlatten = 0.5f, ambientPalette = 0.6f, saturation = 0.75f, addLights = 1.3f,
                hazeStart = 8f, hazeDensity = 0.045f, hazeFalloff = 0.08f, hazeMax = 0.75f, hazeDesat = 0.6f, hazeFromSky = 1f,
                lineWeight = 1.5f, silhouettesOnly = true,
                vignette = 0.5f, vignetteFromPalette = 0.85f,
                tonemap = TonemappingMode.ACES, bloom = 0.4f, bloomThreshold = 0.95f, postExposure = -0.05f, contrast = 12f, gradeSaturation = -15f,
            },
            new Def
            {
                lite = true, name = "Silent Fog", description = "Survival horror, foggy towns, eerie walks. Fog that swallows everything past a few metres, grain, a dark vignette.",
                lineLight = 0.4f, lineFromSurface = 0.9f,
                key = "#D8D6CF", shadow = "#2A2C30", sky = "#8A8E92", accent = "#8C2F2F", fog = "#9A9EA1",
                bands = 3, bandMix = 0.3f, edge = 0.2f, wrap = 0.3f, keyLevel = 0.7f, shadowHardness = 0.5f, accentAmt = 0.05f,
                rimStrength = 0.1f, rimSoftness = 0.3f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.15f,
                ambient = 0.45f, ambientFlatten = 0.8f, ambientPalette = 0.8f, saturation = 0.35f,
                hazeStart = 2f, hazeDensity = 0.09f, hazeFalloff = 0.02f, hazeMax = 0.95f, hazeDesat = 0.85f, hazeFromSky = 1f, lineWeight = 0.5f,
                grain = 0.55f, vignette = 0.75f, vignetteFromPalette = 0.2f,
                tonemap = TonemappingMode.ACES, postExposure = 0.05f, gradeSaturation = -35f, contrast = 10f,
            },
            new Def
            {
                name = "Lo-Fi Horror", description = "Co-op horror, found footage, liminal spaces. Dim key, posterized light, dark haze that eats the distance.",
                lineLight = 0.6f, lineFromSurface = 0.9f,
                key = "#C9C2A7", shadow = "#15161C", sky = "#3A4048", accent = "#D64933", fog = "#2E323A",
                bands = 2, bandMix = 1f, edge = 0.01f, keyLevel = 0.8f, shadowHardness = 1f, accentAmt = 0.1f,
                rimStrength = 0.1f, specStrength = 0.1f, ambient = 0.6f, ambientFlatten = 1f, ambientPalette = 0.9f, saturation = 0.6f,
                // the haze takes the sky's colour, like every heavy-fog look (checked by day and at its own night: no white blob)
                hazeStart = 10f, hazeDensity = 0.028f, hazeFalloff = 0.02f, hazeMax = 0.85f, hazeDesat = 0.6f, hazeFromSky = 1f,
                lineWeight = 0.5f, grain = 0.6f, fringe = 0.35f,
                // posterized light crushes a dark night to black: brighter key, ambient and exposure keep it readable
                tonemap = TonemappingMode.ACES, postExposure = 0.35f, gradeSaturation = -30f, urpVignette = 0.3f,
            },
            new Def
            {
                name = "Analog Horror", description = "Found footage, analog horror, lost tapes. A green-grey grade, worn VHS tape at full resolution, heavy grain and fog.",
                lineFromSurface = 0.9f,
                key = "#C8CBB0", shadow = "#1A1F1C", sky = "#3E4A44", accent = "#B8433A", fog = "#56605A",
                bands = 2, bandMix = 0.6f, edge = 0.08f, keyLevel = 0.8f, shadowHardness = 0.8f, accentAmt = 0.05f,
                rimStrength = 0.05f, specStrength = 0.05f, ambient = 0.42f, ambientFlatten = 1f, ambientPalette = 0.9f, saturation = 0.45f,
                addLights = 1.3f, hazeStart = 5f, hazeDensity = 0.04f, hazeFalloff = 0.03f, hazeMax = 0.8f, hazeDesat = 0.7f, hazeFromSky = 1f,
                grain = 0.7f, vignette = 0.45f, vignetteFromPalette = 0.3f, fringe = 0.5f,
                tonemap = TonemappingMode.ACES, postExposure = 0.3f, gradeSaturation = -25f, contrast = 15f, urpVignette = 0.25f,
            },
            // A screen-printed national-park poster: the picture simplified into flat shapes (the paint filter, no strokes),
            // its light in a few clean steps of ink that keep their hue, distance in layers of warm haze, on paper with a
            // touch of misregistration. Nothing in it is judged against the frame, so nothing shifts as the view moves.
            new Def
            {
                name = "Wilderness Poster", description = "Walking sims, national-park adventure, travel posters. A screen print: flat shapes in a few inks, warm hard light, distance in layers of coloured haze.",
                key = "#FFDDA6", shadow = "#4A5A8A", sky = "#F2AE6E", accent = "#E8613C", fog = "#C98A6A",
                bands = 2, bandMix = 1f, edge = 0.02f, terminator = 0.04f, wrap = 0.15f, spread = 0.7f, keyLevel = 0.95f, shadowHardness = 1f,
                accentAmt = 0.15f, rimStrength = 0.25f, rimWidth = 0.25f, rimSoftness = 0.05f, rimFacing = 0.5f, rimAccent = 0.6f,
                specStrength = 0.05f, specSoftness = 0.02f, ambient = 0.32f, ambientFlatten = 1f, ambientPalette = 0.75f, saturation = 1.12f,
                hazeStart = 22f, hazeDensity = 0.012f, hazeFalloff = 0.04f, hazeMax = 0.42f, hazeDesat = 0.05f, hazeFromSky = 0.4f,
                paper = 0.22f, fringe = 0.22f,
                posterLevels = 8, posterSoftness = 0.2f,
                gradeSaturation = 10f, contrast = 10f,
            },
            new Def
            {
                name = "Watercolour Sketch", description = "Journals, gentle puzzles, picture books. Loose pencil lines over wet watercolour washes, pigment pooling at the edges.",
                lineDepth = 0.55f, lineNormal = 0.55f, lineLight = 0.55f, lineJitter = 0.45f, lineWobble = 0.6f, lineFromSurface = 0.25f, lineColor = "#4A3C36",
                key = "#FFF0D8", shadow = "#8A92B4", sky = "#B8D4E4", accent = "#E0866C", fog = "#EEE6D8",
                bands = 2, bandMix = 0.55f, edge = 0.12f, wrap = 0.35f, spread = 0.75f, keyLevel = 0.88f, accentAmt = 0.2f,
                rimStrength = 0.1f, rimSoftness = 0.3f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.05f,
                ambient = 0.48f, ambientFlatten = 0.85f, ambientPalette = 0.6f, saturation = 0.95f,
                hazeStart = 22f, hazeDensity = 0.014f, hazeMax = 0.55f, hazeDesat = 0.5f, hazeFromSky = 0.4f,
                lineWeight = 1.7f, paper = 0.35f, postExposure = 0.15f,
            },
            // Anime background art in summer: no line art (that's Cel Anime), soft-edged light through a teal-to-peach ramp,
            // deep saturated greens, a glowing sky.
            new Def
            {
                name = "Summer Anime", description = "Slice of life, cozy fantasy, coming-of-age. Like an anime film's backgrounds: lush greens, soft-edged teal shade, warm sunlit highlights, a glowing bloom.",
                key = "#FFF6E2", shadow = "#5A7CA8", sky = "#7EC8F0", accent = "#FFB067", fog = "#CDE6F5",
                bands = 2, bandMix = 0.7f, edge = 0.08f, terminator = 0.02f, wrap = 0.25f, spread = 0.7f, keyLevel = 1f, shadowHardness = 0.85f,
                accentAmt = 0.35f, gradientRamp = 0.15f, gradShadow = new Color(0.38f, 0.45f, 0.54f), gradMid = new Color(0.98f, 0.78f, 0.55f), gradLit = new Color(1f, 0.98f, 0.9f),
                rimStrength = 0.35f, rimWidth = 0.28f, rimSoftness = 0.15f, rimFacing = 0.7f,
                specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.3f, specSize = 0.4f,
                ambient = 0.4f, ambientFlatten = 0.75f, ambientPalette = 0.3f, saturation = 1.25f,
                hazeStart = 35f, hazeDensity = 0.009f, hazeMax = 0.42f, hazeDesat = 0.1f, hazeFromSky = 0.75f,
                tonemap = TonemappingMode.ACES, postExposure = 0.35f, bloom = 0.7f, bloomThreshold = 0.85f, gradeSaturation = 12f, contrast = 6f,
            },
            // A knitted world (Pro: the Yarn surface knits everything): soft wool light wrapping round each ply, a warm key
            // and lavender shade, almost no shine, a gentle vignette. In Lite, the same light without the knit.
            new Def
            {
                name = "Knitted", description = "Cozy games, toy worlds, crafty platformers. Everything knitted in chunky wool, each piece by its own shape, with soft fuzz up close and warm light.",
                key = "#FFF0D8", shadow = "#6B6A8F", sky = "#A8CBE6", accent = "#F2A65A", fog = "#E4DCCF",
                bands = 3, bandMix = 0.15f, edge = 0.25f, wrap = 0.45f, spread = 0.8f, keyLevel = 0.92f, accentAmt = 0.12f,
                rimStrength = 0.15f, rimSoftness = 0.35f, specStyle = LooksmithSpecularStyle.Blinn, specStrength = 0.05f, specSize = 0.3f,
                ambient = 0.5f, ambientFlatten = 0.6f, ambientPalette = 0.4f, reflections = 0.05f, saturation = 1.05f,
                hazeStart = 30f, hazeDensity = 0.01f, hazeMax = 0.45f, hazeDesat = 0.3f, hazeFromSky = 0.6f, vignette = 0.15f,
                tonemap = TonemappingMode.ACES, postExposure = 0.3f, bloom = 0.2f, contrast = 4f, gradeSaturation = 6f,
            },
        };

        public static Def Find(string name) => All.FirstOrDefault(d => d.name == name) ?? All[0];

        /// <summary>
        /// The look a first Apply (and Restyle's previews) start from: Storybook in Pro; Cel Anime in Lite, which has
        /// Storybook's definition but not its Painterly.
        /// </summary>
        public static string FirstLook => LooksmithEdition.isPro ? "Storybook" : "Cel Anime";

        /// <summary>Writes a look into a Looksmith Look override (every parameter overridden, so looks blend completely).</summary>
        public static void Apply(LooksmithLook l, Def d)
        {
            l.active = true;
            void F(ClampedFloatParameter p, float v) => p.Override(v);
            l.style.Override(true);
            F(l.softness, 0.5f); F(l.contrast, 0.5f); F(l.warmth, 0.5f); F(l.rim, 0.5f); F(l.shine, 0.5f);
            l.bands.Override(d.bands);
            F(l.bandMix, d.bandMix); F(l.edgeSoftness, d.edge); F(l.terminator, d.terminator); F(l.wrap, d.wrap); F(l.bandSpread, d.spread);
            F(l.gradientRamp, d.gradientRamp);
            l.gradientShadow.Override(d.gradShadow); l.gradientMid.Override(d.gradMid); l.gradientLit.Override(d.gradLit);
            F(l.painted, d.painted); F(l.brushScale, d.brushScale); F(l.pbrBlend, d.pbrBlend); F(l.keyLevel, d.keyLevel);
            F(l.facets, d.facets);
            F(l.keyTint, d.keyTint); F(l.shadowHardness, d.shadowHardness); F(l.terminatorAccent, d.accentAmt);
            F(l.rimStrength, d.rimStrength); F(l.rimWidth, d.rimWidth); F(l.rimSoftness, d.rimSoftness); F(l.rimLightFacing, d.rimFacing);
            F(l.rimFromAccent, d.rimAccent);
            l.specularStyle.Override(d.specStyle);
            F(l.specularStrength, d.specStrength); F(l.specularSize, d.specSize); F(l.specularSoftness, d.specSoftness);
            F(l.ambient, d.ambient); F(l.ambientFlatten, d.ambientFlatten); F(l.ambientFromPalette, d.ambientPalette);
            F(l.reflections, d.reflections); F(l.additionalLights, d.addLights);
            F(l.saturation, d.saturation);
            l.keyColor.Override(Lch(d.key)); l.shadowColor.Override(Lch(d.shadow)); l.skyColor.Override(Lch(d.sky));
            l.accentColor.Override(Lch(d.accent)); l.fogColor.Override(Lch(d.fog));
            F(l.lineWeight, d.lineWeight); F(l.lineFromSurface, d.lineFromSurface); l.silhouettesOnly.Override(d.silhouettesOnly);
            F(l.lineDepthSensitivity, d.lineDepth); F(l.lineNormalSensitivity, d.lineNormal); F(l.lineLightWeight, d.lineLight);
            F(l.lineJitter, d.lineJitter); F(l.lineWobble, d.lineWobble); F(l.outlineWidth, d.outlineWidth);
            l.lineFadeStart.Override(d.lineFadeStart); l.lineFadeEnd.Override(d.lineFadeEnd);
            ColorUtility.TryParseHtmlString(d.lineColor, out var lineColor);
            l.lineColor.Override(lineColor);
            F(l.haze, 0.5f); l.hazeStart.Override(d.hazeStart); F(l.hazeDensity, d.hazeDensity); F(l.hazeHeightFalloff, d.hazeFalloff);
            F(l.hazeMax, d.hazeMax); F(l.hazeDesaturation, d.hazeDesat); l.hazeHeightBase.Override(0f); F(l.hazeFromSky, d.hazeFromSky);
            F(l.grain, d.grain); F(l.vignette, d.vignette); F(l.paper, d.paper); F(l.vertexSnap, d.vertexSnap); F(l.affineWarp, d.affine);
            F(l.grainSize, 1.2f); F(l.vignetteFromPalette, d.vignetteFromPalette); F(l.paperScale, 1f); F(l.chromaticFringe, d.fringe);
            l.paperOnSurfaces.Override(d.paperOnSurfaces);
            l.paperTexture.Override(null); l.gradingLut.Override(null); F(l.gradingLutContribution, 1f);
        }

        static Vector3 Lch(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return LooksmithColor.ColorToOklch(c);
        }

        /// <summary>
        /// Adds the Art Styles a look uses (set by the Pro editor; Lite looks have none). Called with the profile and the
        /// look's name after the Look and URP grading are written.
        /// </summary>
        public static System.Action<VolumeProfile, string> PopulateStyles;

        /// <summary>The profile's override of a type, added (every parameter overridden) as a sub-asset if missing.</summary>
        public static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing))
                return existing;
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            if (AssetDatabase.Contains(profile))
                AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        /// <summary>Removes a profile's override of a type (and its sub-asset).</summary>
        public static void RemoveOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var existing))
                return;
            profile.Remove<T>();
            if (AssetDatabase.Contains(existing))
                AssetDatabase.RemoveObjectFromAsset(existing);
            Object.DestroyImmediate(existing, true);
        }

        /// <summary>
        /// The variant tweaks to a look's Art Styles (set by the Pro editor): softer styles for realistic assets, the
        /// look-specific variants (no tracking band, no pixels, no tape, no CRT).
        /// </summary>
        public static System.Action<VolumeProfile, Variant> PopulateVariantStyles;

        /// <summary>Fills a Volume Profile asset (already saved to disk) with a look (or a variant) and its URP grading.</summary>
        public static void Populate(VolumeProfile profile, string lookName)
        {
            var variant = FindVariant(lookName);
            var d = variant != null ? VariantDef(variant) : Find(lookName);
            T Get<T>() where T : VolumeComponent => GetOrAdd<T>(profile);
            Apply(Get<LooksmithLook>(), d);

            var tone = Get<Tonemapping>();
            tone.mode.Override(d.tonemap);
            var bloom = Get<Bloom>();
            bloom.active = d.bloom > 0f;
            bloom.intensity.Override(d.bloom);
            bloom.threshold.Override(d.bloomThreshold);
            var adjust = Get<ColorAdjustments>();
            adjust.postExposure.Override(d.postExposure);
            adjust.contrast.Override(d.contrast);
            adjust.saturation.Override(d.gradeSaturation);
            var vignette = Get<Vignette>();
            vignette.active = d.urpVignette > 0f;
            vignette.intensity.Override(d.urpVignette);
            var grain = Get<FilmGrain>();
            grain.active = d.filmGrain > 0f;
            grain.intensity.Override(d.filmGrain);
            grain.type.Override(FilmGrainLookup.Medium3);
            var chroma = Get<ChromaticAberration>();
            chroma.active = d.chromatic > 0f;
            chroma.intensity.Override(d.chromatic);
            if (d.posterLevels > 0)
            {
                var poster = Get<LooksmithPosterize>();
                poster.intensity.Override(1f);
                poster.lightnessLevels.Override(d.posterLevels);
                poster.chromaLevels.Override(0);
                poster.hueLevels.Override(0);
                poster.bandSoftness.Override(d.posterSoftness);
                poster.dither.Override(0f);
                poster.gradientMap.Override(0f);
            }
            else
                RemoveOverride<LooksmithPosterize>(profile);
            PopulateStyles?.Invoke(profile, d.name);
            if (variant != null)
                PopulateVariantStyles?.Invoke(profile, variant);
            EditorUtility.SetDirty(profile);
        }

        [InitializeOnLoadMethod]
        static void Register() => LooksmithLookPopulator.Populate = Populate;

        public const string ShippedFolder = "Assets/PaleFernStudio/Looksmith/Looks";

        public const string VariantsFolder = ShippedFolder + "/Variants";

        public static string ShippedPath(string lookName) =>
            FindVariant(lookName) != null ? $"{VariantsFolder}/{lookName} Look.asset" : $"{ShippedFolder}/{lookName} Look.asset";

        /// <summary>Creates or refreshes the variants' profiles only (the shipped looks themselves are left as they are).</summary>
        public static List<string> WriteVariantProfiles()
        {
            Directory.CreateDirectory(VariantsFolder);
            var written = new List<string>();
            foreach (var v in Variants)
            {
                var path = ShippedPath(v.name);
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, path);
                }
                Populate(profile, v.name);
                AssetDatabase.SaveAssetIfDirty(profile);
                written.Add(path);
            }
            return written;
        }

        /// <summary>Creates or refreshes the shipped Look Profiles.</summary>
        public static List<string> WriteShippedProfiles()
        {
            Directory.CreateDirectory(ShippedFolder);
            var written = new List<string>();
            foreach (var d in All)
            {
                var path = ShippedPath(d.name);
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, path);
                }
                Populate(profile, d.name);
                AssetDatabase.SaveAssetIfDirty(profile);
                written.Add(path);
            }
            return written;
        }
    }
}
