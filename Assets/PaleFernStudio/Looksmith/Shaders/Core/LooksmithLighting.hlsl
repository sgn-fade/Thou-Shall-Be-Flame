// Looksmith lighting model: one ramp-based model that covers Bands, Gradient Ramp, Soft Wrap, Painted and PBR Blend.
// The ramp curve itself is baked on the CPU into _LooksmithRampAtlas from the Look parameters, so every mode
// blends continuously through the Volume system.
#ifndef LOOKSMITH_LIGHTING_INCLUDED
#define LOOKSMITH_LIGHTING_INCLUDED

#include "LooksmithCommon.hlsl"

#ifndef SHADERGRAPH_PREVIEW
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

struct LooksmithSurface
{
    float3 albedo;
    float3 normalWS;
    float3 tangentWS;
    float3 emission;
    float  smoothness;
    float  metallic;
    float  occlusion;
    float4 shadowTint;   // rgb, a = per-material override weight (0 inherits the Look)
    float4 rimTint;      // rgb, a = per-material override weight
    float  rimAmount;
    float  specularAmount;
    // Character extras (Pro graph); zero elsewhere.
    float  faceMask;     // 1 on the face: lighting comes from the SDF face-shadow map instead of N.L
    float  faceLight;    // 0..1 lit amount from the SDF (computed by the Character entry point)
    float  hairMask;     // 1 on hair: anisotropic highlight along the tangent
    float  hairShift;    // shifts the hair highlight along the strand
    // Light let through from behind (grass blades, petals), before the key light's shadow: added as emission, times the
    // shadow the shading samples anyway (a blade used to filter the shadow map once more for it).
    float3 translucency;
};

void LooksmithInitSurface(inout LooksmithSurface s)
{
    s.albedo = 0.5; s.normalWS = float3(0, 1, 0); s.tangentWS = float3(1, 0, 0); s.emission = 0;
    s.smoothness = 0.3; s.metallic = 0; s.occlusion = 1; s.shadowTint = 0; s.rimTint = 0;
    s.rimAmount = 1; s.specularAmount = 1; s.faceMask = 0; s.faceLight = 1; s.hairMask = 0; s.hairShift = 0;
    s.translucency = 0;
}

struct LooksmithGeometry
{
    float3 positionWS;
    float3 viewDirWS;
    float2 screenUV;
    float2 lightmapUV;   // the mesh's lightmap UV (uv1; uv0 on terrain), before unity_LightmapST; 0 where there's none
};

// Diffuse ramp: x is N.L in [-1, 1].
float LooksmithRamp(float NdotL)
{
    float u = saturate(NdotL * 0.5 + 0.5);
    return SAMPLE_TEXTURE2D_LOD(_LooksmithRampAtlas, sampler_LooksmithRampAtlas, float2(u, LOOKSMITH_RAMP_ROW_DIFFUSE), 0).r;
}

// Gradient Ramp mode: a colour ramp indexed by the final light amount; alpha is the mode's weight.
float4 LooksmithGradientRamp(float light)
{
    return SAMPLE_TEXTURE2D_LOD(_LooksmithRampAtlas, sampler_LooksmithRampAtlas, float2(saturate(light), LOOKSMITH_RAMP_ROW_GRADIENT), 0);
}

// Hardens soft shadow-map edges toward the Look's band edge.
float LooksmithShadowEdge(float attenuation)
{
    return lerp(attenuation, smoothstep(0.35, 0.65, attenuation), _LooksmithLightParams.z);
}

