using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// OKLab / OKLCH helpers (Björn Ottosson's OKLab). Palettes are authored in OKLCH so ramps and blends step
    /// evenly in perceived lightness; shaders receive linear sRGB.
    /// </summary>
    public static class LooksmithColor
    {
        /// <summary>OKLCH (L 0..1, C 0..~0.37, h degrees) to linear sRGB (unclamped).</summary>
        public static Vector3 OklchToLinear(Vector3 lch)
        {
            float h = lch.z * Mathf.Deg2Rad;
            return OklabToLinear(new Vector3(lch.x, lch.y * Mathf.Cos(h), lch.y * Mathf.Sin(h)));
        }

        public static Vector3 OklabToLinear(Vector3 lab)
        {
            float l_ = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m_ = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s_ = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            float l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
            return new Vector3(
                4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        public static Vector3 LinearToOklab(Vector3 c)
        {
            float l = 0.4122214708f * c.x + 0.5363325363f * c.y + 0.0514459929f * c.z;
            float m = 0.2119034982f * c.x + 0.6806995451f * c.y + 0.1073969566f * c.z;
            float s = 0.0883024619f * c.x + 0.2817188376f * c.y + 0.6299787005f * c.z;
            float l_ = Cbrt(l), m_ = Cbrt(m), s_ = Cbrt(s);
            return new Vector3(
                0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
                1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
                0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_);
        }

        public static Vector3 LinearToOklch(Vector3 c)
        {
            var lab = LinearToOklab(c);
            float chroma = Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z);
            float hue = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
            if (hue < 0) hue += 360f;
            return new Vector3(lab.x, chroma, hue);
        }

        /// <summary>sRGB colour (as authored in a colour picker) to OKLCH.</summary>
        public static Vector3 ColorToOklch(Color srgb) => LinearToOklch(new Vector3(srgb.linear.r, srgb.linear.g, srgb.linear.b));

        /// <summary>OKLCH to an sRGB Color, gamut-clipped by reducing chroma (keeps lightness and hue).</summary>
        public static Color OklchToColor(Vector3 lch)
        {
            var lin = OklchToLinearInGamut(lch);
            return new Color(lin.x, lin.y, lin.z).gamma;
        }

        /// <summary>Linear sRGB inside [0,1]: chroma is reduced until the colour fits.</summary>
        public static Vector3 OklchToLinearInGamut(Vector3 lch)
        {
            lch.x = Mathf.Clamp01(lch.x);
            var c = OklchToLinear(lch);
            if (InGamut(c))
                return c;
            float lo = 0f, hi = lch.y;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (InGamut(OklchToLinear(new Vector3(lch.x, mid, lch.z)))) lo = mid; else hi = mid;
            }
            c = OklchToLinear(new Vector3(lch.x, lo, lch.z));
            return new Vector3(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z));
        }

        static bool InGamut(Vector3 c) => c.x >= -1e-4f && c.y >= -1e-4f && c.z >= -1e-4f && c.x <= 1.0001f && c.y <= 1.0001f && c.z <= 1.0001f;

        static float Cbrt(float v) => v < 0 ? -Mathf.Pow(-v, 1f / 3f) : Mathf.Pow(v, 1f / 3f);

        /// <summary>Hue difference along the shortest arc, in degrees (-180..180).</summary>
        public static float HueDelta(float from, float to)
        {
            float d = Mathf.Repeat(to - from, 360f);
            return d > 180f ? d - 360f : d;
        }

        /// <summary>The design rule for shadows: hue toward blue/violet by 15–25°, chroma kept, lightness down 35–50%.</summary>
        public static Vector3 DeriveShadow(Vector3 keyLch, float hueShift = 20f, float lightnessDrop = 0.42f)
        {
            float target = 280f; // blue-violet
            float d = HueDelta(keyLch.z, target);
            float h = keyLch.z + Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), hueShift);
            return new Vector3(keyLch.x * (1f - lightnessDrop), keyLch.y, Mathf.Repeat(h, 360f));
        }
    }
}
