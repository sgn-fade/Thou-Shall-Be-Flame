using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The sky gradient on the CPU (a mirror of LooksmithSky.hlsl's gradient, sun halo and average cloud cover), used for the
    /// reflection cubemap and for tools that need the sky's colour in a direction.
    /// </summary>
    public static class LooksmithSkyMath
    {
        public const int ReflectionSize = 32;

        /// <summary>The sky's colour (linear) in a direction, without stars or individual clouds.</summary>
        public static Color Evaluate(in LooksmithSkyState s, Vector3 dir)
        {
            dir.Normalize();
            float y = dir.y;
            float up = Mathf.Pow(Mathf.Clamp01(y), s.horizonCurve);
            var col = Color.Lerp(s.horizon, s.zenith, up);
            float down = Mathf.Clamp01(-y / Mathf.Max(s.groundBlend, 1e-3f));
            col = Color.Lerp(col, s.ground, Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(down)));

            var sunDir = s.sunDirection;
            var h = new Vector2(dir.x, dir.z);
            var sh = new Vector2(sunDir.x, sunDir.z);
            float side = h.sqrMagnitude > 1e-6f && sh.sqrMagnitude > 1e-6f ? Vector2.Dot(h.normalized, sh.normalized) : 0f;
            float glow = Mathf.Pow(Mathf.Clamp01(side * 0.5f + 0.5f), 4f) * Mathf.Exp(-Mathf.Abs(y) * 5f) * s.glowStrength;
            col = Color.Lerp(col, s.glow, Mathf.Clamp01(glow));

            // Clouds, on average: the sky above the horizon leans toward the cloud colours by their cover.
            if (y > 0f && s.cloudCover > 0f)
            {
                float amount = s.cloudCover * s.cloudOpacity * 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(y / 0.22f));
                col = Color.Lerp(col, Color.Lerp(s.cloudShade, s.cloudLit, 0.6f), amount);
            }

            if (y > 0f)
            {
                float c = Mathf.Clamp01(Vector3.Dot(dir, sunDir));
                float halo = (Mathf.Pow(c, 48f) * 0.35f + Mathf.Pow(c, 700f)) * s.sunHalo;
                col += s.sunColor * halo;
            }
            col *= s.exposure;
            col.a = 1f;
            return col;
        }

        /// <summary>Fills a cubemap with the sky (reflections).</summary>
        public static void Fill(Cubemap cube, in LooksmithSkyState s)
        {
            int n = cube.width;
            var pixels = new Color[n * n];
            for (int face = 0; face < 6; face++)
            {
                for (int py = 0; py < n; py++)
                {
                    for (int px = 0; px < n; px++)
                    {
                        float u = (px + 0.5f) / n * 2f - 1f;
                        float v = (py + 0.5f) / n * 2f - 1f;
                        pixels[py * n + px] = Evaluate(s, FaceDirection((CubemapFace)face, u, v));
                    }
                }
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply(false);
        }

        /// <summary>The direction through a cubemap face pixel (u, v in -1..1, v down the face).</summary>
        public static Vector3 FaceDirection(CubemapFace face, float u, float v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u);
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u);
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v);
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v);
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f);
                default: return new Vector3(-u, -v, -1f);
            }
        }

        /// <summary>Changes whenever anything the lighting or the reflection depends on changes.</summary>
        public static int Hash(in LooksmithSkyState s)
        {
            unchecked
            {
                int h = 17;
                void C(Color c) { h = h * 31 + c.GetHashCode(); }
                void F(float f) { h = h * 31 + Mathf.RoundToInt(f * 1000f); }
                C(s.zenith); C(s.horizon); C(s.ground); C(s.glow); F(s.horizonCurve); F(s.groundBlend); F(s.glowStrength); F(s.exposure);
                F(s.sunElevation); F(s.sunAzimuth); F(s.sunIntensity); F(s.sunSize); F(s.sunHalo); F(s.shadowStrength); F(s.lookKeyTint); C(s.sunColor);
                F(s.moonElevation); F(s.moonAzimuth); F(s.moonIntensity); F(s.moonSize); F(s.moonPhase); C(s.moonColor);
                F(s.starDensity); F(s.starBrightness); F(s.twinkle); F(s.milkyWay);
                F(s.cloudCover); F(s.cloudOpacity); F(s.cloudScale); F(s.cloudSpeed); F(s.cloudSoftness); F(s.cloudSteps);
                C(s.cloudLit); C(s.cloudShade); C(s.ambientSky); C(s.ambientEquator); C(s.ambientGround); F(s.ambientIntensity);
                F(s.fog); F(s.fogDensity); C(s.fogColor); F(s.reflectionIntensity);
                return h == 0 ? 1 : h;
            }
        }
    }
}
