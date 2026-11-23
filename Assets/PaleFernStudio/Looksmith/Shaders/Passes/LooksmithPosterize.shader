// Looksmith Posterize & Palette (Art Style, Lite): OKLab posterize (lightness, chroma and hue steps), a three-stop
// gradient map and a palette lookup table, each optional. Pass 1 bakes a palette's lookup table (shared with Pixel Art).
Shader "Hidden/Looksmith/Posterize"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "../Core/LooksmithStyles.hlsl"
        ENDHLSL

        Pass
        {
            Name "Looksmith Posterize"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            // _LooksmithParams0: x lightness levels (0 = keep), y chroma levels (0 = keep), z hue steps (0 = keep), w band softness
            // _LooksmithParams1: x dither, y gradient-map amount, z palette amount, w palette dither
            // _LooksmithParams2/3/4: gradient stops in OKLab (shadow, mid, light)
            // _LooksmithParams5: xy target size (px)

            float Quantize(float v, float levels, float softness, float dither)
            {
                float x = v * levels + dither;
                float k = clamp(floor(x), 0.0, levels - 1.0);
                float f = x - floor(x);
                float up = (k < levels - 1.0 && softness > 0.0) ? smoothstep(1.0 - softness, 1.0, f) : 0.0;
                return (k + up + 0.5) / levels;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                uint2 pixel = (uint2)(uv * _LooksmithParams5.xy);
                float3 source = LooksmithSource(uv);
                float dither = LooksmithBayer4(pixel) * _LooksmithParams1.x;

                float3 lab = LooksmithLinearToOklab(source);
                float chroma = length(lab.yz);
                float hue = atan2(lab.z, lab.y);

                if (_LooksmithParams0.x > 0.5)
                    lab.x = Quantize(saturate(lab.x), _LooksmithParams0.x, _LooksmithParams0.w * 0.5, dither);
                if (_LooksmithParams0.y > 0.5)
                    chroma = round(saturate(chroma / 0.32) * _LooksmithParams0.y + dither) / _LooksmithParams0.y * 0.32;
                if (_LooksmithParams0.z > 0.5)
                {
                    float steps = _LooksmithParams0.z / 6.28318530718;
                    hue = round(hue * steps) / steps;
                }
                lab.yz = chroma * float2(cos(hue), sin(hue));

                // Gradient map: lightness picks a colour from a three-stop ramp (in OKLab, so steps look even).
                if (_LooksmithParams1.y > 0.001)
                {
                    // Stops sit at OKLab lightness 0.25 (shadow), 0.6 (mid-grey) and 0.95 (light).
                    float t = saturate(lab.x);
                    float3 g = t < 0.6 ? lerp(_LooksmithParams2.xyz, _LooksmithParams3.xyz, saturate((t - 0.25) / 0.35))
                                       : lerp(_LooksmithParams3.xyz, _LooksmithParams4.xyz, saturate((t - 0.6) / 0.35));
                    lab = lerp(lab, g, _LooksmithParams1.y);
                }

                float3 styled = max(LooksmithOklabToLinear(lab), 0.0);

                // Palette: the nearest palette colour (OKLab distance), dithered between neighbours.
                if (_LooksmithParams1.z > 0.001)
                    styled = lerp(styled, LooksmithPaletteNearest(styled, LooksmithBayer4(pixel) * _LooksmithParams1.w * 0.15), _LooksmithParams1.z);

                return half4(LooksmithStyleMix(uv, source, styled), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Looksmith Palette LUT Bake"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Bake

            TEXTURE2D(_LooksmithPalette);
            float4 _LooksmithPaletteParams;   // x colour count, y palette width

            // One cell of the 32x32x32 table: the nearest palette colour (OKLab distance) to the cell's sRGB colour.
            half4 Bake(Varyings input) : SV_Target
            {
                uint2 p = uint2(input.positionCS.xy);
                float3 cell = float3(p.x % 32u, p.y, p.x / 32u) / 31.0;
                float3 lab = LooksmithLinearToOklab(SRGBToLinear(cell));
                uint count = (uint)_LooksmithPaletteParams.x;
                uint width = max((uint)_LooksmithPaletteParams.y, 1u);
                float best = 1e9;
                float3 nearest = 0;
                for (uint i = 0u; i < 64u; i++)
                {
                    if (i >= count)
                        break;
                    float3 c = LOAD_TEXTURE2D(_LooksmithPalette, int2(i % width, i / width)).rgb;
                    float3 d = LooksmithLinearToOklab(c) - lab;
                    float dist = dot(d, d);
                    if (dist < best)
                    {
                        best = dist;
                        nearest = c;
                    }
                }
                return half4(nearest, 1.0);
            }
            ENDHLSL
        }
    }
}
