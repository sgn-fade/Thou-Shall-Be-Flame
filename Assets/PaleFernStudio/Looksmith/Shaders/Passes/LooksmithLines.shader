// Looksmith Lines: screen-space edge detection drawn over the opaque image, before transparents.
// Depth silhouettes (a second-derivative test, so flat ground at grazing angles stays clean) and normal creases in every
// edition; colour edges, object edges and per-layer masks when the Pro object-ID buffer is bound.
// Lines sit on the foreground side of an edge, so their width matches the Look's line weight. Each line is drawn at its
// surface's own depth against the camera's depth buffer, so whatever stands in front of that surface but isn't in the
// depth-normals prepass (a grass field, kept out so blades aren't outlined) still hides it. Thin lines are sampled at
// four points inside the pixel, so their edges are smooth instead of stair-stepped.
Shader "Hidden/Looksmith/Lines"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest LEqual Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Looksmith Lines"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "../Core/LooksmithCommon.hlsl"
            #include "../Core/LooksmithWaterPlane.hlsl"

            float4 _LooksmithLineParams;    // x width (px, already scaled to this resolution), y depth sens, z normal sens, w colour sens
            float4 _LooksmithLineParams2;   // x fade start (m), y fade end (m), z light weight, w colour from surface
            float4 _LooksmithLineParams3;   // x jitter, y wobble, z silhouettes only, w object edges
            float4 _LooksmithLineColor;     // rgb fixed line colour (linear), a = 1 when the opaque colour is bound
            float4 _LooksmithLineIdParams;  // x = 1 when the object-ID buffer is bound (Pro), y = 1 when a line-layer mask is set, z = 1 for the Lines Only debug draw
            TEXTURE2D_X(_LooksmithLineIds);   // r object hash, g 1 = on a line layer

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Smooth value noise in world space: line wobble and jitter stay put when the camera moves.
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x), lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x), lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            float EyeDepth(float2 uv) { return LooksmithSurfaceEyeDepth(uv, SampleSceneDepth(uv)); }   // water counts as the surface

            // Looksmith Grass Field marks its pixels in the scene normals' alpha (-1; every other surface writes 0)
            bool LooksmithIsGrass(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, UnityStereoTransformScreenSpaceTex(uv)).a < -0.5;
            }

            bool IsSky(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 1e-7;
            #else
                return raw >= 1.0 - 1e-7;
            #endif
            }

            // A depth a little nearer the camera than this one (raw, as the depth buffer stores it): the line passes the
            // depth test against its own surface, not against something genuinely in front of it.
            float NearerDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                #if UNITY_REVERSED_Z
                    return saturate(raw + 0.002);
                #else
                    return saturate(raw - 0.002);
                #endif
                }
                float eye = LooksmithEyeDepth(raw);
                eye = max(eye * 0.985 - 0.03, _ProjectionParams.y * 1.01);
                return (1.0 / eye - _ZBufferParams.w) / _ZBufferParams.z;
            }

            // The line strength at a point (depth silhouettes, creases, and the Pro colour and object edges).
            float EdgeAt(float2 c, float2 dx, float2 dy, float3 surface)
            {
                float dC = EyeDepth(c), dL = EyeDepth(c - dx), dR = EyeDepth(c + dx), dD = EyeDepth(c - dy), dU = EyeDepth(c + dy);

                // Depth: positive second derivative = this pixel is in front of its neighbours (foreground side).
                float lap = max(dL + dR - 2.0 * dC, dD + dU - 2.0 * dC) / max(dC, 1e-3);
                float tD = lerp(0.6, 0.015, _LooksmithLineParams.y);
                float edge = _LooksmithLineParams.y > 0.001 ? smoothstep(tD, tD * 1.4, lap) : 0.0;

                // Creases: one-sided (right and up neighbours), ignoring neighbours clearly in front of us.
                if (_LooksmithLineParams3.z < 0.5 && _LooksmithLineParams.z > 0.001)
                {
                    float3 nC = SampleSceneNormals(c), nR = SampleSceneNormals(c + dx), nU = SampleSceneNormals(c + dy);
                    float creaseR = (1.0 - dot(nC, nR)) * step(dC * 0.97, dR);
                    float creaseU = (1.0 - dot(nC, nU)) * step(dC * 0.97, dU);
                    float tN = lerp(0.9, 0.05, _LooksmithLineParams.z);
                    edge = max(edge, smoothstep(tN, tN * 1.3, max(creaseR, creaseU)));
                }

                // Pro inputs: colour edges, object edges.
                if (_LooksmithLineColor.a > 0.5 && _LooksmithLineParams.w > 0.001)
                {
                    float3 cR = SampleSceneColor(c + dx), cU = SampleSceneColor(c + dy);
                    float diff = max(length(sqrt(max(surface, 0.0)) - sqrt(max(cR, 0.0))) * step(dC * 0.97, dR),
                                     length(sqrt(max(surface, 0.0)) - sqrt(max(cU, 0.0))) * step(dC * 0.97, dU));
                    float tC = lerp(0.6, 0.05, _LooksmithLineParams.w);
                    edge = max(edge, smoothstep(tC, tC * 1.3, diff));
                }
                if (_LooksmithLineIdParams.x > 0.5 && _LooksmithLineParams3.w > 0.5)
                {
                    float id0 = LOAD_TEXTURE2D_X(_LooksmithLineIds, c * _ScaledScreenParams.xy).r;
                    float idR = LOAD_TEXTURE2D_X(_LooksmithLineIds, (c + dx) * _ScaledScreenParams.xy).r;
                    float idU = LOAD_TEXTURE2D_X(_LooksmithLineIds, (c + dy) * _ScaledScreenParams.xy).r;
                    float other = max(step(1e-4, abs(idR - id0)) * step(dC * 0.97, dR), step(1e-4, abs(idU - id0)) * step(dC * 0.97, dU));
                    edge = max(edge, other);
                }
                return edge;
            }

            half4 Frag(Varyings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                float raw = SampleSceneDepth(uv);
                depth = NearerDepth(raw);
                if (_LooksmithLineIdParams.z < 0.5 && LooksmithSplitPlain(uv.x))
                    return 0;   // A/B compare: plain side
                if (IsSky(raw))
                    return 0;   // lines belong to objects; the sky side of a silhouette stays clean
                if (LooksmithUnderWater(uv, raw))
                    return 0;   // so does the water: no lines on it, nor on the lake bed seen through it
                if (LooksmithIsGrass(uv))
                    return 0;   // nor the meadow: no line around every blade (lines are drawn on the nearer side of an edge, so
                                // what stands in front of the grass keeps its outline)

                float jitter = _LooksmithLineParams3.x, wobble = _LooksmithLineParams3.y;
                float d0 = LooksmithEyeDepth(raw);
                float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);

                float fade = 1.0 - saturate((d0 - _LooksmithLineParams2.x) / max(_LooksmithLineParams2.y - _LooksmithLineParams2.x, 1e-3));
                if (fade <= 0.0)
                    return 0;

                float width = _LooksmithLineParams.x * lerp(0.6, 1.0, fade);
                width *= 1.0 + (ValueNoise(positionWS * 1.7) - 0.5) * 1.6 * jitter;
                float w = max(width, 1.0);
                float2 texel = 1.0 / _ScaledScreenParams.xy;
                float2 wob = (float2(ValueNoise(positionWS * 0.9), ValueNoise(positionWS * 0.9 + 17.3)) - 0.5) * 2.0 * wobble * w * texel;
                float2 c = uv + wob;
                float2 dx = float2(texel.x * w, 0.0), dy = float2(0.0, texel.y * w);
                if (_LooksmithLineIdParams.x > 0.5 && _LooksmithLineIdParams.y > 0.5 && LOAD_TEXTURE2D_X(_LooksmithLineIds, c * _ScaledScreenParams.xy).g < 0.5)
                    return 0;   // not on a line layer

                float3 surface = _LooksmithLineColor.a > 0.5 ? SampleSceneColor(uv) : float3(0.5, 0.5, 0.5);
                float edge;
                if (width < 2.5)
                {
                    // thin lines: four samples on a rotated grid inside the pixel, for smooth (anti-aliased) edges
                    edge = 0.25 * (EdgeAt(c + float2(0.375, 0.125) * texel, dx, dy, surface) + EdgeAt(c + float2(-0.125, 0.375) * texel, dx, dy, surface)
                                 + EdgeAt(c + float2(-0.375, -0.125) * texel, dx, dy, surface) + EdgeAt(c + float2(0.125, -0.375) * texel, dx, dy, surface));
                }
                else
                    edge = EdgeAt(c, dx, dy, surface);

                // Heavier in shadow, lighter in light.
                float shade = 1.0 - saturate(LooksmithLuminance(surface) * 2.0);
                float alpha = edge * fade * saturate(width) * lerp(1.0 - 0.5 * _LooksmithLineParams2.z, 1.0 + 0.5 * _LooksmithLineParams2.z, shade);

                // Colour: fixed, or a darker, richer shade of the surface the line sits on.
                float3 dark = LooksmithSaturate(surface * 0.28, 1.35);
                float3 color = lerp(_LooksmithLineColor.rgb, dark, _LooksmithLineParams2.w * _LooksmithLineColor.a);
                // Ink only darkens: a line is never lighter than half the surface it's drawn on. A fixed colour is
                // darker than anything by day, but by night the scene sinks far below it and the lines stood out pale
                // on every tree, like frost.
                if (_LooksmithLineColor.a > 0.5)
                {
                    float cap = LooksmithLuminance(surface) * 0.5;
                    float ink = LooksmithLuminance(color);
                    color *= ink > cap ? cap / max(ink, 1e-6) : 1.0;
                }

                // Distant lines sink into the Look's haze like the surfaces around them.
                float haze = LooksmithHazeAmount(positionWS);
                color = lerp(color, LooksmithHazeColor(), haze);
                alpha *= 1.0 - haze * 0.6;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
