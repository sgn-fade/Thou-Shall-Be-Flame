// Looksmith Sky: a painted procedural sky. Every value comes from globals that the Looksmith Sky component pushes from a Sky
// Preset, so one shared skybox material serves every scene and nothing is written into the material asset.
// The gradient is mirrored on the CPU (LooksmithSkyMath.cs) for the reflection cubemap; keep the two in step.
#ifndef LOOKSMITH_SKY_INCLUDED
#define LOOKSMITH_SKY_INCLUDED

float4 _LooksmithSkyZenith;       // rgb zenith (linear)
#ifndef LOOKSMITH_COMMON_INCLUDED   // LooksmithCommon declares these too (the Look follows the sky)
float4 _LooksmithSkyHorizon;      // rgb horizon, a = 1 while a Looksmith Sky is active
float4 _LooksmithSkyGround;       // rgb below-horizon colour (designed, never black), a = Look key-tint allowance
#endif
float4 _LooksmithSkyGlow;         // rgb sun-side horizon glow, a strength
float4 _LooksmithSkyParams;       // x horizon curve, y exposure, z ground blend (height), w sky clock (s)
float4 _LooksmithSunDir;          // xyz direction toward the sun, w disc radius (radians)
float4 _LooksmithSunColor;        // rgb disc / halo colour, a halo strength
float4 _LooksmithMoonDir;         // xyz direction toward the moon, w disc radius (radians)
float4 _LooksmithMoonColor;       // rgb, a phase (0 new .. 1 full)
float4 _LooksmithStarParams;      // x density, y brightness, z twinkle, w milky way
float4 _LooksmithCloudLit;        // rgb lit side, a cover (0..1)
float4 _LooksmithCloudShade;      // rgb shaded side, a opacity
float4 _LooksmithCloudParams;     // x scale, y speed, z softness, w painted steps (0 = smooth)

float LooksmithSkyHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 LooksmithSkyHash33(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453123);
}

float LooksmithSkyNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = LooksmithSkyHash13(float3(i, 0.0));
    float b = LooksmithSkyHash13(float3(i + float2(1, 0), 0.0));
    float c = LooksmithSkyHash13(float3(i + float2(0, 1), 0.0));
    float d = LooksmithSkyHash13(float3(i + float2(1, 1), 0.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float LooksmithSkyFbm(float2 p)
{
    float v = 0.0, amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        v += LooksmithSkyNoise2(p) * amp;
        p = p * 2.03 + float2(17.1, 9.3);
        amp *= 0.5;
    }
    return v;
}

// The gradient alone: zenith, horizon, designed ground below, and the sun-side glow along the horizon.
float3 LooksmithSkyGradient(float3 dir)
{
    float y = dir.y;
    float up = pow(saturate(y), _LooksmithSkyParams.x);
    float3 col = lerp(_LooksmithSkyHorizon.rgb, _LooksmithSkyZenith.rgb, up);
    float down = saturate(-y / max(_LooksmithSkyParams.z, 1e-3));
    col = lerp(col, _LooksmithSkyGround.rgb, smoothstep(0.0, 1.0, sqrt(down)));

    float2 h = dir.xz;
    float2 s = _LooksmithSunDir.xz;
    float side = dot(h, s) * rsqrt(max(dot(h, h) * dot(s, s), 1e-6));
    float glow = pow(saturate(side * 0.5 + 0.5), 4.0) * exp(-abs(y) * 5.0) * _LooksmithSkyGlow.a;
    col = lerp(col, _LooksmithSkyGlow.rgb, saturate(glow));
    return col;
}

// One layer of stars: a jittered point per 3D cell of the sky sphere, anti-aliased to at least ~1.5 pixels.
float3 LooksmithStarLayer(float3 dir, float scale, float density, float time, float footprint)
{
    float3 p = dir * scale;
    float3 cell = floor(p);
    float h = LooksmithSkyHash13(cell);
    if (h > density)
        return 0;
    float3 rnd = LooksmithSkyHash33(cell);
    float3 c = 0.25 + 0.5 * rnd;
    float d = length(frac(p) - c);
    float pixel = footprint * scale;   // derivatives are taken once, outside this branchy function
    float size = lerp(0.035, 0.11, rnd.x * rnd.x);
    float eff = max(size, pixel * 1.5);
    float shape = saturate(1.0 - d / eff);
    shape *= shape * (size / eff) * 2.5;   // sub-pixel stars stay visible points instead of fading out
    float twinkle = lerp(1.0, 0.55 + 0.45 * sin(time * (1.5 + 4.0 * rnd.y) + rnd.z * 6.2831), _LooksmithStarParams.z);
    float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.88, 0.72), rnd.z);
    return tint * shape * twinkle * (0.6 + 1.4 * rnd.y);
}

