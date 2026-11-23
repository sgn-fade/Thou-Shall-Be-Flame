using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Textures the styles generate at runtime instead of shipping image files: paper (grain and fibres) and the
    /// palette lookup tables. All are tileable and made from fixed seeds, so every machine gets the same image.
    /// </summary>
    public static class LooksmithStyleTextures
    {
        static Texture2D s_Paper;

        /// <summary>Paper: R = surface height (grain + fibres), G = pigment-catching pits (granulation), B = coarse cloudiness.</summary>
        public static Texture2D Paper()
        {
            if (s_Paper != null)
                return s_Paper;
            const int size = 256;
            s_Paper = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Looksmith Paper",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[size * size];
            var fibres = new float[size * size];
            // Fibres: short random strokes splatted into a height field (wrapped, so the tile repeats seamlessly).
            var random = new System.Random(1237);
            for (int f = 0; f < 900; f++)
            {
                float x = (float)random.NextDouble() * size, y = (float)random.NextDouble() * size;
                float angle = (float)random.NextDouble() * Mathf.PI;
                float length = 6f + (float)random.NextDouble() * 22f;
                float strength = 0.25f + (float)random.NextDouble() * 0.5f;
                float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
                for (float t = 0; t < length; t += 0.5f)
                {
                    int ix = ((int)(x + dx * t) % size + size) % size, iy = ((int)(y + dy * t) % size + size) % size;
                    fibres[iy * size + ix] = Mathf.Max(fibres[iy * size + ix], strength * Mathf.Sin(t / length * Mathf.PI));
                }
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float grain = 0.5f * Periodic(u * 64f, v * 64f, 64, 64, 3) + 0.3f * Periodic(u * 128f, v * 128f, 128, 128, 5) + 0.2f * Periodic(u * 16f, v * 16f, 16, 16, 9);
                float height = Mathf.Clamp01(grain * 0.8f + fibres[y * size + x] * 0.45f);
                float pits = Mathf.Clamp01((0.6f * Periodic(u * 48f, v * 48f, 48, 48, 21) + 0.4f * Periodic(u * 96f, v * 96f, 96, 96, 22) - 0.35f) * 2.2f);
                float cloud = 0.6f * Periodic(u * 4f, v * 4f, 4, 4, 31) + 0.4f * Periodic(u * 8f, v * 8f, 8, 8, 32);
                px[y * size + x] = new Color32(ToByte(height), ToByte(pits), ToByte(cloud), 255);
            }
            s_Paper.SetPixels32(px);
            s_Paper.Apply(true, true);
            return s_Paper;
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

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
        static void ResetStatics()
        {
            s_Paper = null;
            LooksmithPaletteLut.Clear();
        }
    }

    /// <summary>
    /// Nearest-colour lookup tables for palettes (32 steps per channel in a 1024×32 strip). A palette is any small texture
    /// (up to 64 texels, read row by row); its table is baked on the GPU, in OKLab, whenever the texture changes.
    /// </summary>
    public static class LooksmithPaletteLut
    {
        public const int Steps = 32;
        public const int MaxColors = 64;

        sealed class Entry
        {
            public RTHandle lut;
            public uint updateCount;
            public int width, height;
            public bool baked;
        }

        static readonly Dictionary<Texture, Entry> s_Entries = new Dictionary<Texture, Entry>();
        static readonly List<Texture> s_Dead = new List<Texture>();

        sealed class BakeData
        {
            public Material material;
            public int pass;
            public Texture palette;
            public Vector4 parameters;
        }

        /// <summary>Number of palette colours a texture provides.</summary>
        public static int ColorCount(Texture palette) => palette == null ? 0 : Mathf.Min(palette.width * palette.height, MaxColors);

        /// <summary>The lookup table for a palette, baked into this frame's graph first if the palette changed.</summary>
        public static TextureHandle Get(RenderGraph renderGraph, Texture palette, Material bakeMaterial, int bakePass)
        {
            Prune();
            if (!s_Entries.TryGetValue(palette, out var entry))
            {
                entry = new Entry
                {
                    lut = RTHandles.Alloc(Steps * Steps, Steps, colorFormat: GraphicsFormat.R8G8B8A8_SRGB, filterMode: FilterMode.Point,
                        wrapMode: TextureWrapMode.Clamp, name: "Looksmith Palette LUT"),
                };
                s_Entries.Add(palette, entry);
            }
            var handle = renderGraph.ImportTexture(entry.lut);
            if (!entry.baked || entry.updateCount != palette.updateCount || entry.width != palette.width || entry.height != palette.height)
            {
                using (var builder = renderGraph.AddRasterRenderPass<BakeData>("Looksmith Palette LUT", out var data))
                {
                    data.material = bakeMaterial;
                    data.pass = bakePass;
                    data.palette = palette;
                    data.parameters = new Vector4(ColorCount(palette), palette.width, 0f, 0f);
                    builder.SetRenderAttachment(handle, 0);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((BakeData d, RasterGraphContext context) =>
                    {
                        // A property block, copied when the draw is recorded: two palettes baked in one frame share the
                        // material, and values set on it would all read as the last palette's.
                        s_BakeBlock.Clear();
                        s_BakeBlock.SetVector(s_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                        s_BakeBlock.SetTexture(LooksmithShaderIDs.Palette, d.palette);
                        s_BakeBlock.SetVector(LooksmithShaderIDs.PaletteParams, d.parameters);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, s_BakeBlock);
                    });
                }
                entry.baked = true;
                entry.updateCount = palette.updateCount;
                entry.width = palette.width;
                entry.height = palette.height;
            }
            return handle;
        }

        static readonly MaterialPropertyBlock s_BakeBlock = new MaterialPropertyBlock();
        static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        static void Prune()
        {
            s_Dead.Clear();
            foreach (var pair in s_Entries)
                if (pair.Key == null)
                    s_Dead.Add(pair.Key);
            foreach (var key in s_Dead)
            {
                s_Entries[key].lut?.Release();
                s_Entries.Remove(key);
            }
        }

        internal static void Clear()
        {
            foreach (var entry in s_Entries.Values)
                entry.lut?.Release();
            s_Entries.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void ReleaseBeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;
#endif
    }
}
