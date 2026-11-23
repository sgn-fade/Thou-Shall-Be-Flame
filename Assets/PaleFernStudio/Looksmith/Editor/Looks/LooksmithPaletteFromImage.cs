using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Palette from Image: k-means in OKLab finds an image's main colours, then each of the Look's five palette roles
    /// takes the cluster that fits it (Key: the bright light, Shadow: the dark side, Sky: the large light area, Accent: the
    /// most colourful, Fog: the image's airy average). Each role keeps the cluster's hue and is kept in the lightness
    /// range that role works in, so any picture gives a usable Look.
    /// </summary>
    public static class LooksmithPaletteFromImage
    {
        public const int DefaultClusters = 8;
        public static readonly string[] Roles = { "Key", "Shadow", "Sky", "Accent", "Fog" };

        public sealed class Cluster
        {
            public Vector3 lab;
            public float weight;
            public Vector3 lch => ToLch(lab);
        }

        public sealed class Result
        {
            /// <summary>OKLCH per role: key, shadow, sky, accent, fog.</summary>
            public Vector3[] roles = new Vector3[5];
            /// <summary>The clusters found, largest first.</summary>
            public List<Cluster> clusters = new List<Cluster>();
        }

        /// <summary>Reads any texture (readable or not) at a small size and extracts its palette.</summary>
        public static Result Extract(Texture texture, int clusters = DefaultClusters, int seed = 1)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));
            const int size = 128;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                read.Apply(false);
                return Extract(read.GetPixels(), clusters, seed);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        /// <summary>Extracts the palette from sRGB pixels (as a colour picker shows them).</summary>
        public static Result Extract(IList<Color> srgbPixels, int clusters = DefaultClusters, int seed = 1)
        {
            var samples = new List<Vector3>(srgbPixels.Count);
            int stride = Mathf.Max(1, srgbPixels.Count / 16384);
            for (int i = 0; i < srgbPixels.Count; i += stride)
            {
                var c = srgbPixels[i];
                if (c.a < 0.5f)
                    continue;
                var lin = c.linear;
                samples.Add(LooksmithColor.LinearToOklab(new Vector3(lin.r, lin.g, lin.b)));
            }
            if (samples.Count == 0)
                throw new ArgumentException("The image has no opaque pixels.");
            var result = new Result { clusters = KMeans(samples, Mathf.Clamp(clusters, 2, 16), seed) };
            AssignRoles(result);
            return result;
        }

        /// <summary>k-means (k-means++ seeding, deterministic) on OKLab samples. Returns clusters, largest first.</summary>
        public static List<Cluster> KMeans(List<Vector3> samples, int k, int seed = 1, int iterations = 24)
        {
            var rng = new System.Random(seed);
            k = Mathf.Min(k, samples.Count);
            var centres = new List<Vector3> { samples[rng.Next(samples.Count)] };
            var d2 = new float[samples.Count];
            while (centres.Count < k)
            {
                double total = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    float best = float.MaxValue;
                    foreach (var c in centres)
                        best = Mathf.Min(best, (samples[i] - c).sqrMagnitude);
                    d2[i] = best;
                    total += best;
                }
                if (total <= 1e-12)
                    break;   // fewer distinct colours than clusters
                double pick = rng.NextDouble() * total;
                int chosen = samples.Count - 1;
                for (int i = 0; i < samples.Count; i++)
                {
                    pick -= d2[i];
                    if (pick <= 0)
                    {
                        chosen = i;
                        break;
                    }
                }
                centres.Add(samples[chosen]);
            }

            var assignment = new int[samples.Count];
            var sums = new Vector3[centres.Count];
            var counts = new int[centres.Count];
            for (int it = 0; it < iterations; it++)
            {
                bool moved = it == 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    int best = 0;
                    float bestD = float.MaxValue;
                    for (int c = 0; c < centres.Count; c++)
                    {
                        float d = (samples[i] - centres[c]).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = c;
                        }
                    }
                    if (assignment[i] != best)
                        moved = true;
                    assignment[i] = best;
                }
                Array.Clear(sums, 0, sums.Length);
                Array.Clear(counts, 0, counts.Length);
                for (int i = 0; i < samples.Count; i++)
                {
                    sums[assignment[i]] += samples[i];
                    counts[assignment[i]]++;
                }
                for (int c = 0; c < centres.Count; c++)
                    if (counts[c] > 0)
                        centres[c] = sums[c] / counts[c];
                if (!moved)
                    break;
            }
            return Enumerable.Range(0, centres.Count).Where(c => counts[c] > 0)
                .Select(c => new Cluster { lab = centres[c], weight = counts[c] / (float)samples.Count })
                .OrderByDescending(c => c.weight).ToList();
        }

        static void AssignRoles(Result r)
        {
            var all = r.clusters;
            var main = all.Where(c => c.weight >= 0.03f).ToList();
            if (main.Count == 0)
                main = all;
            var taken = new HashSet<Cluster>();
            Cluster Pick(IEnumerable<Cluster> candidates, Func<Cluster, float> score)
            {
                var pool = candidates.Where(c => !taken.Contains(c)).ToList();
                if (pool.Count == 0)
                    pool = all.Where(c => !taken.Contains(c)).ToList();
                if (pool.Count == 0)
                    pool = all;
                var best = pool.OrderByDescending(score).First();
                taken.Add(best);
                return best;
            }

            // Accent first: the most colourful cluster, even a small one (a red door, a flower bed); size only breaks ties.
            var accent = Pick(all.Where(c => c.weight >= 0.01f), c => c.lch.y + 0.05f * Mathf.Sqrt(c.weight));
            // Shadow: the darkest sizeable cluster.
            var shadow = Pick(main, c => -c.lch.x);
            // Sky: a large, light area, cooler hues preferred.
            var sky = Pick(main, c => c.weight * c.lch.x * (1f + 0.5f * Coolness(c.lch.z)));
            // Key: the brightest of what's left, warmer hues preferred.
            var key = Pick(main, c => c.lch.x + 0.15f * (1f - Coolness(c.lch.z)) + 0.5f * c.lch.y);

            var mean = all.Aggregate(Vector3.zero, (acc, c) => acc + c.lab * c.weight);
            var fog = ToLch(mean);

            r.roles[0] = Fit(key.lch, 0.84f, 0.97f, 0.12f);
            r.roles[1] = Fit(shadow.lch, 0.38f, 0.66f, 0.16f);
            r.roles[2] = Fit(sky.lch, 0.62f, 0.92f, 0.14f);
            r.roles[3] = Fit(accent.lch, 0.5f, 0.8f, 0.3f);
            r.roles[4] = Fit(fog, 0.74f, 0.9f, 0.05f);
        }

        // 1 for blue-violet hues (around 250°), 0 for warm hues.
        static float Coolness(float hue) => Mathf.Clamp01(1f - Mathf.Abs(LooksmithColor.HueDelta(hue, 250f)) / 120f);

        static Vector3 Fit(Vector3 lch, float minL, float maxL, float maxC) =>
            new Vector3(Mathf.Clamp(lch.x, minL, maxL), Mathf.Min(lch.y, maxC), lch.z);

        static Vector3 ToLch(Vector3 lab)
        {
            float c = Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z);
            float h = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
            return new Vector3(lab.x, c, h < 0 ? h + 360f : h);
        }
    }
}
