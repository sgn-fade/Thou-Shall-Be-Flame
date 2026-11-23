using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    public enum ScanSeverity
    {
        Blocking = 0,
        Recommended = 1,
        HeadsUp = 2,
        Info = 3,
        Ready = 4,
    }

    /// <summary>One scan result. Recommended findings usually carry a fix (a Changeset item), pre-ticked in the Hub.</summary>
    public sealed class ScanFinding
    {
        public string id;
        public ScanSeverity severity;
        public string title;
        public string detail;
        public LooksmithChange fix;
        public string writes;   // human description of what the fix writes ("URP renderer asset"), or empty

        public override string ToString() => $"[{severity}] {title}";
    }

    /// <summary>
    /// Read-only project scan (plan §4.5). Never writes anything; fixes are offered as changes for the user to tick.
    /// </summary>
    public static class LooksmithScan
    {
        public sealed class Context
        {
            public string unityVersion = Application.unityVersion;
            public RenderPipelineAsset defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            public readonly List<(string quality, RenderPipelineAsset asset)> qualityPipelines = new List<(string, RenderPipelineAsset)>();
            public readonly List<UniversalRenderPipelineAsset> urpAssets = new List<UniversalRenderPipelineAsset>();
            public ColorSpace colorSpace = PlayerSettings.colorSpace;
            public Scene scene = SceneManager.GetActiveScene();
            public BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;

            public static Context Capture()
            {
                var c = new Context();
                var names = QualitySettings.names;
                for (int i = 0; i < names.Length; i++)
                    c.qualityPipelines.Add((names[i], QualitySettings.GetRenderPipelineAssetAt(i)));
                foreach (var a in new[] { c.defaultPipeline }.Concat(c.qualityPipelines.Select(q => q.asset)))
                    if (a is UniversalRenderPipelineAsset urp && !c.urpAssets.Contains(urp))
                        c.urpAssets.Add(urp);
                return c;
            }

            public IEnumerable<ScriptableRendererData> Renderers(UniversalRenderPipelineAsset asset)
            {
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data)
                        yield return data;
            }

            public IEnumerable<ScriptableRendererData> AllRenderers() => urpAssets.SelectMany(Renderers).Distinct();
        }

        public static List<ScanFinding> Run(Context c = null)
        {
            c ??= Context.Capture();
            var findings = new List<ScanFinding>();
            void Add(string id, ScanSeverity s, string title, string detail = "", LooksmithChange fix = null, string writes = "") =>
                findings.Add(new ScanFinding { id = id, severity = s, title = title, detail = detail, fix = fix, writes = writes });

            // Unity version
            var parts = c.unityVersion.Split('.');
            int major = int.TryParse(parts[0], out var mj) ? mj : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var mn) ? mn : 0;
            if (major < 6000 || (major == 6000 && minor < 3))
                Add("unity-version", ScanSeverity.Blocking, $"Unity {c.unityVersion} is too old", "Looksmith needs Unity 6.3 (6000.3) or newer.");
            else
                Add("unity-version", ScanSeverity.Ready, $"Unity {c.unityVersion}");

            // Pipeline
            var active = GraphicsSettings.currentRenderPipeline;
            if (active == null)
                Add("pipeline", ScanSeverity.Blocking, "The Built-in Render Pipeline is active",
                    "Looksmith is URP-only. Unity's Render Pipeline Converter migrates a project to URP; Looksmith never switches pipelines for you.");
            else if (!(active is UniversalRenderPipelineAsset))
                Add("pipeline", ScanSeverity.Blocking, $"{active.GetType().Name} is active", "Looksmith is URP-only (HDRP is not supported).");
            else
                Add("pipeline", ScanSeverity.Ready, "Universal Render Pipeline is active");

            // URP assets in use
            if (c.urpAssets.Count > 0)
                Add("urp-assets", ScanSeverity.Info, $"{c.urpAssets.Count} URP asset(s) in use: {string.Join(", ", c.urpAssets.Select(a => a.name))}",
                    "Fixes apply to each asset separately and are listed per asset.");

            foreach (var asset in c.urpAssets)
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                foreach (var data in c.Renderers(asset))
                {
                    var dataPath = AssetDatabase.GetAssetPath(data);
                    if (data.GetType().Name == "Renderer2DData")
                    {
                        Add("renderer-2d:" + dataPath, ScanSeverity.Blocking, $"{data.name} is a 2D Renderer", "Looksmith's 3D lighting and passes need the Universal Renderer.");
                        continue;
                    }
                    if (data is UniversalRendererData urd)
                    {
                        if (urd.renderingMode == RenderingMode.Deferred || urd.renderingMode == RenderingMode.DeferredPlus)
                            Add("rendering-path:" + dataPath, ScanSeverity.Recommended, $"{data.name} uses the {urd.renderingMode} path",
                                "Looksmith objects are lit in a forward pass, and Looksmith is tuned on Forward+. Deferred and Deferred+ looked the same in the scenes tested, but a scene with many point lights can come out slightly brighter or darker. Keeping Deferred is fine; switch if the lighting looks off.",
                                SetSerializedValueChange.Int(dataPath, "m_RenderingMode", (int)RenderingMode.ForwardPlus, $"Switch {data.name} to Forward+"), "URP renderer asset");
                        else
                            Add("rendering-path:" + dataPath, ScanSeverity.Ready, $"{data.name}: {urd.renderingMode}");
                    }

                    var features = data.rendererFeatures.Where(f => f != null).ToList();
                    var foreign = features.Where(f => !(f is LooksmithRendererFeature)).ToList();
                    if (foreign.Count > 0)
                        Add("third-party-features:" + dataPath, ScanSeverity.HeadsUp, $"{data.name} has {foreign.Count} other renderer feature(s): {string.Join(", ", foreign.Select(f => f.name))}",
                            "Looksmith never moves or removes other features.");
                    if (features.Count(f => f is LooksmithRendererFeature) > 1)
                        Add("duplicate-looksmith-feature:" + dataPath, ScanSeverity.Recommended, $"{data.name} has the Looksmith feature more than once",
                            "Passes would run twice.", new RemoveDuplicateLooksmithFeatureChange { rendererPath = dataPath }, "URP renderer asset");
                }

                if (!asset.supportsHDR)
                {
                    bool mobile = IsMobile(c.buildTarget);
                    Add("hdr:" + assetPath, mobile ? ScanSeverity.Info : ScanSeverity.Recommended, $"HDR is off on {asset.name}",
                        "Glows and bloom on emissive palettes clip without HDR.",
                        mobile ? null : SetSerializedValueChange.Bool(assetPath, "m_SupportsHDR", true, $"Enable HDR on {asset.name}"), mobile ? "" : "URP asset");
                }
                if (!asset.supportsMainLightShadows)
                    Add("main-light-shadows:" + assetPath, ScanSeverity.Recommended, $"Main-light shadows are off on {asset.name}",
                        "Shadow tint and banded shadows need main-light shadows.",
                        SetSerializedValueChange.Bool(assetPath, "m_MainLightShadowsSupported", true, $"Enable main-light shadows on {asset.name}"), "URP asset");
                var addMode = asset.additionalLightsRenderingMode;
                if (addMode != LightRenderingMode.PerPixel)
                    Add("additional-lights:" + assetPath, ScanSeverity.Info, $"Additional lights are {addMode} on {asset.name}",
                        "Looksmith's ramp applies to per-pixel additional lights only.");
                var grd = asset.gpuResidentDrawerMode;
                Add("gpu-resident-drawer:" + assetPath, ScanSeverity.Info, $"GPU Resident Drawer: {grd} on {asset.name}", "Looksmith shaders support it.");
            }

            Add("depth-normals", ScanSeverity.Ready, "Depth, normals and opaque textures",
                "Not needed in the URP asset: Looksmith's passes request them per camera.");

            // Colour space
            if (c.colorSpace == ColorSpace.Gamma)
                Add("color-space", ScanSeverity.Recommended, "The project uses Gamma colour space",
                    "Switch to Linear. Unity reimports textures afterwards.", new ColorSpaceChange { target = ColorSpace.Linear }, "Player Settings");
            else
                Add("color-space", ScanSeverity.Ready, "Linear colour space");

            ScanScene(c, findings);
            ScanMaterials(c, findings);

            // Lightmaps / APV
            if (LightmapSettings.lightmaps != null && LightmapSettings.lightmaps.Length > 0)
                Add("baked-gi", ScanSeverity.Info, "Baked lightmaps present",
                    "Lightmapped surfaces keep their baked light (and mixed lights' shadowmasks) at the strength it was baked with; light probes light everything else the same way. " +
                    "Bake before you restyle: a lightmap bake doesn't see Looksmith materials' colours yet (they bounce light as plain grey).");

            // Platforms
            Add("platform", ScanSeverity.Info, $"Build target: {c.buildTarget}",
                IsMobile(c.buildTarget) ? "Suggested quality tier: Low." : "Suggested quality tier: High.");

            // Version control
            if (VcsGuard.providerActive)
                Add("vcs", ScanSeverity.Info, $"Version control: {UnityEditor.VersionControl.Provider.GetActivePlugin()?.name}", "Looksmith checks files out before writing.");
            if (VcsGuard.gitDetected)
                Add("git", ScanSeverity.Info, "Git repository detected", "Commit before applying Looksmith changes, so you have your own restore point too.");

            return findings;
        }

        static void ScanScene(Context c, List<ScanFinding> findings)
        {
            if (!c.scene.IsValid() || !c.scene.isLoaded)     // a scene being swapped or closed is scanned again once it's back
                return;
            var scenePath = c.scene.path;
            var lights = LooksmithFind.All<Light>()
                .Where(l => l.gameObject.scene == c.scene).ToList();
            if (!lights.Any(l => l.type == LightType.Directional))
                findings.Add(new ScanFinding
                {
                    id = "main-light", severity = ScanSeverity.Recommended, title = "No directional light in the scene",
                    detail = "Looksmith's ramps, shadow tint and rim follow the main light.",
                    fix = string.IsNullOrEmpty(scenePath) ? null : new AddSunChange { scenePath = scenePath }, writes = "scene",
                });

            var volumes = LooksmithFind.All<Volume>()
                .Where(v => v.gameObject.scene == c.scene && v.sharedProfile != null && !v.TryGetComponent<LooksmithLookVolume>(out _)).ToList();
            var grading = volumes.Where(v => v.sharedProfile.TryGet<Tonemapping>(out var t) && t.active ||
                                             v.sharedProfile.TryGet<ColorAdjustments>(out var ca) && ca.active).ToList();
            if (grading.Count > 0)
                findings.Add(new ScanFinding
                {
                    id = "existing-grading", severity = ScanSeverity.HeadsUp, title = $"Existing grading in {grading.Count} volume(s)",
                    detail = $"{string.Join(", ", grading.Take(3).Select(v => v.name))}: the Look Volume goes just above your global volumes, so while a look is on, its tonemapping and colour settings win over the same settings of yours; the rest of your grading (vignette, split toning…) still blends in, and local volumes above it keep their areas. Looksmith never edits your Volume profiles.",
                });

            var swaying = ClearSwayBatchingChange.Find(c.scene);
            if (swaying.Count > 0)
                findings.Add(new ScanFinding
                {
                    id = "swaying-static", severity = ScanSeverity.Recommended,
                    title = $"{swaying.Count} swaying object(s) are static-batched",
                    detail = "Static batching bakes a mesh into world space, so plants with wind slide over the ground instead of swaying. " +
                             "The fix clears Batching Static on them and keeps their other static flags.",
                    fix = new ClearSwayBatchingChange { scenePath = scenePath, count = swaying.Count }, writes = "scene",
                });

            // a camera with Post Processing off never shows a look's URP grading (Stylized Dungeon ships that way)
            var camera = LooksmithLookFit.ReferenceCamera();
            if (camera != null && camera.gameObject.scene == c.scene && SetCameraPostProcessingChange.For(camera) is SetCameraPostProcessingChange pp)
                findings.Add(new ScanFinding
                {
                    id = "camera-post-processing", severity = ScanSeverity.Recommended, title = $"{camera.name} has Post Processing off",
                    detail = "Looksmith's lighting and art styles run either way, but a look's grading (tonemapping, exposure, bloom) goes through URP post-processing, which this camera skips. The fix turns it on for this camera.",
                    fix = pp, writes = "scene",
                });

            // coverage before Apply: how much of the camera's view will get Looksmith's lighting (rendered, not guessed)
            if (camera != null && camera.gameObject.scene == c.scene)
            {
                var coverage = LooksmithCoverage.Measure(camera, LooksmithRestyle.Plan(RestyleScope.Scene));
                if (coverage != null && coverage.sky < 0.999f)
                    findings.Add(new ScanFinding
                    {
                        id = "coverage", severity = coverage.otherAfter > 0.25f ? ScanSeverity.HeadsUp : ScanSeverity.Info,
                        title = coverage.Summary(),
                        detail = $"From {coverage.camera}'s view, with Restyle's default choices. " + coverage.Meaning(),
                    });
            }

            // someone else's water: transparent, so every screen-space effect sees the bed under it until it's marked
            var water = MarkWaterChange.Unmarked(c.scene);
            if (water.Count > 0 && !string.IsNullOrEmpty(scenePath))
                findings.Add(new ScanFinding
                {
                    id = "water-unmarked", severity = ScanSeverity.Recommended,
                    title = $"{water.Count} water surface(s) Looksmith can't see",
                    detail = $"{string.Join(", ", water.Take(4).Select(w => w.name))}{(water.Count > 4 ? ", …" : "")}: water is transparent, so lines, art-style marks, paper and fog see the bed under it, and marks slide through the water as the camera moves. The fix adds a Looksmith Water Plane to each; the water itself is untouched.",
                    fix = new MarkWaterChange
                    {
                        scenePath = scenePath,
                        objectGlobalIds = water.Select(w => GlobalObjectId.GetGlobalObjectIdSlow(w.gameObject).ToString()).ToList(),
                        names = water.Select(w => w.name).ToList(),
                    },
                    writes = "scene",
                });

            // opaque shaders that don't draw into URP's depth-normals: Looksmith's lines and art styles can't see those objects
            var blind = NoDepthNormals(c.scene);
            if (blind.Count > 0)
                findings.Add(new ScanFinding
                {
                    id = "no-depth-normals", severity = ScanSeverity.HeadsUp,
                    title = $"{blind.Count} shader(s) Looksmith's lines and styles can't see",
                    detail = $"{string.Join(", ", blind.Take(4))}{(blind.Count > 4 ? ", …" : "")} draw no depth-normals pass, so URP's normals texture is empty where they are: " +
                             "outlines miss those objects and the art styles take their marks from whatever is behind them. Restyle converts them where a rule or a guess fits; otherwise ask their author for a DepthNormals pass (Shader Graph adds one).",
                });

            if (RenderSettings.fog)
                findings.Add(new ScanFinding
                {
                    id = "unity-fog", severity = ScanSeverity.HeadsUp, title = "Unity fog is on",
                    detail = "Kept as it is: Looksmith surfaces sit in it like the rest of the scene. A Look's haze adds on top; the Look Advisor suggests keeping one of the two.",
                });

            bool hasLooksmith = LooksmithFind.All<LooksmithLookVolume>(FindObjectsInactive.Include)
                .Any(v => v.gameObject.scene == c.scene);
            findings.Add(new ScanFinding
            {
                id = "scene-styled", severity = hasLooksmith ? ScanSeverity.Ready : ScanSeverity.Info,
                title = hasLooksmith ? "This scene has a Looksmith Look Volume" : "This scene isn't styled yet",
            });
        }

        /// <summary>Shaders on the scene's opaque and cutout surfaces with no DepthNormals pass (Looksmith's own excluded).</summary>
        public static List<string> NoDepthNormals(Scene scene)
        {
            var names = new HashSet<string>();
            if (!scene.IsValid() || !scene.isLoaded)
                return names.ToList();
            var checkedShaders = new Dictionary<Shader, bool>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled)
                        continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null || m.renderQueue >= 2500 || m.shader.name.StartsWith("Looksmith/") || m.shader.name.StartsWith("Hidden/"))
                            continue;
                        if (!checkedShaders.TryGetValue(m.shader, out var has))
                            checkedShaders[m.shader] = has = LooksmithRestyle.HasUrpPass(m.shader, mode => mode == "DepthNormals" || mode == "DepthNormalsOnly");
                        // a shader URP can't draw at all is pink, not invisible: Restyle's "can't draw" group handles it
                        if (!has && LooksmithRestyle.RendersInUrp(m.shader))
                            names.Add(m.shader.name);
                    }
                }
            return names.OrderBy(n => n).ToList();
        }

        public static Dictionary<string, int> MaterialCensus(IEnumerable<Material> materials)
        {
            var census = new Dictionary<string, int>();
            foreach (var m in materials.Where(m => m != null).Distinct())
            {
                var family = ShaderFamily(m.shader);
                census[family] = census.TryGetValue(family, out var n) ? n + 1 : 1;
            }
            return census;
        }

        public static string ShaderFamily(Shader s)
        {
            if (s == null) return "Missing shader";
            var n = s.name;
            if (n.StartsWith("Looksmith/")) return "Looksmith";
            if (n == "Universal Render Pipeline/Lit" || n == "Universal Render Pipeline/Complex Lit") return "URP Lit";
            if (n == "Universal Render Pipeline/Simple Lit") return "URP Simple Lit";
            if (n == "Universal Render Pipeline/Baked Lit") return "URP Baked Lit";
            if (n == "Universal Render Pipeline/Unlit") return "URP Unlit";
            if (n.StartsWith("Universal Render Pipeline/Particles")) return "URP Particles";
            if (n.StartsWith("Universal Render Pipeline/Terrain")) return "URP Terrain";
            if (n == "Autodesk Interactive") return "Autodesk Interactive";
            if (n == "Standard" || n == "Standard (Specular setup)") return "Built-in Standard";
            if (n.StartsWith("Mobile/") || n.StartsWith("Legacy Shaders/")) return "Legacy";
            if (n.StartsWith("Hidden/InternalErrorShader")) return "Broken (error shader)";
            if (AssetDatabase.GetAssetPath(s).EndsWith(".shadergraph")) return "Other Shader Graph";
            return "Custom / third-party";
        }

        static void ScanMaterials(Context c, List<ScanFinding> findings)
        {
            if (!c.scene.IsValid())
                return;
            var mats = LooksmithFind.All<Renderer>(FindObjectsInactive.Include)
                .Where(r => r.gameObject.scene == c.scene).SelectMany(r => r.sharedMaterials);
            var census = MaterialCensus(mats);
            if (census.Count == 0)
                return;
            findings.Add(new ScanFinding
            {
                id = "material-census", severity = ScanSeverity.Info,
                title = $"{census.Values.Sum()} material(s) in this scene",
                detail = string.Join(", ", census.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")),
            });
            int foreign = census.Where(kv => kv.Key == "Custom / third-party" || kv.Key == "Other Shader Graph").Sum(kv => kv.Value);
            if (foreign > 0)
                findings.Add(new ScanFinding
                {
                    id = "custom-shaders", severity = ScanSeverity.HeadsUp,
                    title = $"{foreign} material(s) use other shaders",
                    detail = "Restyle skips them and lists them; you can map them manually or leave them as they are (default).",
                });
        }

        /// <summary>True for a Looksmith material that sways in the wind (its vertices move with height in the object).</summary>
        public static bool Sways(Material m) =>
            m != null && m.shader != null && m.shader.name.StartsWith("Looksmith/") &&
            ((m.HasFloat("_Wind") && m.GetFloat("_Wind") > 1e-4f) || (m.HasFloat("_WindFlutter") && m.HasFloat("_Wind") && m.GetFloat("_WindFlutter") > 1e-4f && m.GetFloat("_Wind") > 1e-4f));

        static bool IsMobile(BuildTarget t) => t == BuildTarget.Android || t == BuildTarget.iOS || t == BuildTarget.WebGL;

        /// <summary>Default set of fixes the Hub pre-ticks: every Recommended finding's fix.</summary>
        public static List<LooksmithChange> RecommendedFixes(IEnumerable<ScanFinding> findings) =>
            findings.Where(f => f.severity == ScanSeverity.Recommended && f.fix != null).Select(f => f.fix).ToList();
    }
}
