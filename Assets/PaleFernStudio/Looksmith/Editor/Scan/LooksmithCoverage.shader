// Coverage before Apply (editor only): every surface in one flat code colour, drawn from a command buffer.
Shader "Hidden/Looksmith/Coverage"
{
    Properties
    {
        _Color ("Code", Vector) = (1, 1, 1, 1)   // a vector: a Color property is converted to linear, scrambling the code
        _MainTex ("Alpha (cutout surfaces)", 2D) = "white" {}
        _Clip ("Clip", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _MainTex_ST;
            float _Clip;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes a)
            {
                Varyings v;
                v.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                v.uv = a.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return v;
            }

            half4 Frag(Varyings v) : SV_Target
            {
                if (_Clip > 0.5)
                    clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv).a - 0.5);
                return _Color;
            }
            ENDHLSL
        }
    }
}