// The brush noise, triplanar in world space. The position's screen derivatives come in (the same ones a plain sample
// would take), so it can be read where neighbouring pixels may have left the shading early.
float LooksmithBrushNoise(float3 positionWS, float3 normalWS, float3 dpdx, float3 dpdy)
{
    float scale = max(_LooksmithSurfaceParams.z, 1e-3);
    float3 w = pow(abs(normalWS), 4.0);
    w /= max(w.x + w.y + w.z, 1e-4);
    dpdx *= scale;
    dpdy *= scale;
    float a = SAMPLE_TEXTURE2D_GRAD(_LooksmithBrushNoise, sampler_LooksmithBrushNoise, positionWS.zy * scale, dpdx.zy, dpdy.zy).r;
    float b = SAMPLE_TEXTURE2D_GRAD(_LooksmithBrushNoise, sampler_LooksmithBrushNoise, positionWS.xz * scale, dpdx.xz, dpdy.xz).r;
    float c = SAMPLE_TEXTURE2D_GRAD(_LooksmithBrushNoise, sampler_LooksmithBrushNoise, positionWS.xy * scale, dpdx.xy, dpdy.xy).r;
    return a * w.x + b * w.y + c * w.z;
}

float LooksmithBrushNoise(float3 positionWS, float3 normalWS)
{
    return LooksmithBrushNoise(positionWS, normalWS, ddx(positionWS), ddy(positionWS));
}

float LooksmithSpecularTerm(float3 N, float3 H, float3 T, float smoothness)
{
    float size = _LooksmithSpecParams.y;
    float soft = max(_LooksmithSpecParams.z, 1e-3);
    float exponent = exp2(lerp(9.0, 2.0, size)) * lerp(0.25, 1.0, smoothness);
    float blinn = pow(saturate(dot(N, H)), exponent);
    float stepped = smoothstep(0.5 - soft * 0.5, 0.5 + soft * 0.5, blinn);
    float style = _LooksmithSpecParams.w;
    float spec = lerp(stepped, blinn, saturate(style));
    if (style > 1.0)
    {
        // Anisotropic hair streak (Kajiya-Kay) along the tangent.
        float TdotH = dot(normalize(T), H);
        float sinTH = sqrt(saturate(1.0 - TdotH * TdotH));
        float streak = pow(sinTH, exponent);
        streak = lerp(streak, smoothstep(0.5 - soft * 0.5, 0.5 + soft * 0.5, streak), 0.5);
        spec = lerp(spec, streak, saturate(style - 1.0));
    }
    // Only genuinely smooth surfaces shine: URP's default smoothness (0.5) stays mostly matte.
    return spec * smoothstep(0.3, 0.9, smoothness);
}

// Only curved surfaces get rim light, so it stays on real silhouettes: a flat face seen edge-on (a roof, a wall) would
// otherwise light up whole and change with every step the camera takes. Curvature is about 1 / radius: how fast the
// normal turns per metre across the pixel (from the normal's and the position's screen derivatives).
float LooksmithCurved(float3 dndx, float3 dndy, float3 dpdx, float3 dpdy)
{
    float3 dn = abs(dndx) + abs(dndy);
    float3 dp = abs(dpdx) + abs(dpdy);
    float curvature = (dn.x + dn.y + dn.z) / max(dp.x + dp.y + dp.z, 1e-6);
    return smoothstep(0.08, 0.5, curvature);
}

// curved: LooksmithCurved at this pixel.
float LooksmithRimTermCurved(float3 N, float3 V, float NdotL, float shadow, float curved)
{
    float fresnel = 1.0 - saturate(dot(N, V));
    float edge = 1.0 - _LooksmithRimParams.y;
    float soft = max(_LooksmithRimParams.z, 1e-3);
    float rim = smoothstep(edge - soft * 0.5, edge + soft * 0.5, fresnel);
    float facing = smoothstep(-0.1, 0.4, NdotL) * shadow;
    // Keep rim on silhouettes and walls: ground and grass seen at grazing angles would otherwise fill with rim.
    float upMask = 1.0 - smoothstep(0.45, 0.95, N.y);
    return rim * lerp(1.0, facing, _LooksmithRimParams.w) * upMask * curved;
}

float LooksmithRimTerm(float3 N, float3 V, float NdotL, float shadow, float3 positionWS)
{
    return LooksmithRimTermCurved(N, V, NdotL, shadow, LooksmithCurved(ddx(N), ddy(N), ddx(positionWS), ddy(positionWS)));
}

