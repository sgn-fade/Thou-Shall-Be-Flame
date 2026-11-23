// Looksmith shared globals and helpers. Every Looksmith shader includes this file.
// The globals are pushed per camera by the Looksmith Setup pass from the camera's Look (Volume stack).
#ifndef LOOKSMITH_COMMON_INCLUDED
#define LOOKSMITH_COMMON_INCLUDED

#include "LooksmithDepth.hlsl"

// 1 when a Look styles this camera; 0 means "render as plain PBR" (no Look Volume, or Bypass on).
float _LooksmithEnabled;
float _LooksmithBypass;
float _LooksmithDepthAvailable;   // 1 when the Look requested the depth texture (soft particles)
float _LooksmithPrepassDepth;     // 1 in the opaque pass when the depth texture holds this frame's depth prepass
float4 _LooksmithSplit;           // A/B compare: x split position (0..1 of the view), y 1 when on; left of it renders plain
float4 _LooksmithDebugSurface;    // x 1 = Lighting Only debug view (albedo grey, no emission)
float4 _LooksmithUnityFog;        // the scene's Unity fog for Looksmith surfaces: x mode (0 off, 1 linear, 2 exp, 3 exp2), y density, z start, w end
float4 _LooksmithUnityFogColor;   // rgb linear

// Palette roles, linear RGB.
float4 _LooksmithKeyColor;        // rgb key light tint
float4 _LooksmithShadowColor;     // rgb shadow tint (multiplier, relative to the key light intensity)
float4 _LooksmithSkyColor;        // rgb ambient from the palette
float4 _LooksmithAccentColor;     // rgb terminator accent
float4 _LooksmithFogColor;        // rgb aerial perspective colour

float4 _LooksmithLightParams;     // x painted amount, y PBR blend, z shadow-edge hardness, w key tint strength
float4 _LooksmithAmbientParams;   // x ambient intensity, y flatten, z palette sky amount, w reflection strength
float4 _LooksmithRimParams;       // x strength, y width, z softness, w light-facing
float4 _LooksmithRimColor;        // rgb rim colour
float4 _LooksmithSpecParams;      // x strength, y size, z softness, w style (0 stepped, 1 Blinn, 2 anisotropic)
float4 _LooksmithSpecColor;       // rgb specular colour
float4 _LooksmithTerminatorParams;// x accent strength, y lit-band level, z unused, w additional-light intensity
float4 _LooksmithAtmosParams;     // x haze start (m), y density, z height falloff, w max haze
float4 _LooksmithAtmosParams2;    // x desaturation with distance, y height base (m), z haze colour from the sky, w unused
float4 _LooksmithSkyHorizon;      // Looksmith Sky: rgb horizon colour (linear), a = 1 while a Looksmith Sky is active
float4 _LooksmithSkyGround;       // Looksmith Sky: rgb below-horizon colour, a = how much a Look may tint the sky's light (1 by day)
float4 _LooksmithSurfaceParams;   // x albedo saturation, y flat shading (facets), z brush noise scale, w unused
float4 _LooksmithVertexParams;    // x vertex snap grid in pixels (0 off), y affine warp, zw unused

// Caustics (Pro water): set while a water surface casts them; strength 0 = off (always so in Lite).
float4 _LooksmithCausticParams;   // x water plane height (m), y strength, z pattern scale (per m), w depth over which they fade (m)
float4 _LooksmithCausticParams2;  // x time (s, the water clock)
float4 _LooksmithCausticBounds;   // xy min, zw max of the water's footprint (world xz)

TEXTURE2D(_LooksmithRampAtlas);   SAMPLER(sampler_LooksmithRampAtlas);
TEXTURE2D(_LooksmithCausticTexture);
TEXTURE2D(_LooksmithBrushNoise);  SAMPLER(sampler_LooksmithBrushNoise);

// Looksmith Atmosphere (Pro) feeds these; zero (off) everywhere else.
float4 _LooksmithCloudShadowParams;  // x strength (0 = off), y pattern scale (per m), zw drift offset (m)
float4 _LooksmithCloudShadowParams2; // x coverage (0..1), y edge softness, z cloud height (m), w unused
float4 _LooksmithAOParams;           // x ambient occlusion amount (0 = off), y how much it also darkens direct light
TEXTURE2D(_LooksmithAOTexture);

