// Depth and view rays for any camera. An orthographic camera's depth buffer is linear and its view rays are parallel,
// starting from its plane rather than its position: effects that took eye depth as perspective, or measured from the
// camera's position, drew rings and discs centred under an orthographic camera. Include after URP's Core.hlsl.
#ifndef LOOKSMITH_DEPTH_INCLUDED
#define LOOKSMITH_DEPTH_INCLUDED

// Eye (view-space) depth of a raw depth-buffer value.
float LooksmithEyeDepth(float raw)
{
    if (unity_OrthoParams.w > 0.5)
    {
    #if UNITY_REVERSED_Z
        raw = 1.0 - raw;
    #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw, _ZBufferParams);
}

// Where the view ray reaching a world position starts: the camera, or for an orthographic camera its spot on the
// camera's plane.
float3 LooksmithRayOrigin(float3 positionWS)
{
    if (unity_OrthoParams.w > 0.5)
    {
        float3 back = UNITY_MATRIX_V[2].xyz;       // the camera's backward axis in world space
        return positionWS - back * dot(positionWS - _WorldSpaceCameraPos, back);
    }
    return _WorldSpaceCameraPos;
}

// How far a world position is from the eye along its view ray.
float LooksmithViewDistance(float3 positionWS)
{
    return distance(positionWS, LooksmithRayOrigin(positionWS));
}

#endif
