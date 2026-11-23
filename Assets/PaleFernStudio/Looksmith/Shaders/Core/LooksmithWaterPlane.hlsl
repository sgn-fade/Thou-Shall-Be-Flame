// The water plane for screen-space edge detection (lines, comic ink, pixel outlines). Looksmith water is transparent, so the
// depth texture holds the lake bed under it: without this, the shore's top read as standing in front of the deep bed and
// got an outline along it, drawn over the water. Set by the Pro water for the surface nearest the camera; y = 0 (always
// so in Lite) leaves depths untouched. Include after URP's Core.hlsl.
#ifndef LOOKSMITH_WATER_PLANE_INCLUDED
#define LOOKSMITH_WATER_PLANE_INCLUDED

#include "LooksmithDepth.hlsl"

float4 _LooksmithWaterPlane;         // x surface height (m), y 1 while a water surface is active
float4 _LooksmithWaterPlaneBounds;   // xy min, zw max of its footprint (world xz)

// True when the scene point at a pixel lies under the water: the pixel shows the water's surface.
bool LooksmithUnderWater(float2 uv, float raw)
{
    if (_LooksmithWaterPlane.y < 0.5 || unity_OrthoParams.w > 0.5)
        return false;
    float h = _LooksmithWaterPlane.x;
    float3 p = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
    return _WorldSpaceCameraPos.y > h && p.y < h - 0.02 && all(p.xz >= _LooksmithWaterPlaneBounds.xy) && all(p.xz <= _LooksmithWaterPlaneBounds.zw);
}

// The eye depth an edge detector should see at a pixel: where the scene point there lies under the water, the depth at
// which the view ray meets the water's surface instead. (Edge detectors also skip outlining water pixels themselves:
// water isn't an object, and its far edge read as standing in front of the bank behind it.)
float LooksmithSurfaceEyeDepth(float2 uv, float raw)
{
    float eye = LooksmithEyeDepth(raw);
    if (LooksmithUnderWater(uv, raw))
    {
        float h = _LooksmithWaterPlane.x;
        float3 p = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
        eye *= (_WorldSpaceCameraPos.y - h) / max(_WorldSpaceCameraPos.y - p.y, 1e-4);     // eye depth scales along the ray
    }
    return eye;
}

// The scene point a pixel shows, with water as a surface: under the water plane, the point where the view ray meets the
// surface. Effects that print marks, paper or fog into the world use it, so over a lake they're laid on the water
// instead of on the bed (where they slid through the water as the camera moved).
float3 LooksmithSurfacePositionWS(float2 uv, float raw)
{
    float3 p = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
    if (LooksmithUnderWater(uv, raw))
    {
        float3 eye = _WorldSpaceCameraPos;
        p = eye + (p - eye) * ((eye.y - _LooksmithWaterPlane.x) / max(eye.y - p.y, 1e-4));
    }
    return p;
}

#endif
