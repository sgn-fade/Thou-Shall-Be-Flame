using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>How much of a camera's view Looksmith lights, now and after a Restyle plan (rendered, not guessed).</summary>
    public sealed class CoverageReport
    {
        public string camera;
        /// <summary>Shares of what the camera sees, sky aside (0..1).</summary>
        public float looksmith, other;
        /// <summary>Share of the frame that is sky or empty (0..1).</summary>
        public float sky;
        /// <summary>With the plan applied (when one was given).</summary>
        public float looksmithAfter, otherAfter;
        public bool hasPlan;
        /// <summary>Shaders that keep their own lighting (after the plan), biggest first, with their share of the view.</summary>
        public readonly List<KeyValuePair<string, float>> keepTheirOwn = new List<KeyValuePair<string, float>>();

        static string P(float x) => Mathf.RoundToInt(x * 100f) + "%";

        /// <summary>One line: "Looksmith lights 12% of what Main Camera sees, 87% after Restyle."</summary>
        public string Summary() => hasPlan && Mathf.Abs(looksmithAfter - looksmith) > 0.005f
            ? $"Looksmith lights {P(looksmith)} of what {camera} sees, {P(looksmithAfter)} after Restyle"
            : $"Looksmith lights {P(hasPlan ? looksmithAfter : looksmith)} of what {camera} sees";

        /// <summary>What it means for the look, in plain words.</summary>
        public string Meaning()
        {
            float rest = hasPlan ? otherAfter : other;
            if (rest < 0.02f)
                return "Everything in view gets the look's lighting: its bands, rim light and palette.";
            var names = string.Join(", ", keepTheirOwn.Take(3).Select(k => $"{k.Key} {P(k.Value)}"));
            return $"{P(rest)} of the view keeps its own shaders ({names}). Those surfaces keep their own lighting, so the " +
                   "look's bands, rim light and palette tint won't show on them; its lines, art styles and grading still do. " +
                   "Restyle's \"Map manually\" converts a shader Looksmith doesn't know." +
                   (rest > 0.4f ? " With this much on other shaders, the screen-wide styles carry the look." : "");
        }
    }

    /// <summary>
    /// Coverage before Apply: renders a camera's view off-screen with every surface in a flat code colour (Looksmith now,
    /// Looksmith after the Restyle plan, or which other shader), and counts. Nothing in the scene is touched: renderers are
    /// drawn from a command buffer, terrains from a coarse copy of their heightmap and tree instances.
    /// </summary>
    public static class LooksmithCoverage
    {
        const string ShaderPath = "Assets/PaleFernStudio/Looksmith/Editor/Scan/LooksmithCoverage.shader";
        const int MaxTrees = 4000;

        sealed class Kind
        {
            public bool now, after;
            public int index;          // other shaders after the plan: 1..; 0 = Looksmith after
            public string shader;
            public Material material;
        }

        public static CoverageReport Measure(Camera camera, RestylePlan plan = null, int width = 256, int height = 144)
        {
            if (camera == null)
                return null;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                return null;

            var converting = new HashSet<Material>();
            if (plan != null)
                foreach (var i in plan.items)
                    if (i.spec != null && i.material != null && LooksmithRestyle.ConvertingGroups.Contains(i.group) && plan.Enabled(i.group))
                        converting.Add(i.material);

            var kinds = new Dictionary<(bool, bool, string, Material), Kind>();
            var otherNames = new List<string>();
            Kind KindOf(Material m)
            {
                var name = m != null && m.shader != null ? m.shader.name : "(missing)";
                bool now = name.StartsWith("Looksmith/");
                bool after = now || (m != null && converting.Contains(m));
                // alpha-cutout surfaces (leaves, grass cards) count only where they're opaque
                var cutout = Cutout(m, out var cutoutMap);
                if (!kinds.TryGetValue((now, after, name, cutout ? m : null), out var k))
                {
                    int index = 0;
                    if (!after)
                    {
                        index = otherNames.IndexOf(name) + 1;
                        if (index == 0)
                        {
                            otherNames.Add(name);
                            index = otherNames.Count;
                        }
                    }
                    k = new Kind { now = now, after = after, index = Mathf.Min(index, 254), shader = name };
                    k.material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    // a vector, not a colour: colours are converted to linear, which would scramble the code
                    k.material.SetVector("_Color", new Vector4(now ? 1f : 0.5f, after ? 1f : 0.5f, (k.index + 1) / 255f, 1f));
                    if (cutout)
                    {
                        k.material.SetTexture("_MainTex", m.GetTexture(cutoutMap));
                        k.material.SetTextureScale("_MainTex", m.GetTextureScale(cutoutMap));
                        k.material.SetTextureOffset("_MainTex", m.GetTextureOffset(cutoutMap));
                        k.material.SetFloat("_Clip", 1f);
                    }
                    kinds.Add((now, after, name, cutout ? m : null), k);
                }
                return k;
            }

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var cmd = new CommandBuffer { name = "Looksmith Coverage" };
            var temp = new List<Object>();
            var previous = RenderTexture.active;
            try
            {
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Color.clear);
                float aspect = width / (float)height;
                var projection = camera.orthographic
                    ? Matrix4x4.Ortho(-camera.orthographicSize * aspect, camera.orthographicSize * aspect, -camera.orthographicSize, camera.orthographicSize, camera.nearClipPlane, camera.farClipPlane)
                    : Matrix4x4.Perspective(camera.fieldOfView, aspect, camera.nearClipPlane, camera.farClipPlane);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, projection);

                // LOD groups: only LOD 0 (the others draw over it)
                var lowerLods = new HashSet<Renderer>();
                foreach (var group in LooksmithFind.All<LODGroup>())
                {
                    var lods = group.GetLODs();
                    for (int l = 1; l < lods.Length; l++)
                        foreach (var r in lods[l].renderers)
                            if (r != null)
                                lowerLods.Add(r);
                }
                foreach (var r in LooksmithFind.All<Renderer>())
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff
                        || lowerLods.Contains(r) || (camera.cullingMask & (1 << r.gameObject.layer)) == 0)
                        continue;
                    var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null)
                        continue;
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < Mathf.Min(mats.Length, mesh.subMeshCount); s++)
                        cmd.DrawRenderer(r, KindOf(mats[s]).material, s, 0);
                }
                foreach (var t in Terrain.activeTerrains)
                    DrawTerrain(cmd, t, camera, KindOf, temp);

                Graphics.ExecuteCommandBuffer(cmd);
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                temp.Add(tex);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false);

                int total = width * height, skyCount = 0, looksmithNow = 0, looksmithAfter = 0;
                var perOther = new Dictionary<int, int>();
                foreach (var p in tex.GetPixels32())
                {
                    if (p.a < 128)
                    {
                        skyCount++;
                        continue;
                    }
                    if (p.r > 191)
                        looksmithNow++;
                    if (p.g > 191)
                        looksmithAfter++;
                    else
                    {
                        int index = p.b - 1;
                        perOther[index] = perOther.TryGetValue(index, out var n) ? n + 1 : 1;
                    }
                }
                float seen = Mathf.Max(total - skyCount, 1);
                var report = new CoverageReport
                {
                    camera = camera.name,
                    sky = skyCount / (float)total,
                    looksmith = looksmithNow / seen,
                    other = 1f - looksmithNow / seen,
                    looksmithAfter = looksmithAfter / seen,
                    otherAfter = 1f - looksmithAfter / seen,
                    hasPlan = plan != null,
                };
                if (total == skyCount)
                {
                    report.looksmith = report.other = report.looksmithAfter = report.otherAfter = 0f;
                }
                foreach (var kv in perOther.OrderByDescending(kv => kv.Value))
                    if (kv.Key >= 1 && kv.Key <= otherNames.Count)
                        report.keepTheirOwn.Add(new KeyValuePair<string, float>(otherNames[kv.Key - 1], kv.Value / seen));
                return report;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                cmd.Release();
                foreach (var k in kinds.Values)
                    Object.DestroyImmediate(k.material);
                foreach (var o in temp)
                    Object.DestroyImmediate(o);
            }
        }

        /// <summary>An alpha-cutout material and the texture holding its alpha (its base map, by name or by inspector name).</summary>
        static bool Cutout(Material m, out string map)
        {
            map = null;
            if (m == null)
                return false;
            bool clip = m.IsKeywordEnabled("_ALPHATEST_ON") || (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f)
                        || (m.renderQueue >= 2450 && m.renderQueue < 2750);
            if (!clip || m.shader == null)
                return false;
            map = LooksmithMaterialConverter.BaseMapNames.FirstOrDefault(n => m.HasProperty(n) && m.GetTexture(n) != null)
                  ?? LooksmithRestyle.PairsFromDisplayNames(m.shader).Where(p => p.target == "_BaseMap").Select(p => p.source).FirstOrDefault(n => m.GetTexture(n) != null);
            return map != null;
        }

        /// <summary>A terrain as a coarse grid of its heightmap, and its tree instances nearest the camera.</summary>
        static void DrawTerrain(CommandBuffer cmd, Terrain terrain, Camera camera, System.Func<Material, Kind> kindOf, List<Object> temp)
        {
            var data = terrain.terrainData;
            if (data == null)
                return;
            var material = terrain.materialTemplate != null ? terrain.materialTemplate
                : GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.defaultTerrainMaterial : null;
            const int res = 65;
            var size = data.size;
            var vertices = new Vector3[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)(res - 1), v = z / (float)(res - 1);
                    vertices[z * res + x] = new Vector3(u * size.x, data.GetInterpolatedHeight(u, v), v * size.z);
                }
            var triangles = new int[(res - 1) * (res - 1) * 6];
            int t = 0;
            for (int z = 0; z < res - 1; z++)
                for (int x = 0; x < res - 1; x++)
                {
                    int i = z * res + x;
                    triangles[t++] = i; triangles[t++] = i + res; triangles[t++] = i + 1;
                    triangles[t++] = i + 1; triangles[t++] = i + res; triangles[t++] = i + res + 1;
                }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = triangles };
            temp.Add(mesh);
            cmd.DrawMesh(mesh, Matrix4x4.Translate(terrain.GetPosition()), kindOf(material).material, 0, 0);

            if (!terrain.drawTreesAndFoliage || data.treeInstanceCount == 0)
                return;
            // each prototype's LOD 0 meshes, relative to its root
            var parts = new List<(Mesh mesh, int submesh, Matrix4x4 local, Material material)>[data.treePrototypes.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                parts[p] = new List<(Mesh, int, Matrix4x4, Material)>();
                var prefab = data.treePrototypes[p].prefab;
                if (prefab == null)
                    continue;
                var lod = prefab.GetComponent<LODGroup>();
                var renderers = lod != null && lod.GetLODs().Length > 0 ? lod.GetLODs()[0].renderers : prefab.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    var filter = r != null ? r.GetComponent<MeshFilter>() : null;
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var local = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < Mathf.Min(mats.Length, filter.sharedMesh.subMeshCount); s++)
                        parts[p].Add((filter.sharedMesh, s, local, mats[s]));
                }
            }
            var origin = terrain.GetPosition();
            var eye = camera.transform.position;
            foreach (var tree in data.treeInstances
                         .Select(i => (i, pos: Vector3.Scale(i.position, size) + origin))
                         .OrderBy(x => (x.pos - eye).sqrMagnitude).Take(MaxTrees))
            {
                if (tree.i.prototypeIndex < 0 || tree.i.prototypeIndex >= parts.Length)
                    continue;
                var world = Matrix4x4.TRS(tree.pos, Quaternion.AngleAxis(tree.i.rotation * Mathf.Rad2Deg, Vector3.up),
                    new Vector3(tree.i.widthScale, tree.i.heightScale, tree.i.widthScale));
                foreach (var part in parts[tree.i.prototypeIndex])
                    cmd.DrawMesh(part.mesh, world * part.local, kindOf(part.material).material, part.submesh, 0);
            }
        }
    }
}