// What the stylized shading reads from screen derivatives (the flat-shaded normal, how curved the surface is, the brush
// noise), worked out up front by every pixel of a quad: a pixel may then leave the shading early (a hidden layer)
// without leaving its neighbours' derivatives undefined, and the rest of the shading takes no derivatives.
struct LooksmithScreenTerms
{
    float3 normalWS;    // the stylized shading's normal: flat-shaded as far as the Look asks
    float  curved;
    float  brush;
};

LooksmithScreenTerms LooksmithGetScreenTerms(LooksmithSurface s, LooksmithGeometry g)
{
    LooksmithScreenTerms t;
    float3 dpdx = ddx(g.positionWS);
    float3 dpdy = ddy(g.positionWS);
    float3 N = s.normalWS;
    // Flat shading: the face's own normal (from the position's screen derivatives), turned to the side the surface faces.
    if (_LooksmithSurfaceParams.y > 1e-3)
    {
        float3 faceN = cross(dpdy, dpdx);
        float len = length(faceN);
        if (len > 1e-12)
        {
            faceN /= len;
            faceN *= dot(faceN, N) < 0.0 ? -1.0 : 1.0;
            N = normalize(lerp(N, faceN, _LooksmithSurfaceParams.y));
        }
    }
    t.normalWS = N;
    t.curved = LooksmithCurved(ddx(N), ddy(N), dpdx, dpdy);
    t.brush = _LooksmithLightParams.x > 1e-3 ? LooksmithBrushNoise(g.positionWS, N, dpdx, dpdy) : 0.5;
    return t;
}

#ifndef SHADERGRAPH_PREVIEW
// Hidden layers: in the opaque pass after a depth prepass, an alpha-clipped fragment lying behind the depth the prepass
// found at its pixel is covered by a nearer surface (leaf cards drawn layer over layer, a wall behind a tree). Its
// shading is skipped and the fragment dropped: the nearer surface draws the pixel. The margin keeps a surface's own
// fragments (the same depth, give or take two passes' precision) and anything a hair behind it. The depth compared is
// the farthest of the pixel's and its four neighbours': the prepass and the opaque pass place a vertex a hair apart (two
// compiled shaders), so a pixel on a silhouette can be covered in one and not in the other; next to an edge, nothing is
// skipped. Only alpha-clipped surfaces: a solid one's hidden fragments never reach the shader (the depth test rejects
// them before it runs), and the five depth reads would only add to every visible pixel.
bool LooksmithBehindPrepass(float3 positionWS, float2 screenUV)
{
    if (_LooksmithPrepassDepth < 0.5)
        return false;
    int2 last = int2(_ScaledScreenParams.xy) - 1;
    int2 p = clamp(int2(screenUV * _ScaledScreenParams.xy), 0, last);
    float c = LoadSceneDepth(uint2(p));
    float l = LoadSceneDepth(uint2(max(p.x - 1, 0), p.y));
    float r = LoadSceneDepth(uint2(min(p.x + 1, last.x), p.y));
    float d = LoadSceneDepth(uint2(p.x, max(p.y - 1, 0)));
    float u = LoadSceneDepth(uint2(p.x, min(p.y + 1, last.y)));
    #if UNITY_REVERSED_Z
        float raw = min(c, min(min(l, r), min(d, u)));
    #else
        float raw = max(c, max(max(l, r), max(d, u)));
    #endif
    float scene = LooksmithEyeDepth(raw);
    float mine = -TransformWorldToView(positionWS).z;
    return mine > scene * 1.002 + 0.02;
}

bool LooksmithHiddenLayer(float3 positionWS, float2 screenUV)
{
#if defined(_SURFACE_TYPE_TRANSPARENT) || !defined(_ALPHATEST_ON)
    return false;
#else
    return LooksmithBehindPrepass(positionWS, screenUV);
#endif
}
#endif

