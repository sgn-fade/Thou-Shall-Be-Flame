using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Bakes the Look's lighting curve into the ramp atlas (256 x 4, linear):
    /// row 0 = diffuse ramp over N·L (R), row 1 = Gradient Ramp colours over light amount (A = its weight).
    /// Bands, Soft Wrap and Gradient Ramp are just regions of one parameter space, so looks blend smoothly.
    /// </summary>
    public static class LooksmithRamp
    {
        public const int Width = 256, Height = 4;

        public struct Settings
        {
            public int bands;
            public float bandMix, edge, terminator, wrap, spread, gradientWeight;
            public Color gradientShadow, gradientMid, gradientLit; // linear

            public int Hash()
            {
                unchecked
                {
                    int h = bands;
                    h = h * 397 ^ Q(bandMix); h = h * 397 ^ Q(edge); h = h * 397 ^ Q(terminator); h = h * 397 ^ Q(wrap);
                    h = h * 397 ^ Q(spread); h = h * 397 ^ Q(gradientWeight);
                    h = h * 397 ^ gradientShadow.GetHashCode(); h = h * 397 ^ gradientMid.GetHashCode(); h = h * 397 ^ gradientLit.GetHashCode();
                    return h;
                }
            }

            static int Q(float v) => Mathf.RoundToInt(v * 4096f);
        }

        static float Smooth(float a, float b, float x)
        {
            if (b <= a) return x >= a ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The diffuse ramp value for a given N·L (also used by tests and the inspector preview).</summary>
        public static float Evaluate(in Settings s, float nl)
        {
            float t0 = s.terminator;
            float soft = Smooth(t0 - s.wrap - s.edge, t0 + (1f - t0) * 0.85f, nl);
            int n = Mathf.Clamp(s.bands, 1, 5);
            float band = 0f;
            for (int k = 0; k < n; k++)
            {
                float theta = t0 + (float)k / n * s.spread * (1f - t0);
                band += Smooth(theta - s.edge, theta + s.edge, nl) / n;
            }
            return Mathf.Lerp(soft, band, s.bandMix);
        }

        public static Texture2D Create()
        {
            return new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                name = "Looksmith Ramp Atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        public static void Bake(Texture2D tex, in Settings s)
        {
            var px = new Color32[Width * Height];
            for (int x = 0; x < Width; x++)
            {
                float u = x / (float)(Width - 1);
                float r = Evaluate(s, u * 2f - 1f);
                px[x] = new Color(r, 0f, 0f, 1f);
                var g = u < 0.5f ? Color.Lerp(s.gradientShadow, s.gradientMid, u * 2f) : Color.Lerp(s.gradientMid, s.gradientLit, u * 2f - 1f);
                g.a = s.gradientWeight;
                px[Width + x] = g;
                px[2 * Width + x] = new Color32(0, 0, 0, 255);
                px[3 * Width + x] = new Color32(0, 0, 0, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
        }
    }

    /// <summary>Tileable brush-stroke noise for the Painted mode, generated once (no texture asset to ship).</summary>
    public static class LooksmithBrushNoise
    {
        static Texture2D s_Texture;

        public static Texture2D Get()
        {
            if (s_Texture != null)
                return s_Texture;
            const int size = 128;
            s_Texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Looksmith Brush Noise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // Strokes: noise stretched along x, plus finer bristle streaks.
                float n = 0.6f * Periodic(u * 4f, v * 16f, 4, 16, 1) + 0.3f * Periodic(u * 8f, v * 48f, 8, 48, 7) + 0.1f * Periodic(u * 32f, v * 96f, 32, 96, 13);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(n * 255f), 0, 255);
                px[y * size + x] = new Color32(b, b, b, 255);
            }
            s_Texture.SetPixels32(px);
            s_Texture.Apply(true, true);
            return s_Texture;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        static float Periodic(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int Wx(int i) => ((i % px) + px) % px;
            int Wy(int i) => ((i % py) + py) % py;
            float a = Hash(Wx(x0), Wy(y0), seed), b = Hash(Wx(x0 + 1), Wy(y0), seed);
            float c = Hash(Wx(x0), Wy(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wy(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Texture = null;
    }
}
