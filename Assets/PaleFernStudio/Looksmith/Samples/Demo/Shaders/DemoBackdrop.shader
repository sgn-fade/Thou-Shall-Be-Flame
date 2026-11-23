// Looksmith demo: the distant mountain ranges around the valley. Layered silhouettes coloured from the Looksmith Sky, so they
// follow the time of day (warm at sunset, dark under the stars): each range is a darker shade of the horizon, fading
// into it with distance and toward its foot (valley mist). Vertex colour: rgb = tint, a = haze (0 near .. 1 lost in the sky).
// The ranges stay out of the depth and normals the Look reads: they fade into the sky on their own, and outlines drawn
// from their edges hung over the horizon as bare scribbles (Comic Line, Pixel Diorama). So they're drawn after the
// skybox (the skybox fills only what no depth covers), behind everything else, writing no depth.
Shader "Looksmith/Samples/Demo Backdrop"
{
    Properties
    {
        _Darken ("Silhouette Darkness", Range(0, 1)) = 0.45
        _Fallback ("Colour Without a Looksmith Sky", Color) = (0.55, 0.62, 0.7, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+600" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Darken;
            float4 _Fallback;
        CBUFFER_END

        float4 _LooksmithSkyHorizon;     // rgb horizon (linear), a = 1 while a Looksmith Sky is active
        float4 _LooksmithSkyZenith;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 color : COLOR;
            float3 normalWS : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.color = v.color;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings i) : SV_Target
            {
                float3 horizon = _LooksmithSkyHorizon.a > 0.5 ? _LooksmithSkyHorizon.rgb : _Fallback.rgb;
                float3 zenith = _LooksmithSkyHorizon.a > 0.5 ? _LooksmithSkyZenith.rgb : _Fallback.rgb * 0.8;
                // a range is the sky's own colour, darker and a little bluer, lit only by the haze around it
                float3 shade = lerp(horizon, zenith, 0.35) * (1.0 - _Darken) * i.color.rgb;
                return half4(lerp(shade, horizon, saturate(i.color.a)), 1.0);
            }
            ENDHLSL
        }

    }
}