// Caustics: light focused by the water's waves, projected onto anything under the water plane inside its footprint.
// Two scrolling copies of a caustic pattern, kept where both are bright, fading in below the surface and out with depth.
float LooksmithCaustics(float3 positionWS, float3 normalWS)
{
    float caustics = 0.0;
    float depth = _LooksmithCausticParams.x - positionWS.y;
    float2 p = positionWS.xz;
    if (_LooksmithCausticParams.y > 0.0 && depth > 0.0 && all(p >= _LooksmithCausticBounds.xy) && all(p <= _LooksmithCausticBounds.zw))
    {
        float t = _LooksmithCausticParams2.x;
        float2 uv = p * _LooksmithCausticParams.z;
        float a = SAMPLE_TEXTURE2D_LOD(_LooksmithCausticTexture, sampler_LinearRepeat, uv + float2(t * 0.045, t * 0.027), 0).r;
        float b = SAMPLE_TEXTURE2D_LOD(_LooksmithCausticTexture, sampler_LinearRepeat, uv * 1.31 + float2(-t * 0.031, t * 0.038), 0).r;
        float fade = saturate(depth * 4.0) * (1.0 - saturate(depth / max(_LooksmithCausticParams.w, 0.01)));
        caustics = min(a, b) * fade * _LooksmithCausticParams.y * saturate(normalWS.y * 0.7 + 0.3);
    }
    return caustics;
}

float3 LooksmithAtmosphere(float3 color, float3 positionWS)
{
    float haze = LooksmithHazeAmount(positionWS);
    color = lerp(color, LooksmithLuminance(color).xxx, haze * _LooksmithAtmosParams2.x);
    return lerp(color, LooksmithHazeColor(), haze);
}

#ifndef SHADERGRAPH_PREVIEW

float4 LooksmithShadowCoord(float3 positionWS, float2 screenUV)
{
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        return float4(screenUV, 0, 1);
    #else
        return TransformWorldToShadowCoord(positionWS);
    #endif
}

// The scene's own Unity fog, as URP fogs its own materials (by view depth). URP's Unlit graphs never take it, so
// without this a restyled surface drops out of the fog the rest of the scene sits in. Off under a Looksmith Sky's fog.
float3 LooksmithUnityFog(float3 color, float3 positionWS)
{
    if (_LooksmithUnityFog.x < 0.5)
        return color;
    float z = max(-TransformWorldToView(positionWS).z, 0.0);
    float visible;
    if (_LooksmithUnityFog.x < 1.5)
        visible = saturate((_LooksmithUnityFog.w - z) / max(_LooksmithUnityFog.w - _LooksmithUnityFog.z, 1e-4));
    else if (_LooksmithUnityFog.x < 2.5)
        visible = exp(-_LooksmithUnityFog.y * z);
    else
    {
        float d = _LooksmithUnityFog.y * z;
        visible = exp(-d * d);
    }
    return lerp(_LooksmithUnityFogColor.rgb, color, visible);
}

// Baked lighting, as URP Lit has it: a lightmapped surface takes its indirect light (and fully baked lights) from the
// lightmap instead of the probes, and mixed lights' baked shadows from the shadowmask (probe occlusion elsewhere).
float2 LooksmithLightmapUV(LooksmithGeometry g)
{
    return g.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
}

half4 LooksmithShadowMask(LooksmithGeometry g)
{
    float2 lightmapUV = LooksmithLightmapUV(g);
    half4 mask = SAMPLE_SHADOWMASK(lightmapUV);
    return mask;
}

float3 LooksmithBakedGI(LooksmithGeometry g, float3 N)
{
    #if defined(LIGHTMAP_ON)
        return SampleLightmap(LooksmithLightmapUV(g), N);
    #else
        return SampleSH(N);
    #endif
}

