Shader "Hidden/Looksmith/DebugView"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Looksmith Debug View"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "../Core/LooksmithDepth.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            float _LooksmithDebugMode;

            half3 ViewNormals(float2 uv)  { return SampleSceneNormals(uv) * 0.5 + 0.5; }
            half3 ViewDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                float eye = LooksmithEyeDepth(raw);
                return (half3)saturate(1.0 - log2(1.0 + eye) / 8.0);
            }
            half3 ViewOpaque(float2 uv)   { return 1.0 - SampleSceneColor(uv); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                int mode = (int)_LooksmithDebugMode;

                if (mode == 1) return half4(ViewNormals(uv), 1);
                if (mode == 2) return half4(ViewDepth(uv), 1);
                if (mode == 3) return half4(ViewOpaque(uv), 1);
                if (mode == 5)
                {
                    // Value Check: perceived lightness only.
                    half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                    return half4((half3)dot(c, half3(0.2126, 0.7152, 0.0722)), 1);
                }
                if (mode == 8) return half4(1, 1, 1, 1);
                if (mode == 4)
                {
                    // Quadrants: top-left final colour, top-right normals,
                    // bottom-left depth, bottom-right inverted opaque colour.
                    float2 q = frac(uv * 2.0);
                    bool right = uv.x >= 0.5;
                    bool top = uv.y >= 0.5;
                    if (top && !right) return half4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, q).rgb, 1);
                    if (top && right) return half4(ViewNormals(q), 1);
                    if (!top && !right) return half4(ViewDepth(q), 1);
                    return half4(ViewOpaque(q), 1);
                }
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            }
            ENDHLSL
        }
    }
}
