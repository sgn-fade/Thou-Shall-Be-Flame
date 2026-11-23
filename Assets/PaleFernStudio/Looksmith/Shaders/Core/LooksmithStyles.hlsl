// Looksmith Art Styles: shared helpers for the style-stack passes (full-screen, after URP's post-processing).
// Include after URP's Core.hlsl and Core RP's Blit.hlsl.
#ifndef LOOKSMITH_STYLES_INCLUDED
#define LOOKSMITH_STYLES_INCLUDED

#include "LooksmithWaterPlane.hlsl"

// The camera's own depth (bound for outline styles when the depth texture leaves things out, like a grass field):
// an edge found in the depth texture isn't drawn where something nearer stands in front of it.
TEXTURE2D_X_FLOAT(_LooksmithOccluderDepth);
float4 _LooksmithOccluderParams;    // x 1 when bound

bool LooksmithOccluded(float2 uv, float sceneEye)
{
    if (_LooksmithOccluderParams.x < 0.5)
        return false;
    float raw = SAMPLE_TEXTURE2D_X_LOD(_LooksmithOccluderDepth, sampler_PointClamp, uv, 0).r;
    return LooksmithEyeDepth(raw) < sceneEye * 0.97 - 0.05;
}

float4 _LooksmithStyleMix;          // x intensity, y 1 = layer mask bound (Pro), zw mask size in pixels
float4 _LooksmithMixSky;            // x 1 = the sky keeps its rendered look (the Look's Style Sky off), y counts as sky beyond (m)
TEXTURE2D_X_FLOAT(_LooksmithMixDepth);  // the scene depth, bound with _LooksmithMixSky
uint _LooksmithStyleLayers;         // rendering layers that get the style
TYPED_TEXTURE2D_X(uint, _LooksmithStyleMask);   // rendering-layer bits of the visible surface (0 = nothing drawn: sky)

float4 _LooksmithParams0, _LooksmithParams1, _LooksmithParams2, _LooksmithParams3, _LooksmithParams4, _LooksmithParams5, _LooksmithParams6, _LooksmithParams7;

// ------------------------------------------------------------------ stack blending

// True where the sky keeps its rendered look (the Look's Style Sky off): nothing drawn there, or (with a sky distance)
// something farther than it, like a sky dome.
bool LooksmithKeptSky(float2 uv)
{
    bool kept = false;
    if (_LooksmithMixSky.x > 0.5)
    {
        float raw = SAMPLE_TEXTURE2D_X_LOD(_LooksmithMixDepth, sampler_PointClamp, uv, 0).r;
    #if UNITY_REVERSED_Z
        kept = raw <= 1e-7;
    #else
        kept = raw >= 1.0 - 1e-7;
    #endif
        kept = kept || (_LooksmithMixSky.y > 0.0 && LooksmithEyeDepth(raw) > _LooksmithMixSky.y);
    }
    return kept;
}

// How much of the style this pixel gets: the style's intensity, or 0 on objects outside its rendering layers (and on a
// sky that keeps its own look).
float LooksmithStyleWeight(float2 uv)
{
    float w = LooksmithKeptSky(uv) ? 0.0 : _LooksmithStyleMix.x;
    if (_LooksmithStyleMix.y > 0.5)
    {
        uint2 p = min(uint2(uv * _LooksmithStyleMix.zw), uint2(_LooksmithStyleMix.zw) - 1u);
        uint bits = LOAD_TEXTURE2D_X(_LooksmithStyleMask, p).r;
        w = (bits == 0u || (bits & _LooksmithStyleLayers) != 0u) ? w : 0.0;
    }
    return w;
}

float3 LooksmithStyleMix(float2 uv, float3 source, float3 styled)
{
    return lerp(source, styled, LooksmithStyleWeight(uv));
}

float3 LooksmithSource(float2 uv)       { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }
float3 LooksmithSourcePoint(float2 uv)  { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb; }

// ------------------------------------------------------------------ colour

float LooksmithLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float3 LooksmithLinearToOklab(float3 c)
{
    c = max(c, 0.0);
    float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
    float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
    float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
    float3 lms = pow(max(float3(l, m, s), 1e-9), 1.0 / 3.0);
    return float3(
        0.2104542553 * lms.x + 0.7936177850 * lms.y - 0.0040720468 * lms.z,
        1.9779984951 * lms.x - 2.4285922050 * lms.y + 0.4505937099 * lms.z,
        0.0259040371 * lms.x + 0.7827717662 * lms.y - 0.8086757660 * lms.z);
}

