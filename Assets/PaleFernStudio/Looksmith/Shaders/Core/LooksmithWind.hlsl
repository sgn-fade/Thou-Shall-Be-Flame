// The Looksmith Wind: the breeze's state where something stands and the sway it gives a vertex. Shared by the Looksmith graphs
// (LooksmithSurface), the grass field and Looksmith Sway, which must draw swaying things exactly where they are. Include after
// URP's Core.hlsl.
#ifndef LOOKSMITH_WIND_INCLUDED
#define LOOKSMITH_WIND_INCLUDED

#include "LooksmithCommon.hlsl"

float4 _LooksmithWind;    // xy direction (xz), z speed, w gustiness; zero = a gentle default breeze
float4 _LooksmithWind2;   // x strength (global multiplier), y gust wave size (per m), z gust wave speed (m/s), w unused
float4 _LooksmithWindClock;   // x seconds, y 1 when held (LooksmithWind.SetTimeOverride); else the shader clock

float LooksmithWindClock() { return _LooksmithWindClock.y > 0.5 ? _LooksmithWindClock.x : _Time.y; }

// The Looksmith Wind where something stands: its direction (xz), the wind's clock, the global strength and the gust at
// that spot (about 0..1.9; 1 = the mean). Gusts roll across the world as noise drifting downwind.
struct LooksmithWindState
{
    float2 dir;
    float  time;
    float  strength;
    float  gust;
};

LooksmithWindState LooksmithWindAt(float2 positionXZ)
{
    LooksmithWindState w;
    w.dir = dot(_LooksmithWind.xy, _LooksmithWind.xy) > 1e-6 ? normalize(_LooksmithWind.xy) : float2(0.8, 0.6);
    float speed = _LooksmithWind.z > 0.0 ? _LooksmithWind.z : 1.0;
    float gusts = _LooksmithWind.w > 0.0 ? _LooksmithWind.w : 0.4;
    w.strength = _LooksmithWind2.x > 0.0 ? _LooksmithWind2.x : 1.0;
    float waveScale = _LooksmithWind2.y > 0.0 ? _LooksmithWind2.y : 0.045;
    float waveSpeed = _LooksmithWind2.z > 0.0 ? _LooksmithWind2.z : 5.0;
    w.time = LooksmithWindClock() * speed;
    float2 wp = (positionXZ - w.dir * (w.time * waveSpeed)) * waveScale;
    float wave = LooksmithValueNoise2(wp) * 0.7 + LooksmithValueNoise2(wp * 2.3 + 1.7) * 0.3;
    w.gust = lerp(1.0, smoothstep(0.25, 0.85, wave) * 1.9, gusts);
    return w;
}

// Wind sway: the higher a vertex sits in its object (up to WindHeight metres), the further it leans with the breeze.
// Gusts roll across the world as waves (noise drifting downwind), so a meadow ripples in fronts and neighbouring
// trees lean together then recover; each object also sways on its own phase. Flutter adds a quick per-vertex
// shiver (leaves). Wind is the sway in metres at full height. Off (0) on every material unless set.
float3 LooksmithWindOffset(float3 positionOS, float3 positionWS, float wind, float windHeight, float flutter)
{
    LooksmithWindState w = LooksmithWindAt(positionWS.xz);
    float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
    float h = saturate(positionOS.y / max(windHeight, 0.01));
    float bend = h * h;
    float phase = dot(pivot.xz, float2(0.13, 0.17)) + w.time;
    float sway = (sin(phase * 1.3) * 0.35 + sin(phase * 2.1 + 1.3) * 0.25 + 0.55) * w.gust * w.strength;
    float shiver = sin(LooksmithWindClock() * 9.0 + dot(positionWS, float3(1.7, 2.3, 1.1))) * flutter * h * (0.5 + w.gust);
    float3 offset = float3(w.dir.x, 0.0, w.dir.y) * (sway * bend * wind) + float3(1.0, 0.5, 1.0) * (shiver * 0.04 * wind);
    offset.y -= bend * wind * wind * 0.15 * sway;     // bending tips dip a little instead of stretching
    return offset;
}

#endif
