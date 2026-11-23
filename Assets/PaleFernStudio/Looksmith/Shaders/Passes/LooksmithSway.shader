// Looksmith Sway: an override shader that draws, for every visible surface the wind sways (a Looksmith material with Wind above
// 0), how far the wind has moved it (world xyz, metres; w 1). Everything that doesn't sway collapses in the vertex stage
// and draws nothing. Visibility is tested against the scene depth texture, like Looksmith Line IDs. Marks laid on surfaces
// (paper, brush strokes) subtract it to lie where a leaf sits at rest, so they ride it as it sways.
// No UnityPerMaterial buffer on purpose: drawn over other shaders' materials, it reads their Wind, Wind Height, Wind
// Flutter and alpha clip by name (a buffer of its own would read theirs with the wrong layout).
Shader "Hidden/Looksmith/Sway"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Wind ("Wind", Float) = 0
        _WindHeight ("Wind Height", Float) = 1
        _WindFlutter ("Wind Flutter", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Looksmith Sway"
            // With the camera's depth buffer bound (read-only), hidden layers fail the depth test before they're
            // shaded; the offset pulls this draw a hair toward the camera, so a surface's own fragments pass however the
            // two shaders round its depth. Without a depth buffer the test is off and the depth texture alone decides.
            ZWrite Off ZTest LEqual Cull Off Blend Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "../Core/LooksmithWind.hlsl"

            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _Wind, _WindHeight, _WindFlutter;
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 offset : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                if (_Wind <= 1e-4)
                    return o;           // still: every vertex at w = 0, the triangle is clipped away
                // the same sway the material's own vertex stage gives it (LooksmithVertex_float)
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                o.offset = LooksmithWindOffset(input.positionOS.xyz, ws, _Wind, _WindHeight, _WindFlutter);
                o.positionCS = TransformWorldToHClip(ws + o.offset);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                // Only the visible surface: our depth must match the scene's.
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float scene = LooksmithEyeDepth(SampleSceneDepth(uv));
                float mine = LooksmithEyeDepth(input.positionCS.z);
                clip(scene * 1.002 + 0.01 - mine);
                return float4(input.offset, 1.0);
            }
            ENDHLSL
        }
    }
}