float3 LooksmithOklabToLinear(float3 lab)
{
    float l_ = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
    float m_ = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
    float s_ = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;
    float l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
    return float3(
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
}

float3 LooksmithToSrgb(float3 c)   { return LinearToSRGB(saturate(c)); }
float3 LooksmithFromSrgb(float3 c) { return SRGBToLinear(saturate(c)); }

// ------------------------------------------------------------------ dither and noise (all deterministic)

// Ordered dither thresholds in (-0.5, 0.5).
float LooksmithBayer4(uint2 p)
{
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(p.y & 3u) * 4u + (p.x & 3u)] + 0.5) / 16.0 - 0.5;
}

float LooksmithBayer8(uint2 p)
{
    uint x = p.x & 7u, y = p.y & 7u, xy = x ^ y;
    uint v = ((xy & 1u) << 5) | ((x & 1u) << 4) | ((xy & 2u) << 2) | ((x & 2u) << 1) | ((xy & 4u) >> 1) | ((x & 4u) >> 2);
    return (v + 0.5) / 64.0 - 0.5;
}

// Interleaved gradient noise: a cheap, well-spread screen-space dither (blue-noise-like).
float LooksmithIgn(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))) - 0.5; }

float LooksmithHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float LooksmithHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float LooksmithValueNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(LooksmithHash13(i), LooksmithHash13(i + float3(1, 0, 0)), f.x), lerp(LooksmithHash13(i + float3(0, 1, 0)), LooksmithHash13(i + float3(1, 1, 0)), f.x), f.y);
    float b = lerp(lerp(LooksmithHash13(i + float3(0, 0, 1)), LooksmithHash13(i + float3(1, 0, 1)), f.x), lerp(LooksmithHash13(i + float3(0, 1, 1)), LooksmithHash13(i + float3(1, 1, 1)), f.x), f.y);
    return lerp(a, b, f.z);
}

// ------------------------------------------------------------------ palettes

// Nearest-palette lookup table (32 steps per channel in a 1024x32 strip, baked by the Posterize shader's bake pass).
TEXTURE2D(_LooksmithPaletteLut);

// The palette colour nearest to a linear colour; `offset` (in sRGB units) dithers the lookup.
float3 LooksmithPaletteNearest(float3 linearColor, float offset)
{
    float3 s = saturate(LooksmithToSrgb(linearColor) + offset);
    int3 i = (int3)floor(s * 31.0 + 0.5);
    return LOAD_TEXTURE2D(_LooksmithPaletteLut, int2(i.b * 32 + i.r, i.g)).rgb;
}

// ------------------------------------------------------------------ scene reconstruction (depth/normals requested by the style)

bool LooksmithIsSkyDepth(float raw)
{
#if UNITY_REVERSED_Z
    return raw <= 1e-7;
#else
    return raw >= 1.0 - 1e-7;
#endif
}

// ------------------------------------------------------------------ marks laid on the surfaces

// Paper grain, brush strokes and paint blotches are laid on the surfaces, not the screen, at one steady size out to
// LOOKSMITH_MARKS_NEAR metres: walking up to a wall shows the same strokes larger, as on a canvas (they used to keep a steady
// size on screen, so they shrank on the wall as you came closer, and the octaves that did it ringed the player). Beyond,
// they double in size with the distance (a steady size on screen) so far surfaces still read as painted.
#define LOOKSMITH_MARKS_NEAR 6.0

// The marks' octave at a distance from the eye: k (their size is the near size times 2^k) and f, the blend toward
// k + 1. Zero out to LOOKSMITH_MARKS_NEAR; an orthographic camera passes 0 (one size everywhere).
// The distance is the straight one to the camera (LooksmithMarkDistance), not the depth along the view: with the depth, the
// marks on a wall changed octave as the head only turned.
float2 LooksmithMarkOctave(float distance)
{
    float level = log2(max(distance / LOOKSMITH_MARKS_NEAR, 1.0));
    float k = floor(level);
    return float2(k, smoothstep(0.0, 1.0, level - k));
}

