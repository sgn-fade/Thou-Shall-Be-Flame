// Shader Graph entry points (Custom Function nodes, File mode). Buyers can call these from their own graphs;
// the "Looksmith Lighting" sub graph wraps LooksmithLighting_float.
#ifndef LOOKSMITH_SURFACE_INCLUDED
#define LOOKSMITH_SURFACE_INCLUDED

#include "LooksmithLighting.hlsl"
#include "LooksmithWind.hlsl"

// Samples the standard surface maps (URP Lit property names, so Restyle maps 1:1).
void LooksmithSurface_float(UnityTexture2D BaseMap, float4 BaseColor, float2 UV, float4 VertexColor, float VertexColorAmount,
    UnityTexture2D NormalMap, float NormalStrength, float3 NormalWS, float3 TangentWS, float3 BitangentWS,
    UnityTexture2D EmissionMap, float4 EmissionColor, UnityTexture2D OcclusionMap, float OcclusionStrength,
    out float3 Albedo, out float Alpha, out float3 NormalOut, out float3 Emission, out float Occlusion)
{
    float2 uv = BaseMap.GetTransformedUV(UV);
    float4 albedo = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, uv) * BaseColor;
    albedo.rgb *= lerp(float3(1, 1, 1), VertexColor.rgb, VertexColorAmount);
    Albedo = albedo.rgb;
    Alpha = albedo.a;

    float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uv), NormalStrength);
    NormalOut = normalize(normalTS.x * TangentWS + normalTS.y * BitangentWS + normalTS.z * NormalWS);

    Emission = SAMPLE_TEXTURE2D(EmissionMap.tex, EmissionMap.samplerstate, uv).rgb * EmissionColor.rgb;
    float ao = SAMPLE_TEXTURE2D(OcclusionMap.tex, OcclusionMap.samplerstate, uv).g;
    Occlusion = lerp(1.0, ao, OcclusionStrength);
}

// The Looksmith lighting model for any surface. Output is final colour (emission and aerial perspective included).
void LooksmithLighting_float(float3 Albedo, float3 NormalWS, float3 Emission, float Smoothness, float Metallic, float Occlusion,
    float4 ShadowTint, float4 RimTint, float RimAmount, float SpecularAmount,
    float3 PositionWS, float3 ViewDirWS, float4 ScreenPosition, float3 TangentWS, float2 LightmapUV, out float3 Color)
{
    LooksmithSurface s;
    LooksmithInitSurface(s);
    s.albedo = Albedo;
    s.normalWS = NormalWS;
    s.tangentWS = TangentWS;
    s.emission = Emission;
    s.smoothness = Smoothness;
    s.metallic = Metallic;
    s.occlusion = Occlusion;
    s.shadowTint = ShadowTint;
    s.rimTint = RimTint;
    s.rimAmount = RimAmount;
    s.specularAmount = SpecularAmount;

    LooksmithGeometry g;
    g.positionWS = PositionWS;
    g.viewDirWS = ViewDirWS;
    g.screenUV = ScreenPosition.xy;
    g.lightmapUV = LightmapUV;
    Color = LooksmithShade(s, g);
}

// Vertex hooks shared by every Looksmith graph: wind sway (when the material asks for it), then clip-space vertex
// snapping to a pixel grid (Retro looks; not in the shadow pass).
void LooksmithVertex_float(float3 PositionOS, float Wind, float WindHeight, float WindFlutter, out float3 Out)
{
    Out = PositionOS;
#if !defined(SHADERGRAPH_PREVIEW)
    if (Wind > 1e-4)
    {
        float3 ws = TransformObjectToWorld(PositionOS);
        Out = TransformWorldToObject(ws + LooksmithWindOffset(PositionOS, ws, Wind, WindHeight, WindFlutter));
    }
    #if defined(SHADERPASS) && defined(SHADERPASS_SHADOWCASTER)
        #if SHADERPASS == SHADERPASS_SHADOWCASTER
            return;
        #endif
    #endif
    float snap = _LooksmithVertexParams.x;
    float3 positionWS = TransformObjectToWorld(Out);
    if (snap > 0.5 && LooksmithIsStyledAt(positionWS))
    {
        float4 positionCS = TransformWorldToHClip(positionWS);
        float2 grid = max(_ScreenParams.xy / snap, 1.0) * 0.5;
        float2 ndc = positionCS.xy / positionCS.w;
        ndc = round(ndc * grid) / grid;
        positionCS.xy = ndc * positionCS.w;
        float4 snappedWS = mul(UNITY_MATRIX_I_VP, positionCS);
        Out = TransformWorldToObject(snappedWS.xyz / snappedWS.w);
    }
#endif
}

