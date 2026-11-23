// Looksmith Finish: the last layer of the Art Style stack, driven by the Look's Finish group.
// Chromatic fringe, grading LUT, paper (on the surfaces or over the screen), vignette (toward the palette's Shadow
// colour) and film grain.
// URP's own bloom and tonemapping run before this; Finish never replaces them.
Shader "Hidden/Looksmith/Finish"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Looksmith Finish"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "../Core/LooksmithStyles.hlsl"

            // _LooksmithParams0: x grain, y grain size (px), z grain seed, w vignette
            // _LooksmithParams1: rgb vignette colour (linear), w paper
            // _LooksmithParams2: x paper uv per pixel, y fringe, z LUT contribution, w aspect
            // _LooksmithParams3: LUT scale/offset (1/width, 1/height, height-1), w unused
            // _LooksmithParams4: xy target size (px), z 1 = paper on the surfaces, w 1 = orthographic
            // _LooksmithParams5: x metres per pixel at 1 m (orthographic: per pixel), y pixels per paper texel, z paper width (texels)
            TEXTURE2D(_LooksmithPaper);
            TEXTURE2D(_LooksmithGradingLut);

            // Paper height and relief on one plane of the world, with texels texelMetres wide, filtered by the pixel's
            // footprint on the plane (dx, dy in metres).
            float2 PaperPlane(float2 p, float texelMetres, float2 dx, float2 dy)
            {
                float tile = texelMetres * _LooksmithParams5.z;
                float2 uv = p / tile;
                float2 gx = dx / tile, gy = dy / tile;
                float2 texel = 1.5 / _LooksmithParams5.zz;
                float h = SAMPLE_TEXTURE2D_GRAD(_LooksmithPaper, sampler_TrilinearRepeat, uv, gx, gy).r;
                float hx = SAMPLE_TEXTURE2D_GRAD(_LooksmithPaper, sampler_TrilinearRepeat, uv + float2(texel.x, 0), gx, gy).r;
                float hy = SAMPLE_TEXTURE2D_GRAD(_LooksmithPaper, sampler_TrilinearRepeat, uv + float2(0, texel.y), gx, gy).r;
                return float2(h, (h - hx) + (hy - h));
            }

            // Paper laid on the surfaces (triplanar, blended across faces): texels nearTexel metres wide out to
            // LOOKSMITH_MARKS_NEAR, so coming closer shows the same paper larger; beyond, they grow in octaves with the distance
            // (eye 0: one size). The relief is a difference, centred on 0: shifted to 0.5 for the contrast-keeping blend.
            float2 SurfacePaper(float3 positionWS, float3 n, float distance, float nearTexel, float3 dPdx, float3 dPdy)
            {
                float2 o = LooksmithMarkOctave(distance);
                float3 w = pow(abs(n), 4.0);
                w /= max(w.x + w.y + w.z, 1e-4);
                float2 fine = 0, coarse = 0;
                float t0 = nearTexel * exp2(o.x), t1 = t0 * 2.0;
                float3 p0 = positionWS + LooksmithOctaveShift(o.x), p1 = positionWS + LooksmithOctaveShift(o.x + 1.0);
                if (w.x > 0.01) { fine += w.x * PaperPlane(p0.zy, t0, dPdx.zy, dPdy.zy); coarse += w.x * PaperPlane(p1.zy, t1, dPdx.zy, dPdy.zy); }
                if (w.y > 0.01) { fine += w.y * PaperPlane(p0.xz, t0, dPdx.xz, dPdy.xz); coarse += w.y * PaperPlane(p1.xz, t1, dPdx.xz, dPdy.xz); }
                if (w.z > 0.01) { fine += w.z * PaperPlane(p0.xy, t0, dPdx.xy, dPdy.xy); coarse += w.z * PaperPlane(p1.xy, t1, dPdx.xy, dPdy.xy); }
                if (o.y <= 0.001)
                    return fine;
                return LooksmithBlendOctaves(fine + float2(0, 0.5), coarse + float2(0, 0.5), o.y) - float2(0, 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 pixel = uv * _LooksmithParams4.xy;

                // Chromatic fringe: red and blue split radially, strongest at the frame edges.
                float3 c;
                float fringe = _LooksmithParams2.y;
                if (fringe > 0.001)
                {
                    float2 d = uv - 0.5;
                    float2 offset = d * dot(d, d) * fringe * 0.035;
                    c.r = LooksmithSource(uv + offset).r;
                    c.g = LooksmithSource(uv).g;
                    c.b = LooksmithSource(uv - offset).b;
                }
                else
                {
                    c = LooksmithSource(uv);
                }

                // Grading LUT (URP's strip layout, sRGB in and out).
                if (_LooksmithParams2.z > 0.001)
                {
                    float3 graded = ApplyLut2D(TEXTURE2D_ARGS(_LooksmithGradingLut, sampler_LinearClamp), saturate(LinearToSRGB(c)), _LooksmithParams3.xyz);
                    c = lerp(c, SRGBToLinear(graded), _LooksmithParams2.z);
                }

                // Paper: grainy, fibrous, lit from the top left; on the surfaces (the sky keeps a flat sheet behind them)
                // or as one sheet over the screen. A sky that keeps its rendered look gets no paper and no grain.
                bool keptSky = LooksmithKeptSky(uv);
                float paper = keptSky ? 0.0 : _LooksmithParams1.w;
                if (paper > 0.001)
                {
                    float2 hr;
                    float raw = _LooksmithParams4.z > 0.5 ? SampleSceneDepth(uv) : 0.0;
                    float eye = LooksmithEyeDepth(raw);
                    // the paper where the surface sits at rest (a swaying leaf carries its paper with it)
                    float3 positionWS = LooksmithRestPosition(uv, LooksmithSurfacePositionWS(uv, raw));
                    float3 dPdx, dPdy;
                    LooksmithSurfaceDerivatives(positionWS, eye, _LooksmithParams5.x, dPdx, dPdy);
                    if (_LooksmithParams4.z > 0.5 && !LooksmithIsSkyDepth(raw))
                    {
                        float3 n = normalize(SampleSceneNormals(uv));
                        bool ortho = _LooksmithParams4.w > 0.5;
                        float nearTexel = _LooksmithParams5.x * (ortho ? 1.0 : LOOKSMITH_MARKS_NEAR) * _LooksmithParams5.y;
                        hr = SurfacePaper(positionWS, n, ortho ? 0.0 : LooksmithMarkDistance(positionWS), nearTexel, dPdx, dPdy);
                    }
                    else
                    {
                        float2 puv = pixel * _LooksmithParams2.x;
                        float2 texel = _LooksmithParams2.xx * 1.5;
                        float h0 = SAMPLE_TEXTURE2D_LOD(_LooksmithPaper, sampler_LinearRepeat, puv, 0).r;
                        float hx = SAMPLE_TEXTURE2D_LOD(_LooksmithPaper, sampler_LinearRepeat, puv + float2(texel.x, 0), 0).r;
                        float hy = SAMPLE_TEXTURE2D_LOD(_LooksmithPaper, sampler_LinearRepeat, puv + float2(0, texel.y), 0).r;
                        hr = float2(h0, (h0 - hx) + (hy - h0));
                    }
                    float h = hr.x;
                    float relief = hr.y;
                    float shade = 1.0 + (h - 0.5) * 0.18 + relief * 0.9;
                    float3 tint = float3(1.0, 0.975, 0.93);
                    c = lerp(c, c * tint * shade, paper);
                }

                // Vignette toward the palette's shadow colour (black when "from palette" is 0).
                float vignette = _LooksmithParams0.w;
                if (vignette > 0.001)
                {
                    float2 d = (uv - 0.5) * float2(_LooksmithParams2.w, 1.0);
                    float r = length(d) * 1.35;
                    float mask = smoothstep(0.45, 1.25, r) * vignette;
                    c = lerp(c, c * _LooksmithParams1.rgb, saturate(mask));
                }

                // Film grain, strongest in the mid-tones; size in pixels at the target resolution.
                float grain = keptSky ? 0.0 : _LooksmithParams0.x;
                if (grain > 0.001)
                {
                    float n = LooksmithValueNoise3(float3(pixel / max(_LooksmithParams0.y, 0.5), _LooksmithParams0.z)) - 0.5;
                    n += (LooksmithHash12(pixel + _LooksmithParams0.z * 17.0) - 0.5) * 0.35;
                    float3 s = LinearToSRGB(saturate(c));
                    float response = 1.0 - abs(LooksmithLuma(s) * 2.0 - 1.0) * 0.6;
                    c = SRGBToLinear(saturate(s + n * grain * 0.16 * response));
                }

                return half4(c, 1.0);
            }
            ENDHLSL
        }

        // A/B compare: the image before the Art Style stack left of the split, the styled image right of it.
        Pass
        {
            Name "Looksmith Split"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "../Core/LooksmithCommon.hlsl"

            TEXTURE2D_X(_LooksmithSplitSource);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (LooksmithSplitPlain(uv.x))
                    return SAMPLE_TEXTURE2D_X_LOD(_LooksmithSplitSource, sampler_LinearClamp, uv, 0);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            }
            ENDHLSL
        }
    }
}