#define LOOKSMITH_RAMP_ROW_DIFFUSE  0.125
#define LOOKSMITH_RAMP_ROW_GRADIENT 0.375

bool LooksmithIsStyled()
{
    return _LooksmithEnabled > 0.5 && _LooksmithBypass < 0.5;
}

// A/B compare: true left of the split (x = 0..1 across the view), where everything renders as under Bypass.
bool LooksmithSplitPlain(float viewX)
{
    return _LooksmithSplit.y > 0.5 && viewX < _LooksmithSplit.x;
}

// LooksmithIsStyled() for a surface point: also false left of the A/B split.
bool LooksmithIsStyledAt(float3 positionWS)
{
    bool styled = LooksmithIsStyled();
#ifndef SHADERGRAPH_PREVIEW
    if (styled && _LooksmithSplit.y > 0.5)
    {
        float4 positionCS = TransformWorldToHClip(positionWS);
        styled = !LooksmithSplitPlain(positionCS.x / positionCS.w * 0.5 + 0.5);
    }
#endif
    return styled;
}

float LooksmithLuminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 LooksmithSaturate(float3 c, float saturation)
{
    return lerp(LooksmithLuminance(c).xxx, c, saturation);
}

// The haze colour: the palette's Fog colour, or with a Looksmith Sky in the scene the sky's horizon (by the Look's "Haze
// from sky"), with the palette's hue kept at the sky's brightness, so haze never glows at night.
float3 LooksmithHazeColor()
{
    float3 fog = _LooksmithFogColor.rgb;
    if (_LooksmithSkyHorizon.a > 0.5)
    {
        float3 relative = fog * (LooksmithLuminance(_LooksmithSkyHorizon.rgb) / max(LooksmithLuminance(fog), 1e-4));
        fog = lerp(relative, _LooksmithSkyHorizon.rgb, _LooksmithAtmosParams2.z);
    }
    return fog;
}

// Smooth 2D value noise (0..1), no texture needed.
float LooksmithValueNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
    float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
    float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
    float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Shadows of drifting clouds on the ground (1 = sunlit). The point is projected along the light onto the cloud
// layer, so the shadows slide over hills and walls the way the sun throws them.
float LooksmithCloudShadow(float3 positionWS, float3 lightDir)
{
    if (_LooksmithCloudShadowParams.x <= 0.0)
        return 1.0;
    float rise = max(_LooksmithCloudShadowParams2.z - positionWS.y, 0.0);
    float2 p = positionWS.xz + lightDir.xz / max(lightDir.y, 0.15) * rise;
    p = p * _LooksmithCloudShadowParams.y + _LooksmithCloudShadowParams.zw * _LooksmithCloudShadowParams.y;
    float n = LooksmithValueNoise2(p) * 0.55 + LooksmithValueNoise2(p * 2.13 + 7.1) * 0.3 + LooksmithValueNoise2(p * 4.7 + 3.3) * 0.15;
    float cover = _LooksmithCloudShadowParams2.x;
    float soft = max(_LooksmithCloudShadowParams2.y, 0.01);
    float cloud = smoothstep(1.0 - cover - soft, 1.0 - cover + soft, n);
    return 1.0 - cloud * _LooksmithCloudShadowParams.x;
}

// Screen-space ambient occlusion from the Atmosphere's AO pass (1 = open).
float LooksmithScreenAO(float2 screenUV)
{
    if (_LooksmithAOParams.x <= 0.0)
        return 1.0;
    float ao = SAMPLE_TEXTURE2D_LOD(_LooksmithAOTexture, sampler_LinearClamp, screenUV, 0).r;
    return lerp(1.0, ao, _LooksmithAOParams.x);
}

// Aerial perspective amount at a world position (0 .. the Look's max haze). Surfaces and lines share it.
float LooksmithHazeAmount(float3 positionWS)
{
    float dist = LooksmithViewDistance(positionWS);     // (from an orthographic camera's plane, not its position)
    float haze = 1.0 - exp(-max(0.0, dist - _LooksmithAtmosParams.x) * _LooksmithAtmosParams.y);
    float height = exp(-max(0.0, positionWS.y - _LooksmithAtmosParams2.y) * _LooksmithAtmosParams.z);
    return saturate(haze * height) * _LooksmithAtmosParams.w;
}

#endif // LOOKSMITH_COMMON_INCLUDED