float3 LooksmithSkyColor(float3 dir)
{
    dir = normalize(dir);
    float footprint = length(fwidth(dir));
    float time = _LooksmithSkyParams.w;
    float y = dir.y;
    float3 col = LooksmithSkyGradient(dir);
    float above = smoothstep(-0.004, 0.004, y);

    // Clouds: a painted layer on a flattened dome, lit from the sun's side, fading toward the horizon.
    float cloud = 0.0;
    float3 cloudCol = 0;
    if (_LooksmithCloudLit.a > 0.001 && y > 0.0)
    {
        float2 uv = dir.xz / (y + 0.12) * _LooksmithCloudParams.x + float2(1.0, 0.35) * time * _LooksmithCloudParams.y;
        float n = LooksmithSkyFbm(uv);
        float cover = _LooksmithCloudLit.a;
        float edge = 1.0 - cover;
        float soft = max(_LooksmithCloudParams.z, 0.01);
        cloud = smoothstep(edge - soft, edge + soft, n) * smoothstep(0.0, 0.22, y) * _LooksmithCloudShade.a;
        float2 toSun = _LooksmithSunDir.xz * rsqrt(max(dot(_LooksmithSunDir.xz, _LooksmithSunDir.xz), 1e-6));
        float n2 = LooksmithSkyFbm(uv + toSun * 0.35);
        float lit = saturate((n - n2) * 4.0 + 0.55);
        if (_LooksmithCloudParams.w > 0.5)
        {
            float steps = _LooksmithCloudParams.w;
            float q = lit * steps;
            lit = (floor(q) + smoothstep(0.35, 0.65, frac(q))) / steps;
        }
        cloudCol = lerp(_LooksmithCloudShade.rgb, _LooksmithCloudLit.rgb, lit);
        // A silver lining toward the sun.
        float toward = saturate(dot(dir, normalize(_LooksmithSunDir.xyz)));
        cloudCol += _LooksmithSunColor.rgb * pow(toward, 12.0) * (1.0 - lit) * 0.6 * _LooksmithSunColor.a;
    }

    // Stars, the milky way and the moon live behind the clouds.
    float3 night = 0;
    if (_LooksmithStarParams.y > 0.001)
    {
        float t = time;
        float3 stars = LooksmithStarLayer(dir, 140.0, _LooksmithStarParams.x * 0.45, t, footprint) + LooksmithStarLayer(dir, 65.0, _LooksmithStarParams.x * 0.25, t * 0.7, footprint);
        float3 bandN = normalize(float3(0.32, 0.62, -0.71));
        float band = exp(-pow(dot(dir, bandN) / 0.2, 2.0));
        float dust = LooksmithSkyFbm(dir.xz * 6.0 + dir.y * 3.0);
        float3 milky = float3(0.55, 0.6, 0.85) * band * smoothstep(0.35, 0.8, dust) * 0.12 * _LooksmithStarParams.w;
        night = (stars + milky) * _LooksmithStarParams.y * smoothstep(0.0, 0.18, y);
    }
    if (_LooksmithMoonColor.r + _LooksmithMoonColor.g + _LooksmithMoonColor.b > 0.001)
    {
        float3 m = normalize(_LooksmithMoonDir.xyz);
        float r = _LooksmithMoonDir.w;
        float cosA = dot(dir, m);
        float aa = max(fwidth(cosA), 1e-5);
        float disc = smoothstep(cos(r) - aa, cos(r) + aa, cosA);
        // Phase: a second disc slides across to leave a crescent; the dark side keeps a faint earthshine.
        float3 side = normalize(cross(m, float3(0, 1, 0)) + 1e-4);
        float3 shadowDir = normalize(m + side * (r * 2.0 * _LooksmithMoonColor.a));
        float shadow = smoothstep(cos(r) - aa, cos(r) + aa, dot(dir, shadowDir));
        float lit = disc * (1.0 - shadow * (1.0 - step(0.999, _LooksmithMoonColor.a)));
        float glow = pow(saturate(cosA), 900.0) * 0.35 + pow(saturate(cosA), 60.0) * 0.08;
        night += _LooksmithMoonColor.rgb * (lit + disc * 0.06 + glow * _LooksmithMoonColor.a) * above;
    }
    col += night * (1.0 - cloud);
    col = lerp(col, cloudCol, cloud);

    // The sun: a crisp disc and a soft halo; the ground hides it below the horizon.
    float3 sd = normalize(_LooksmithSunDir.xyz);
    float cosS = dot(dir, sd);
    float aaS = max(fwidth(cosS), 1e-5);
    float sunDisc = smoothstep(cos(_LooksmithSunDir.w) - aaS, cos(_LooksmithSunDir.w) + aaS, cosS);
    float halo = (pow(saturate(cosS), 48.0) * 0.35 + pow(saturate(cosS), 700.0)) * _LooksmithSunColor.a;
    col += _LooksmithSunColor.rgb * (sunDisc * 6.0 * (1.0 - cloud * 0.85) + halo) * above;

    return col * _LooksmithSkyParams.y;
}

#endif // LOOKSMITH_SKY_INCLUDED
