using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    public enum RestyleScope { Selection, Scene, Folder, Project }

    /// <summary>Where a material lives decides how Restyle may change it.</summary>
    public enum MaterialLocation
    {
        Owned,        // a .mat in Assets: swapped in place
        Embedded,     // inside a model in Assets: extracted (Unity's standard step), then swapped
        ReadOnly,     // in a package or built in: a restyled copy plus renderer remaps in the scene
        Unsupported,  // not an asset, or inside another kind of asset
    }

    public sealed class RestyleItem
    {
        public Material material;
        public string assetPath;
        public string sourceShader;
        public MaterialLocation location;
        public string group;
        public string reason;
        public RestyleSpec spec;
        /// <summary>Scene renderer slots using this material (Terrain: slot -1).</summary>
        public readonly List<(Object owner, int slot)> uses = new List<(Object, int)>();
    }

    /// <summary>A dry run: what Restyle would do, grouped, before anything is written.</summary>
    public sealed class RestylePlan
    {
        public RestyleScope scope;
        public string scopeLabel;
        public bool inverse;
        public readonly List<RestyleItem> items = new List<RestyleItem>();
        public readonly Dictionary<string, bool> groupEnabled = new Dictionary<string, bool>
        {
            { LooksmithRestyle.GroupOwned, true }, { LooksmithRestyle.GroupExtract, true }, { LooksmithRestyle.GroupCopies, true },
            { LooksmithRestyle.GroupManual, true }, { LooksmithRestyle.GroupBroken, true }, { LooksmithRestyle.GroupGuessed, false },
        };

        public IEnumerable<RestyleItem> InGroup(string group) => items.Where(i => i.group == group);
        public bool Enabled(string group) => groupEnabled.TryGetValue(group, out var on) && on;
        public int ConvertibleCount => items.Count(i => i.spec != null && LooksmithRestyle.ConvertingGroups.Contains(i.group));

        /// <summary>The preview as one Changeset: rules first, then extractions, in-place swaps, copies, remaps.</summary>
        public Changeset ToChangeset()
        {
            var cs = new Changeset { label = inverse ? "Convert to URP (" + scopeLabel + ")" : "Restyle " + scopeLabel };
            var active = items.Where(i => i.spec != null && LooksmithRestyle.ConvertingGroups.Contains(i.group) && Enabled(i.group)).ToList();

            foreach (var shader in active.Where(i => i.group == LooksmithRestyle.GroupManual).Select(i => i.sourceShader).Distinct())
            {
                var spec = active.First(i => i.sourceShader == shader).spec;
                cs.changes.Add(new CreateRestyleRuleChange { rulePath = LooksmithRestyle.UserRulePath(shader), sourceShader = shader, spec = spec });
            }
            var embedded = active.Where(i => i.location == MaterialLocation.Embedded).ToList();
            var references = embedded.Count > 0 ? new EmbeddedReferences(embedded.Select(i => AssetDatabase.AssetPathToGUID(i.assetPath))) : null;
            foreach (var i in embedded)
            {
                var modelDir = Path.GetDirectoryName(i.assetPath).Replace('\\', '/');
                var change = new ExtractAndRestyleChange
                {
                    modelPath = i.assetPath, materialName = i.material.name, sourceShader = i.sourceShader, spec = i.spec, groupName = i.group,
                    extractedPath = LooksmithRestyle.UniquePath($"{modelDir}/Materials/{LooksmithRestyle.SafeName(i.material.name)}.mat", cs),
                };
                references.Find(i.material, change);
                cs.changes.Add(change);
            }
            foreach (var i in active.Where(i => i.location == MaterialLocation.Owned))
                cs.changes.Add(new RestyleMaterialChange { materialPath = i.assetPath, sourceShader = i.sourceShader, spec = i.spec, groupName = i.group });

            var remaps = new Dictionary<(string scene, string group), RemapRenderersChange>();
            foreach (var i in active.Where(i => i.location == MaterialLocation.ReadOnly && i.uses.Count > 0))
            {
                var copyPath = LooksmithRestyle.CopyPathFor(i.material, i.spec, cs, out bool reuse);
                if (!reuse)
                    cs.changes.Add(new GenerateRestyledCopyChange
                    {
                        sourceGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(i.material).ToString(), sourceName = i.material.name,
                        sourceShader = i.sourceShader, copyPath = copyPath, spec = i.spec, groupName = i.group,
                    });
                foreach (var (owner, slot) in i.uses)
                {
                    var scenePath = (owner as Component)?.gameObject.scene.path;
                    if (string.IsNullOrEmpty(scenePath))
                        continue;
                    if (!remaps.TryGetValue((scenePath, i.group), out var remap))
                        remaps[(scenePath, i.group)] = remap = new RemapRenderersChange { scenePath = scenePath, groupName = i.group };
                    remap.entries.Add(new RemapEntry
                    {
                        objectGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(owner).ToString(), slot = slot, fromName = i.material.name, toPath = copyPath,
                    });
                }
            }
            cs.changes.AddRange(remaps.Values);
            return cs;
        }
    }

    /// <summary>
    /// Where a model's own (embedded) materials are referenced directly: prefabs built from the model's pieces, scenes
    /// and other assets. Extraction removes the embedded material, so those references move with it. Each text file
    /// under Assets that mentions one of the models is read once.
    /// </summary>
    sealed class EmbeddedReferences
    {
        static readonly string[] k_Extensions = { ".prefab", ".unity", ".asset", ".anim", ".controller", ".playable" };
        readonly List<(string path, string text)> m_Files = new List<(string, string)>();

        public EmbeddedReferences(IEnumerable<string> modelGuids)
        {
            var needles = modelGuids.Distinct().Select(g => "guid: " + g).ToArray();
            var root = LooksmithBackups.FullPath("Assets");
            foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!k_Extensions.Contains(Path.GetExtension(full).ToLowerInvariant()))
                    continue;
                string text;
                try { text = File.ReadAllText(full); }
                catch (IOException) { continue; }
                if (!text.StartsWith("%YAML") || !needles.Any(n => text.Contains(n)))
                    continue;   // binary-serialised files can't be re-pointed; they're the rare case
                m_Files.Add(("Assets" + full.Substring(root.Length).Replace('\\', '/'), text));
            }
        }

        public void Find(Material embedded, ExtractAndRestyleChange change)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(embedded, out string guid, out long localId))
                return;
            change.embeddedReference = $"{{fileID: {localId}, guid: {guid}, type: 3}}";
            var openScenes = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;
                openScenes.Add(scene.path);
                if (scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Any(r => r.sharedMaterials.Contains(embedded)))
                    change.referencingScenes.Add(scene.path);
            }
            // open scenes are fixed in memory (and saved by whoever applies); closed files on disk
            foreach (var (path, text) in m_Files)
                if (!openScenes.Contains(path) && text.Contains(change.embeddedReference))
                    change.referencingFiles.Add(path);
        }
    }

    /// <summary>
    /// Restyle (P3, P6): converts materials to Looksmith shaders through a previewed, backed-up Changeset. Materials you
    /// own are swapped in place; model materials are extracted first; package materials get copies. Shaders no rule
    /// knows are skipped and listed, never converted silently.
    /// </summary>
    public static class LooksmithRestyle
    {
        public const string GroupOwned = "Your materials";
        public const string GroupExtract = "Inside models (extracted first)";
        public const string GroupCopies = "Package and built-in materials (restyled copies)";
        public const string GroupManual = "Mapped manually";
        public const string GroupBroken = "Guessed: shaders URP can't draw (pink today)";
        public const string GroupGuessed = "Guessed: other shaders that look like low-poly pack shaders";
        public const string GroupSkipped = "Skipped: other shaders (left as they are)";
        public const string GroupAlready = "Already Looksmith";

        public static readonly string[] ConvertingGroups = { GroupOwned, GroupExtract, GroupCopies, GroupManual, GroupBroken, GroupGuessed };
        public static readonly string[] AllGroups = { GroupOwned, GroupExtract, GroupCopies, GroupManual, GroupBroken, GroupGuessed, GroupSkipped, GroupAlready };

        public const string GeneratedMaterials = LooksmithSetup.GeneratedFolder + "/Materials";
        public const string UserRules = LooksmithSetup.GeneratedFolder + "/Restyle Rules";

        // ------------------------------------------------------------------ plan

        public static RestylePlan Plan(RestyleScope scope, string folder = null, bool inverse = false,
            IReadOnlyDictionary<string, RestyleSpec> manual = null, IEnumerable<Object> selection = null)
        {
            var plan = new RestylePlan { scope = scope, inverse = inverse };
            var rules = LooksmithRestyleRule.All();
            var byMaterial = new Dictionary<Material, RestyleItem>();

            void Use(Material m, Object owner, int slot)
            {
                if (m == null)
                    return;
                if (!byMaterial.TryGetValue(m, out var item))
                {
                    item = Classify(m, rules, inverse, manual);
                    if (item == null)
                        return;
                    byMaterial[m] = item;
                    plan.items.Add(item);
                }
                if (owner != null && owner is Component c && c.gameObject.scene.IsValid())
                    item.uses.Add((owner, slot));
            }

            void FromGameObjects(IEnumerable<GameObject> roots)
            {
                foreach (var go in roots)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int s = 0; s < mats.Length; s++)
                            Use(mats[s], r, s);
                    }
                    foreach (var t in go.GetComponentsInChildren<Terrain>(true))
                        Use(TerrainMaterial(t), t, -1);
                }
            }

            switch (scope)
            {
                case RestyleScope.Scene:
                {
                    var scene = SceneManager.GetActiveScene();
                    plan.scopeLabel = string.IsNullOrEmpty(scene.name) ? "Untitled scene" : scene.name;
                    if (scene.IsValid() && scene.isLoaded)     // not while Play Mode swaps the scene for its copy
                        FromGameObjects(scene.GetRootGameObjects());
                    break;
                }
                case RestyleScope.Folder:
                case RestyleScope.Project:
                {
                    var root = scope == RestyleScope.Project || string.IsNullOrEmpty(folder) ? "Assets" : folder;
                    plan.scopeLabel = scope == RestyleScope.Project ? "project" : root;
                    foreach (var m in MaterialsUnder(root))
                        Use(m, null, 0);
                    break;
                }
                case RestyleScope.Selection:
                {
                    var objects = (selection ?? Selection.objects).Where(o => o != null).ToList();
                    plan.scopeLabel = objects.Count == 1 ? objects[0].name : $"{objects.Count} selected";
                    foreach (var o in objects)
                    {
                        var path = AssetDatabase.GetAssetPath(o);
                        if (o is Material mat)
                            Use(mat, null, 0);
                        else if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                            foreach (var m in MaterialsUnder(path))
                                Use(m, null, 0);
                        else if (o is GameObject go)
                        {
                            if (go.scene.IsValid())
                                FromGameObjects(new[] { go });
                            else if (AssetImporter.GetAtPath(path) is ModelImporter)
                                foreach (var m in EmbeddedMaterials(path))
                                    Use(m, null, 0);
                            else
                                FromGameObjects(new[] { go });   // prefab asset: its materials (no scene remaps)
                        }
                    }
                    break;
                }
            }

            // Package materials only reach renderers through scene remaps; without a scene use there's nothing to point.
            foreach (var item in plan.items.Where(i => i.location == MaterialLocation.ReadOnly && i.uses.Count == 0 && i.spec != null))
            {
                item.reason = "In a package and not used by an object in the open scene; Looksmith never edits packages.";
                item.group = GroupSkipped;
                item.spec = null;
            }
            return plan;
        }

        static Material TerrainMaterial(Terrain t)
        {
            if (t.materialTemplate != null)
                return t.materialTemplate;
            return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.defaultTerrainMaterial : null;
        }

        /// <summary>.mat files under a folder plus materials still embedded in models there (extracted ones are skipped).</summary>
        public static IEnumerable<Material> MaterialsUnder(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m != null)
                        yield return m;
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                foreach (var m in EmbeddedMaterials(AssetDatabase.GUIDToAssetPath(guid)))
                    yield return m;
        }

        static IEnumerable<Material> EmbeddedMaterials(string modelPath)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
                yield break;
            var remapped = new HashSet<string>(importer.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name));
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
                if (!remapped.Contains(m.name))
                    yield return m;
        }

        // ------------------------------------------------------------------ classify

        static RestyleItem Classify(Material m, List<LooksmithRestyleRule> rules, bool inverse, IReadOnlyDictionary<string, RestyleSpec> manual)
        {
            bool isLooksmith = m.shader != null && m.shader.name.StartsWith("Looksmith/");
            if (inverse && !isLooksmith)
                return null;   // Convert to URP only looks at Looksmith materials
            var item = new RestyleItem { material = m, assetPath = AssetDatabase.GetAssetPath(m), sourceShader = m.shader != null ? m.shader.name : "(missing shader)" };
            item.location = Locate(m, item.assetPath, out item.reason);
            if (item.location == MaterialLocation.Unsupported)
            {
                item.group = GroupSkipped;
                return item;
            }
            if (!inverse && isLooksmith)
            {
                item.group = GroupAlready;
                return item;
            }
            if (!inverse && UsesOpacityMap(m))
            {
                item.group = GroupSkipped;
                item.reason = "Its see-through comes from a separate opacity map, which Looksmith Lit doesn't have: converted, it would vanish, so Restyle leaves it as it is.";
                return item;
            }
            // Lite has no Looksmith Terrain (Pro): a terrain is left as it is, never guessed (a mesh shader would break it)
            if (!inverse && !LooksmithEdition.isPro && IsTerrain(m.shader))
            {
                item.group = GroupSkipped;
                item.reason = "A terrain: terrains are styled by Looksmith Terrain, which is part of Looksmith Pro, so Lite leaves it as it is.";
                return item;
            }
            // a rule that maps by inspector names steps aside when it finds no base colour (another shader by that name)
            var rule = rules.FirstOrDefault(r => r.inverse == inverse && r.Matches(m.shader) && r.ToSpec(m.shader) != null);
            if (rule != null)
            {
                item.spec = rule.ToSpec(m.shader);
                item.group = LocationGroup(item.location);
            }
            else if (!inverse && manual != null && manual.TryGetValue(item.sourceShader, out var manualSpec))
            {
                item.spec = manualSpec;
                item.group = GroupManual;
            }
            else if (!inverse && Guess(m.shader, rules, out var guessed) && guessed.pairs.Count > 0 && m.renderQueue >= 2750)
            {
                // a transparent master known only by its inspector names (Dark Medieval's trees): where its see-through
                // comes from is unknown, and a wrong guess made the trees vanish
                item.group = GroupSkipped;
                item.reason = "A transparent master known only by its inspector names: where its see-through comes from is unknown, and converted it could vanish, so Restyle leaves it as it is.";
            }
            else if (!inverse && Guess(m.shader, rules, out guessed))
            {
                item.spec = guessed;
                // a shader URP can't draw shows pink as it is, so converting it can only help: on by default
                bool broken = !RendersInUrp(m.shader);
                item.group = broken ? GroupBroken : GroupGuessed;
                item.reason = $"{item.sourceShader} has a {(guessed.vertexColorAmount > 0 ? "vertex-colour " : "")}{guessed.recipe.ToString().ToLowerInvariant()} layout (base colour/texture names Looksmith recognises)." +
                              (broken ? " It's a Built-in pipeline shader URP can't draw (it shows pink), so this guess is on by default." : "");
            }
            else
            {
                item.group = GroupSkipped;
                item.reason = inverse ? "No inverse rule for this Looksmith shader."
                    : IsWater(m.shader) ? "A water shader: its look is the shader itself, so Restyle leaves it as it is."
                    : HasOwnWind(m.shader) ? "Foliage with its own wind: converting it would stop it moving, so Restyle leaves it as it is."
                    : IsEffect(m.shader) ? "A visual effect (fog, smoke, fire…): its look is the shader itself, so Restyle leaves it as it is."
                    : !RendersInUrp(m.shader) ? "No rule knows this shader, and URP can't draw it (it shows pink). Map it manually to convert it."
                    : "No rule knows this shader. Map it manually, or leave it as it is.";
            }
            return item;
        }

        static string LocationGroup(MaterialLocation l) => l switch
        {
            MaterialLocation.Embedded => GroupExtract,
            MaterialLocation.ReadOnly => GroupCopies,
            _ => GroupOwned,
        };

        public static MaterialLocation Locate(Material m, string path, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(path))
            {
                reason = "Not saved as an asset (created at runtime or inside the scene).";
                return MaterialLocation.Unsupported;
            }
            if (path.StartsWith("Packages/") || path.StartsWith("Resources/") || path.StartsWith("Library/"))
                return MaterialLocation.ReadOnly;
            if (!path.StartsWith("Assets/"))
            {
                reason = "Outside the Assets folder.";
                return MaterialLocation.Unsupported;
            }
            if (AssetDatabase.IsMainAsset(m) && path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                return MaterialLocation.Owned;
            if (AssetImporter.GetAtPath(path) is ModelImporter)
                return MaterialLocation.Embedded;
            reason = "Stored inside another asset; Restyle only converts .mat files and model materials.";
            return MaterialLocation.Unsupported;
        }

        static readonly HashSet<string> s_UrpColorPasses = new HashSet<string> { "", "SRPDefaultUnlit", "UniversalForward", "UniversalForwardOnly", "UniversalGBuffer" };

        /// <summary>
        /// Whether URP draws a shader's colour at all: one of its passes is tagged UniversalForward(Only), UniversalGBuffer,
        /// SRPDefaultUnlit or has no LightMode. A shader with only Built-in passes (ForwardBase, Vertex, Always…) is drawn
        /// with the error shader: pink.
        /// </summary>
        public static bool RendersInUrp(Shader shader)
        {
            if (shader == null || shader.name == "Hidden/InternalErrorShader")
                return false;
            return HasUrpPass(shader, s_UrpColorPasses.Contains);
        }

        /// <summary>
        /// Whether a subshader URP would use (tagged for URP, or for no pipeline) has a pass whose LightMode matches. Read
        /// from the editor's shader data: the runtime API only sees the active subshader, which can be a one-pass fallback
        /// (URP Lit's, in a batchmode editor).
        /// </summary>
        public static bool HasUrpPass(Shader shader, System.Func<string, bool> lightMode)
        {
            if (shader == null)
                return false;
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null)
                return false;
            var pipelineTag = new ShaderTagId("RenderPipeline");
            var lightModeTag = new ShaderTagId("LightMode");
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                var sub = data.GetSubshader(s);
                var pipeline = sub.FindTagValue(pipelineTag).name;
                if (!string.IsNullOrEmpty(pipeline) && !pipeline.StartsWith("Universal"))
                    continue;
                for (int p = 0; p < sub.PassCount; p++)
                    if (lightMode(sub.GetPass(p).FindTagValue(lightModeTag).name ?? ""))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// The low-poly heuristic: an unknown shader with recognisable base-colour / base-texture names. Offered as
        /// "Guessed", unticked, so nothing is converted unless you agree (P6).
        /// </summary>
        public static bool Guess(Shader shader, List<LooksmithRestyleRule> rules, out RestyleSpec spec)
        {
            spec = null;
            if (shader == null || shader.name.StartsWith("Hidden/") || IsWater(shader) || HasOwnWind(shader) || IsEffect(shader))
                return false;
            var props = Enumerable.Range(0, shader.GetPropertyCount()).Select(shader.GetPropertyName).ToList();
            bool hasMap = LooksmithMaterialConverter.BaseMapNames.Any(props.Contains);
            bool hasColor = LooksmithMaterialConverter.BaseColorNames.Any(props.Contains);
            List<PropertyPair> pairs = null;
            if (!hasMap)
            {
                // Shader Graph "master materials" and generated shaders often keep their real names (Texture2D_2A1A8040,
                // Material_Texture2D_1) behind readable ones: read the names people see in the inspector instead
                var named = PairsFromDisplayNames(shader);
                if (named.Any(p => p.target == "_BaseMap"))
                    pairs = named;
                else if (!hasColor && !named.Any(p => p.target == "_BaseColor"))
                    return false;
                else if (!hasColor)
                    pairs = named;
            }
            var lower = shader.name.ToLowerInvariant();
            var recipe = lower.Contains("particle") ? RestyleRecipe.Particles : lower.Contains("unlit") ? RestyleRecipe.Unlit : RestyleRecipe.Lit;
            var template = rules.FirstOrDefault(r => !r.inverse && r.recipe == recipe);
            if (template == null)
                return false;
            bool vertexColour = lower.Contains("vertex") || props.Any(p => p.ToLowerInvariant().Contains("vertexcolor"));
            bool twoSided = System.Text.RegularExpressions.Regex.IsMatch(lower, "two ?sided|double ?sided|2 ?sided");
            spec = RestyleSpec.For(template.target, recipe, pairs != null ? "Guessed (inspector names)" : "Guessed (low-poly heuristic)",
                vertexColour ? 1f : 0f, pairs, twoSided);
            return true;
        }

        static readonly string[] k_OpacityWords = { "opacity", "alpha mask", "alphamask", "transparency", "cutout mask", "opacitymask" };

        /// <summary>
        /// A material whose see-through comes from its own opacity texture (Leartes' transparent master, its trees): an
        /// opacity map assigned and a cutout or transparent queue. Looksmith Lit takes alpha from the base map, so converted,
        /// the surface would vanish. (Walls on the opaque master declare the same texture and don't use it.)
        /// </summary>
        public static bool UsesOpacityMap(Material m)
        {
            if (m == null || m.shader == null || m.renderQueue < 2450)
                return false;
            var shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                    continue;
                var name = shader.GetPropertyName(i);
                var text = (name + " " + shader.GetPropertyDescription(i)).ToLowerInvariant();
                if (k_OpacityWords.Any(text.Contains) && m.GetTexture(name) != null)
                    return true;
            }
            return false;
        }

        static readonly string[] k_WindWords = { "wind", "sway", "gust", "flutter", "breeze" };

        /// <summary>Foliage that moves with its own wind (a property says so): converting it would freeze it.</summary>
        public static bool HasOwnWind(Shader shader)
        {
            if (shader == null)
                return false;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var text = (shader.GetPropertyName(i) + " " + shader.GetPropertyDescription(i)).ToLowerInvariant();
                if (k_WindWords.Any(text.Contains))
                    return true;
            }
            return false;
        }

        static readonly HashSet<string> k_WaterWords = new HashSet<string> { "water", "ocean", "sea", "river", "lake", "waves", "liquid" };

        /// <summary>
        /// A water shader, by its own name (the last part of the path: a pack folder called "ForestLake" isn't water).
        /// Its look is the shader itself, so it's never guessed into Looksmith Lit.
        /// </summary>
        /// <summary>A terrain shader (URP's, the built-in ones, or any with Terrain in its name).</summary>
        public static bool IsTerrain(Shader shader) => shader != null && shader.name.IndexOf("Terrain", System.StringComparison.OrdinalIgnoreCase) >= 0
                                                      && !shader.name.StartsWith("Looksmith/");

        public static bool IsWater(Shader shader)
        {
            if (shader == null)
                return false;
            var own = shader.name.Substring(shader.name.LastIndexOf('/') + 1);
            var words = System.Text.RegularExpressions.Regex.Replace(own, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.Split(words, "[^a-z]+").Any(k_WaterWords.Contains);
        }

        static readonly HashSet<string> k_EffectWords = new HashSet<string>
        {
            "fog", "smoke", "fire", "flame", "flames", "mist", "steam", "cloud", "clouds", "volumetric", "godray", "godrays",
            "hologram", "explosion", "flare",
        };

        /// <summary>
        /// A visual effect drawn in URP (Dark Medieval's S_Fog_Sphere, S_CandleFlame), by its own name. Converted, a fog
        /// volume would become a solid-looking lit surface, so it's never guessed. Particle shaders keep their own recipe,
        /// and an effect URP can't draw (pink today) can still be guessed: anything beats pink.
        /// </summary>
        public static bool IsEffect(Shader shader)
        {
            if (shader == null || shader.name.ToLowerInvariant().Contains("particle"))
                return false;
            var own = shader.name.Substring(shader.name.LastIndexOf('/') + 1);
            var words = System.Text.RegularExpressions.Regex.Replace(own, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.Split(words, "[^a-z]+").Any(k_EffectWords.Contains) && RendersInUrp(shader);
        }

        static readonly string[] k_BaseMapWords = { "base colour", "base color", "basecolor", "basecolour", "albedo", "diffuse", "main tex", "base map", "colour map", "color map" };
        static readonly string[] k_TintWords = { "color", "colour", "tint", "base color", "base colour", "main color", "main colour" };
        static readonly string[] k_SecondLayerWords = { "02", "2nd", "second", "secondary", "detail", "layer 2", "material 2" };

        /// <summary>
        /// Base colour map, tint and normal map found by the names shown in the inspector ("Material 01 Base Colour",
        /// "Color", "Material 01 Normal"). On a layered master the first layer wins.
        /// </summary>
        public static List<PropertyPair> PairsFromDisplayNames(Shader shader)
        {
            var maps = new List<(string name, string label)>();
            var normals = new List<(string name, string label)>();
            string tint = null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if ((shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0)
                    continue;
                var label = shader.GetPropertyDescription(i).Trim().ToLowerInvariant();
                var name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        if (k_BaseMapWords.Any(label.Contains))
                            maps.Add((name, label));
                        else if (label.Contains("normal"))
                            normals.Add((name, label));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        // a colour that starts as transparent black is a mask or an add, never a tint to multiply by
                        var d = shader.GetPropertyDefaultVectorValue(i);
                        bool clear = d.x < 0.004f && d.y < 0.004f && d.z < 0.004f && d.w < 0.004f;
                        if (tint == null && !clear && k_TintWords.Any(w => label == w || label.EndsWith(" " + w)))
                            tint = name;
                        break;
                }
            }
            string First(List<(string name, string label)> found) =>
                found.Where(f => !k_SecondLayerWords.Any(f.label.Contains)).Select(f => f.name).FirstOrDefault() ?? found.Select(f => f.name).FirstOrDefault();
            var pairs = new List<PropertyPair>();
            if (First(maps) is string map)
                pairs.Add(new PropertyPair(map, "_BaseMap"));
            if (tint != null)
                pairs.Add(new PropertyPair(tint, "_BaseColor"));
            if (First(normals) is string normal)
                pairs.Add(new PropertyPair(normal, "_BumpMap"));
            return pairs;
        }

        // ------------------------------------------------------------------ paths

        public static string SafeName(string s) => string.Join("_", s.Split(Path.GetInvalidFileNameChars())).Replace('/', '_');

        public static string UserRulePath(string shaderName) => $"{UserRules}/{SafeName(shaderName)}.asset";

        /// <summary>A free path that no file and no other change in the set is using.</summary>
        public static string UniquePath(string path, Changeset cs)
        {
            var taken = new HashSet<string>(cs.changes.SelectMany(c => c.Writes()));
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var candidate = path;
            for (int n = 1; File.Exists(candidate) || taken.Contains(candidate); n++)
                candidate = $"{dir}/{name} {n}{ext}";
            return candidate;
        }

        /// <summary>Where a package material's restyled copy goes; reuses a copy an earlier Restyle made for the same shader.</summary>
        public static string CopyPathFor(Material source, RestyleSpec spec, Changeset cs, out bool reuse)
        {
            var path = $"{GeneratedMaterials}/{SafeName(source.name)} (Looksmith).mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            reuse = existing != null && existing.shader != null && existing.shader.name == spec.targetShaderName;
            return reuse ? path : UniquePath(path, cs);
        }
    }
}