// Standard URP physically based shading; used for PBR Blend, Bypass, and when no Look is active.
float3 LooksmithShadePBR(LooksmithSurface s, LooksmithGeometry g, float4 shadowCoord)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = g.positionWS;
    inputData.normalWS = s.normalWS;
    inputData.viewDirectionWS = g.viewDirWS;
    inputData.shadowCoord = shadowCoord;
    inputData.bakedGI = LooksmithBakedGI(g, s.normalWS);
    inputData.normalizedScreenSpaceUV = g.screenUV;
    inputData.shadowMask = LooksmithShadowMask(g);

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = s.albedo;
    surface.metallic = s.metallic;
    surface.smoothness = s.smoothness;
    surface.occlusion = s.occlusion;
    surface.normalTS = float3(0, 0, 1);
    surface.alpha = 1;
    return UniversalFragmentPBR(inputData, surface).rgb;
}

// PBR Blend: URP's physically based shading (UniversalFragmentPBR), worked out alongside the stylized shading instead of
// on its own. It takes the key light the stylized shading already has (its shadow filtered once, not twice: the soft
// filter is the dearest part of a pixel), the same probe ambient, and its additional lights from the same loop. The sums
// are URP's own, so the picture is the same.
struct LooksmithPBR
{
    BRDFData brdf;
    BRDFData brdfClearCoat;
    InputData inputData;
    SurfaceData surface;
    AmbientOcclusionFactor ao;
    float3 additional;
};

LooksmithPBR LooksmithBeginPBR(LooksmithSurface s, LooksmithGeometry g, float4 shadowCoord, half4 shadowMask, float3 bakedGI)
{
    LooksmithPBR p = (LooksmithPBR)0;
    p.inputData.positionWS = g.positionWS;
    p.inputData.normalWS = s.normalWS;
    p.inputData.viewDirectionWS = g.viewDirWS;
    p.inputData.shadowCoord = shadowCoord;
    p.inputData.bakedGI = bakedGI;
    p.inputData.normalizedScreenSpaceUV = g.screenUV;
    p.inputData.shadowMask = shadowMask;
    p.surface.albedo = s.albedo;
    p.surface.metallic = s.metallic;
    p.surface.smoothness = s.smoothness;
    p.surface.occlusion = s.occlusion;
    p.surface.normalTS = float3(0, 0, 1);
    p.surface.alpha = 1;
    InitializeBRDFData(p.surface, p.brdf);
    p.brdfClearCoat = CreateClearCoatBRDFData(p.surface, p.brdf);
    p.ao = CreateAmbientOcclusionFactor(p.inputData, p.surface);
    return p;
}

// An additional light, as URP's PBR loop takes it (GetAdditionalLight with the InputData adds only screen-space AO).
void LooksmithPBRAddLight(inout LooksmithPBR p, Light light)
{
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
        light.color *= p.ao.directAmbientOcclusion;
    #endif
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularOff = true;
    #else
    bool specularOff = false;
    #endif
    p.additional += LightingPhysicallyBased(p.brdf, p.brdfClearCoat, light, p.inputData.normalWS, p.inputData.viewDirectionWS,
                                            p.surface.clearCoatMask, specularOff);
}

// The PBR colour, given the key light the stylized shading sampled (URP's GetMainLight(InputData, ...) is that light
// with screen-space AO on top).
float3 LooksmithEndPBR(LooksmithPBR p, Light mainLight)
{
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
        mainLight.color *= p.ao.directAmbientOcclusion;
    #endif
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularOff = true;
    #else
    bool specularOff = false;
    #endif
    MixRealtimeAndBakedGI(mainLight, p.inputData.normalWS, p.inputData.bakedGI);
    LightingData lightingData = CreateLightingData(p.inputData, p.surface);
    lightingData.giColor = GlobalIllumination(p.brdf, p.brdfClearCoat, p.surface.clearCoatMask, p.inputData.bakedGI,
                                              p.ao.indirectAmbientOcclusion, p.inputData.positionWS, p.inputData.normalWS,
                                              p.inputData.viewDirectionWS, p.inputData.normalizedScreenSpaceUV);
    #ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer()))
    #endif
        lightingData.mainLightColor = LightingPhysicallyBased(p.brdf, p.brdfClearCoat, mainLight, p.inputData.normalWS,
                                                              p.inputData.viewDirectionWS, p.surface.clearCoatMask, specularOff);
    lightingData.additionalLightsColor = p.additional;
    return CalculateFinalColor(lightingData, 1).rgb;
}