// How far a surface point is from the eye, for LooksmithMarkOctave.
float LooksmithMarkDistance(float3 positionWS) { return LooksmithViewDistance(positionWS); }

// Each octave's own place on the world: octave k's pattern is sampled at (p + LooksmithOctaveShift(k)) / (size * 2^k),
// whichever side of a blend it's on. The blend toward k + 1 ends on exactly the pattern octave k + 1 starts with, so the
// marks change size smoothly with the distance (the coarse half of a blend used an offset of its own, and the pattern
// jumped where one octave handed over to the next: a ring round the player at 12 and 24 m).
float3 LooksmithOctaveShift(float k) { return float3(13.7, 5.3, 9.1) * k; }

// Blends two octaves of a mark texture (values about 0.5) keeping its contrast: a plain blend of two unrelated patterns
// is half as contrasty halfway, which read as a pale ring round the player.
float LooksmithBlendOctaves(float fine, float coarse, float f)
{
    return 0.5 + (lerp(fine, coarse, f) - 0.5) * rsqrt(max((1.0 - f) * (1.0 - f) + f * f, 0.5));
}
float2 LooksmithBlendOctaves(float2 fine, float2 coarse, float f)
{
    return 0.5 + (lerp(fine, coarse, f) - 0.5) * rsqrt(max((1.0 - f) * (1.0 - f) + f * f, 0.5));
}
float4 LooksmithBlendOctaves(float4 fine, float4 coarse, float f)
{
    return 0.5 + (lerp(fine, coarse, f) - 0.5) * rsqrt(max((1.0 - f) * (1.0 - f) + f * f, 0.5));
}

// ------------------------------------------------------------------ marks that ride the wind

// The Looksmith Sway pass draws, for every visible surface the wind sways, its wind offset (xyz, metres). A mark laid where a
// leaf is now would stay put while the leaf sways through it; laid at the leaf's place at rest, it rides the leaf.
TEXTURE2D_X_FLOAT(_LooksmithSwayOffset);
float4 _LooksmithSwayParams;        // x 1 when bound

// Where the surface seen at a pixel sits when the wind is still (positionWS itself if it doesn't sway).
float3 LooksmithRestPosition(float2 uv, float3 positionWS)
{
    if (_LooksmithSwayParams.x < 0.5)
        return positionWS;
    return positionWS - SAMPLE_TEXTURE2D_X_LOD(_LooksmithSwayOffset, sampler_PointClamp, uv, 0).xyz;
}

// ------------------------------------------------------------------ a pixel's footprint on a surface

// How far one pixel across (dPdx) and one up (dPdy) the screen moves along a surface (normal n) at positionWS, from the
// camera's rays through the surface's plane: exact for any view direction and any turn of the camera (screen derivatives
// are one value per 2 x 2 pixels and jump at silhouettes; the depth along the view changes as the camera only turns).
// metresPerPixel: 2 tan(fov / 2) / height (orthographic: the world size of a pixel).
void LooksmithPixelFootprint(float3 positionWS, float3 n, float metresPerPixel, bool ortho, out float3 dPdx, out float3 dPdy)
{
    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz, forward = -UNITY_MATRIX_V[2].xyz;
    float3 rx = right * metresPerPixel, ry = up * metresPerPixel;
    float3 d = forward;
    float scale = 1.0;
    if (!ortho)
    {
        float3 v = positionWS - _WorldSpaceCameraPos;
        scale = max(dot(v, forward), 1e-4);
        d = v / scale;                                   // the ray, its forward part 1
    }
    float dn = dot(d, n);
    float limit = 0.08 * length(d);                      // grazing surfaces: a long but finite footprint
    dn = dn < 0.0 ? min(dn, -limit) : max(dn, limit);
    dPdx = scale * (rx - d * (dot(rx, n) / dn));
    dPdy = scale * (ry - d * (dot(ry, n) / dn));
}

// ------------------------------------------------------------------ paper laid on the surfaces