// Unlit / Gradient: flat colour (texture x colour x vertex colour), an optional world-height gradient, and optionally
// the Look's aerial perspective so unlit props sit in the same air.
void LooksmithUnlit_float(UnityTexture2D BaseMap, float4 BaseColor, float2 UV, float4 VertexColor, float4 GradientBottom,
    float4 GradientTop, float4 GradientRange, float GradientAmount, float LookHaze, float4 EmissionColor, float3 PositionWS,
    out float3 Color, out float Alpha)
{
    float4 c = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, BaseMap.GetTransformedUV(UV)) * BaseColor * VertexColor;
    float h = saturate((PositionWS.y - GradientRange.x) / max(GradientRange.y - GradientRange.x, 1e-3));
    float3 gradient = lerp(GradientBottom.rgb, GradientTop.rgb, h);
    Color = lerp(c.rgb, c.rgb * gradient, GradientAmount) + EmissionColor.rgb;
    Alpha = c.a;
#ifndef SHADERGRAPH_PREVIEW
    if (LookHaze > 0.5 && LooksmithIsStyledAt(PositionWS))
        Color = LooksmithAtmosphere(Color, PositionWS);
#endif
}

#ifndef SHADERGRAPH_PREVIEW
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

// Particles: texture x colour x vertex colour, optional Looksmith lighting, soft fade against the scene (when the Look
// requests depth), and the Look's aerial perspective.
void LooksmithParticle_float(UnityTexture2D BaseMap, float4 BaseColor, float2 UV, float4 VertexColor, float Lit, float SoftDistance,
    float4 EmissionColor, float3 PositionWS, float3 NormalWS, float3 ViewDirWS, float4 ScreenPositionRaw,
    out float3 Color, out float Alpha)
{
    float4 c = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, BaseMap.GetTransformedUV(UV)) * BaseColor * VertexColor;
    Color = c.rgb;
    Alpha = c.a;
#ifndef SHADERGRAPH_PREVIEW
    float2 screenUV = ScreenPositionRaw.xy / max(ScreenPositionRaw.w, 1e-5);
    if (Lit > 0.001)
    {
        LooksmithSurface s;
        LooksmithInitSurface(s);
        s.albedo = c.rgb;
        s.normalWS = NormalWS;
        s.smoothness = 0;
        s.rimAmount = 0;
        LooksmithGeometry g;
        g.positionWS = PositionWS;
        g.viewDirWS = ViewDirWS;
        g.screenUV = screenUV;
        g.lightmapUV = 0;
        Color = lerp(c.rgb, LooksmithShade(s, g), Lit);
    }
    if (SoftDistance > 0.0 && _LooksmithDepthAvailable > 0.5)
    {
        float sceneEye = LooksmithEyeDepth(SampleSceneDepth(screenUV));
        Alpha *= saturate((sceneEye - ScreenPositionRaw.w) / SoftDistance);
    }
    if (LooksmithIsStyledAt(PositionWS) && Lit <= 0.001)
        Color = LooksmithAtmosphere(Color, PositionWS);
#endif
    Color += EmissionColor.rgb;
}

#endif // LOOKSMITH_SURFACE_INCLUDED