// One additional light through the Look's ramp (same ramp as the key light, no shadow tint).
void LooksmithAddLight(Light light, LooksmithSurface s, float3 V, inout float3 diffuse, inout float3 specular)
{
    float NdotL = dot(s.normalWS, light.direction);
    float amount = LooksmithRamp(NdotL) * LooksmithShadowEdge(light.shadowAttenuation) * light.distanceAttenuation;
    float3 color = light.color * _LooksmithTerminatorParams.w;
    diffuse += s.albedo * color * amount;
    float3 H = SafeNormalize(light.direction + V);
    specular += color * (LooksmithSpecularTerm(s.normalWS, H, s.tangentWS, s.smoothness) * amount);
}

float3 LooksmithShadeStylized(LooksmithSurface s, LooksmithGeometry g, float4 shadowCoord, LooksmithScreenTerms terms)
{
    s.normalWS = terms.normalWS;
    float3 N = s.normalWS;
    float3 V = g.viewDirWS;
    half4 shadowMask = LooksmithShadowMask(g);
    uint meshLayers = GetMeshRenderingLayer();

    s.albedo = LooksmithSaturate(s.albedo, _LooksmithSurfaceParams.x);
    float3 diffuseAlbedo = s.albedo * (1.0 - 0.9 * s.metallic);

    // Key (main) light
    Light mainLight = GetMainLight(shadowCoord, g.positionWS, shadowMask);
    #if defined(LIGHTMAP_ON)
        float3 bakedGI = SampleLightmap(LooksmithLightmapUV(g), N);
    #else
        float3 bakedGI = SampleSH(N);
    #endif
    bool pbrBlend = _LooksmithLightParams.y > 1e-3;
    LooksmithPBR pbr = (LooksmithPBR)0;
    if (pbrBlend)
        pbr = LooksmithBeginPBR(s, g, shadowCoord, shadowMask, bakedGI);
    // distance attenuation is 0 where the light is culled for this object, or baked into its lightmap (Subtractive)
    float3 mainColor = mainLight.color * mainLight.distanceAttenuation;
    #ifdef _LIGHT_LAYERS
        if (!IsMatchingLightLayer(mainLight.layerMask, meshLayers))
            mainColor = 0;
    #endif
    float mainIntensity = max(mainColor.r, max(mainColor.g, mainColor.b));
    float3 lightHue = mainIntensity > 1e-4 ? mainColor / mainIntensity : float3(1, 1, 1);
    // The palette's Key colour tints the light's own colour (a warm key warms the sun, never repaints the moon); an
    // Looksmith Sky says how much tint its light accepts (all by day, little by moonlight).
    float3 key = _LooksmithKeyColor.rgb / max(max(_LooksmithKeyColor.r, _LooksmithKeyColor.g), max(_LooksmithKeyColor.b, 1e-4));
    float tintAllowed = _LooksmithSkyHorizon.a > 0.5 ? _LooksmithSkyGround.a : 1.0;
    float3 keyTint = lerp(lightHue, lightHue * key, _LooksmithLightParams.w * tintAllowed) * _LooksmithTerminatorParams.y;
    float3 shadowTint = lerp(_LooksmithShadowColor.rgb, s.shadowTint.rgb, s.shadowTint.a);

    float NdotL = dot(N, mainLight.direction);
    if (_LooksmithLightParams.x > 1e-3)
        NdotL += (terms.brush - 0.5) * _LooksmithLightParams.x * 0.8;
    float shadow = LooksmithShadowEdge(mainLight.shadowAttenuation) * LooksmithCloudShadow(g.positionWS, mainLight.direction);
    float occlusion = LooksmithScreenAO(g.screenUV);
    float light = LooksmithRamp(NdotL) * shadow * lerp(1.0, occlusion, _LooksmithAOParams.y);
    if (s.faceMask > 0.001)
        light = lerp(light, s.faceLight * shadow, s.faceMask);

    float3 lightFactor = lerp(shadowTint, keyTint, light);
    float4 gradient = LooksmithGradientRamp(light);
    lightFactor = lerp(lightFactor, gradient.rgb, gradient.a);
    float3 diffuse = diffuseAlbedo * lightFactor * mainIntensity;

    // Terminator accent: a hue shift where light turns to shadow.
    float terminator = saturate(1.0 - abs(light * 2.0 - 1.0));
    diffuse += diffuseAlbedo * _LooksmithAccentColor.rgb * (terminator * terminator * _LooksmithTerminatorParams.x * mainIntensity);

    // Caustics (Pro water) under the water plane.
    if (_LooksmithCausticParams.y > 0.0)
        diffuse += diffuseAlbedo * keyTint * (LooksmithCaustics(g.positionWS, N) * shadow * mainIntensity);

    float3 H = SafeNormalize(mainLight.direction + V);
    float3 specularLight = mainColor * (LooksmithSpecularTerm(N, H, s.tangentWS, s.smoothness) * light);
    if (s.hairMask > 0.001)
    {
        // Hair: a shifted anisotropic streak, whatever the Look's specular style.
        float3 T = normalize(s.tangentWS + N * s.hairShift);
        float TdotH = dot(T, H);
        float streak = pow(sqrt(saturate(1.0 - TdotH * TdotH)), 80.0) * smoothstep(-0.2, 0.4, NdotL);
        specularLight = lerp(specularLight, mainColor * streak * light * 0.8, s.hairMask);
    }

    // Additional lights get the same ramp.
    float3 addDiffuse = 0;
    float3 addSpecular = 0;
    #if defined(_ADDITIONAL_LIGHTS)
        uint pixelLightCount = GetAdditionalLightsCount();
        #if USE_CLUSTER_LIGHT_LOOP
            InputData inputData = (InputData)0;
            inputData.normalizedScreenSpaceUV = g.screenUV;
            inputData.positionWS = g.positionWS;
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light dirLight = GetAdditionalLight(lightIndex, g.positionWS, shadowMask);
                #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(dirLight.layerMask, meshLayers))
                #endif
                {
                    LooksmithAddLight(dirLight, s, V, addDiffuse, addSpecular);
                    if (pbrBlend)
                        LooksmithPBRAddLight(pbr, dirLight);
                }
            }
        #endif
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light addLight = GetAdditionalLight(lightIndex, g.positionWS, shadowMask);
            #ifdef _LIGHT_LAYERS
            if (IsMatchingLightLayer(addLight.layerMask, meshLayers))
            #endif
            {
                LooksmithAddLight(addLight, s, V, addDiffuse, addSpecular);
                if (pbrBlend)
                    LooksmithPBRAddLight(pbr, addLight);
            }
        LIGHT_LOOP_END
    #endif

    float3 specColor = lerp(_LooksmithSpecColor.rgb, s.albedo, s.metallic);
    float3 specular = (specularLight + addSpecular) * specColor * (_LooksmithSpecParams.x * s.specularAmount);

    // Rim
    float3 rimColor = lerp(_LooksmithRimColor.rgb, s.rimTint.rgb, s.rimTint.a);
    float3 rim = rimColor * (LooksmithRimTermCurved(N, V, NdotL, shadow, terms.curved) * _LooksmithRimParams.x * s.rimAmount * clamp(mainIntensity, 0.25, 1.2));

    // Ambient: SH, flattened toward its average, then tinted toward the palette's sky colour at the environment's own
    // brightness (a bright palette never lights up a night scene). A lightmapped surface uses its lightmap instead, the
    // baked lights (torches, lamps) included; in baked scenes the CPU keeps it at full strength with a light tint.
    #if defined(LIGHTMAP_ON)
        float3 ambient = bakedGI;
        #if defined(_MIXED_LIGHTING_SUBTRACTIVE)
            ambient = SubtractDirectMainLightFromLightmap(mainLight, N, ambient);
        #endif
    #else
        float3 ambient = lerp(bakedGI, SampleSH(float3(0, 0, 0)), _LooksmithAmbientParams.y);
    #endif
    float3 paletteAmbient = _LooksmithSkyColor.rgb * (LooksmithLuminance(ambient) / max(LooksmithLuminance(_LooksmithSkyColor.rgb), 1e-4));
    ambient = lerp(ambient, paletteAmbient, _LooksmithAmbientParams.z);
    ambient *= _LooksmithAmbientParams.x * s.occlusion * occlusion;

    float3 color = diffuse + addDiffuse + diffuseAlbedo * ambient + specular + rim;

    if (_LooksmithAmbientParams.w > 1e-3)
    {
        float3 R = reflect(-V, N);
        float fresnel = pow(1.0 - saturate(dot(N, V)), 4.0);
        float3 reflection = GlossyEnvironmentReflection(R, g.positionWS, 1.0 - s.smoothness, s.occlusion, g.screenUV);
        float3 f0 = lerp(float3(0.04, 0.04, 0.04), s.albedo, s.metallic);
        // the grazing-angle sheen is kept for genuinely glossy surfaces: on matte ones it brightened whole faces as
        // the camera moved
        color += reflection * lerp(f0, 1.0, fresnel * smoothstep(0.55, 0.95, s.smoothness)) * _LooksmithAmbientParams.w;
    }

    if (pbrBlend)
        color = lerp(color, LooksmithEndPBR(pbr, mainLight), _LooksmithLightParams.y);
    color += s.translucency * mainLight.shadowAttenuation;
    return color;
}