// A surface position's screen derivatives for filtering marks laid on it, taken where every pixel of the quad runs
// (before any branch) and capped at a few pixels' worth so a silhouette's depth jump doesn't blur its edge.
void LooksmithSurfaceDerivatives(float3 positionWS, float eye, float metresPerPixel, out float3 dPdx, out float3 dPdy)
{
    dPdx = ddx(positionWS);
    dPdy = ddy(positionWS);
    float cap = max(eye, 1.0) * metresPerPixel * 8.0;
    dPdx *= min(1.0, cap / max(length(dPdx), 1e-6));
    dPdy *= min(1.0, cap / max(length(dPdy), 1e-6));
}

// A paper texture laid on the world (triplanar, blended across faces) instead of over the screen. Its texels are
// nearTexelMetres wide out to LOOKSMITH_MARKS_NEAR metres and grow in octaves beyond (LooksmithMarkOctave of the distance; 0
// for one size). paperTexels is the texture's width. Filtered by the pixel's footprint (dPdx, dPdy:
// LooksmithSurfaceDerivatives), so a surface seen edge-on blurs its paper instead of shimmering, without the paper changing
// size as the view turns.
float4 LooksmithSurfacePaper(TEXTURE2D_PARAM(tex, samp), float3 positionWS, float3 n, float distance, float nearTexelMetres, float paperTexels,
                         float3 dPdx, float3 dPdy)
{
    float2 o = LooksmithMarkOctave(distance);
    float3 w = pow(abs(n), 4.0);
    w /= max(w.x + w.y + w.z, 1e-4);
    float t0 = nearTexelMetres * exp2(o.x) * paperTexels, t1 = t0 * 2.0;
    float3 p0 = positionWS + LooksmithOctaveShift(o.x), p1 = positionWS + LooksmithOctaveShift(o.x + 1.0);
    float4 fine = 0, coarse = 0;
    if (w.x > 0.01)
    {
        fine += w.x * SAMPLE_TEXTURE2D_GRAD(tex, samp, p0.zy / t0, dPdx.zy / t0, dPdy.zy / t0);
        coarse += w.x * SAMPLE_TEXTURE2D_GRAD(tex, samp, p1.zy / t1, dPdx.zy / t1, dPdy.zy / t1);
    }
    if (w.y > 0.01)
    {
        fine += w.y * SAMPLE_TEXTURE2D_GRAD(tex, samp, p0.xz / t0, dPdx.xz / t0, dPdy.xz / t0);
        coarse += w.y * SAMPLE_TEXTURE2D_GRAD(tex, samp, p1.xz / t1, dPdx.xz / t1, dPdy.xz / t1);
    }
    if (w.z > 0.01)
    {
        fine += w.z * SAMPLE_TEXTURE2D_GRAD(tex, samp, p0.xy / t0, dPdx.xy / t0, dPdy.xy / t0);
        coarse += w.z * SAMPLE_TEXTURE2D_GRAD(tex, samp, p1.xy / t1, dPdx.xy / t1, dPdy.xy / t1);
    }
    return o.y > 0.001 ? LooksmithBlendOctaves(fine, coarse, o.y) : fine;
}

// ------------------------------------------------------------------ scene key (the light's tone)

// Tone-driven styles (comic bands and dots, hatching) judge light and shadow against the scene's key, like an eye
// adapting: a night scene isn't all shadow, a bright noon isn't all light. The key is the tone a mid surface takes in the
// scene's light (LooksmithSceneKey.cs: the main light, the ambient, the exposure and the tonemapper), set per camera, never
// measured from the frame: a surface keeps its tone whatever else comes into view.
float4 _LooksmithSceneKeyTone;      // x the key (0..1 tone)

float LooksmithTone(float3 linearColor) { return LinearToSRGB(saturate(LooksmithLuma(linearColor))); }

// The scene's key (0..1 tone).
float LooksmithSceneKeyTone()
{
    return _LooksmithSceneKeyTone.x > 0.0 ? _LooksmithSceneKeyTone.x : 0.42;
}

// How much a style brightens (or darkens) tones before judging them, so the scene's key lands at a mid tone; `adapt`
// 0 keeps tones as they are. Clamped: a night scene stays darker than day, a bright one lighter.
float LooksmithToneAdaptation(float adapt)
{
    float key = LooksmithSceneKeyTone();
    return lerp(1.0, clamp(0.42 / max(key, 0.02), 0.7, 3.2), saturate(adapt));
}

#endif // LOOKSMITH_STYLES_INCLUDED