#endif // !SHADERGRAPH_PREVIEW

// Full Looksmith shading for one fragment, including emission and aerial perspective.
float3 LooksmithShade(LooksmithSurface s, LooksmithGeometry g)
{
    s.normalWS = normalize(s.normalWS);
    g.viewDirWS = normalize(g.viewDirWS);
    // Two-sided surfaces (leaves, petals, cloth) seen from behind: mirror the view into the front, so the view-dependent
    // terms (rim, specular, reflections) see the back exactly as the front. The light (N.L) is untouched, so a leaf
    // shades the same from either side instead of flaring with rim light from one of them.
    float facing = dot(s.normalWS, g.viewDirWS);
    if (facing < 0.0)
        g.viewDirWS = normalize(g.viewDirWS - 2.0 * facing * s.normalWS);
#ifdef SHADERGRAPH_PREVIEW
    float3 L = normalize(float3(0.5, 0.7, -0.4));
    float lambert = saturate(dot(s.normalWS, L));
    return s.albedo * (0.25 + 0.75 * step(0.2, lambert)) + s.emission;
#else
    float4 shadowCoord = LooksmithShadowCoord(g.positionWS, g.screenUV);
    if (_LooksmithDebugSurface.x > 0.5)
    {
        s.albedo = 0.5;   // Lighting Only debug view
        s.emission = 0;
    }
    LooksmithScreenTerms terms = LooksmithGetScreenTerms(s, g);
    // One exit point: FXC warns "potentially uninitialized variable" on early returns from inlined functions.
    float3 color = 0;
    [branch] if (LooksmithHiddenLayer(g.positionWS, g.screenUV))
        clip(-1.0);
    else if (!LooksmithIsStyledAt(g.positionWS))
    {
        float3 translucent = 0;
        if (any(s.translucency > 0.0))
            translucent = s.translucency * MainLightShadow(shadowCoord, g.positionWS, LooksmithShadowMask(g), _MainLightOcclusionProbes);
        color = LooksmithUnityFog(LooksmithShadePBR(s, g, shadowCoord) + s.emission + translucent, g.positionWS);
    }
    else
        color = LooksmithAtmosphere(LooksmithUnityFog(LooksmithShadeStylized(s, g, shadowCoord, terms) + s.emission, g.positionWS), g.positionWS);
    return color;
#endif
}

#endif // LOOKSMITH_LIGHTING_INCLUDED


