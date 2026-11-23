using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Hub > Restyle: pick a scope, preview (a dry run with counts, before/after thumbnails and a toggle per group),
    /// then Apply as one backed-up Changeset. Also "Convert to URP Lit" (the inverse) and "Map manually…".
    /// </summary>
    [InitializeOnLoad]
    public static class LooksmithRestylePage
    {
        static RestyleScope s_Scope = RestyleScope.Scene;
        static DefaultAsset s_Folder;
        static RestylePlan s_Plan;
        static bool s_BackupFirst = true;
        static readonly Dictionary<string, RestyleSpec> s_Manual = new Dictionary<string, RestyleSpec>();
        static string s_Mapping;   // source shader whose manual mapping editor is open
        static List<Object> s_Selection;

        static LooksmithRestylePage()
        {
            // Statics are reset explicitly: Looksmith supports Enter Play Mode without a domain reload.
            s_Plan = null;
            s_Manual.Clear();
            s_Mapping = null;
            LooksmithHubWindow.restylePageFactory = Build;
        }

        [MenuItem("Assets/Looksmith/Restyle…", false, 2000)]
        static void RestyleSelection()
        {
            s_Scope = RestyleScope.Selection;
            s_Selection = Selection.objects.ToList();
            s_Plan = LooksmithRestyle.Plan(RestyleScope.Selection, selection: s_Selection, manual: s_Manual);
            LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Restyle);
        }

        [MenuItem("Assets/Looksmith/Restyle…", true)]
        static bool CanRestyleSelection() =>
            Selection.objects.Any(o => o is Material || o is GameObject || (o is DefaultAsset && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o))));

        static void Refresh() => EditorWindow.GetWindow<LooksmithHubWindow>(false, null, false)?.Rebuild();

        static void Preview(bool inverse)
        {
            var folder = s_Folder != null ? AssetDatabase.GetAssetPath(s_Folder) : null;
            s_Plan = LooksmithRestyle.Plan(s_Scope, folder, inverse, s_Manual, s_Scope == RestyleScope.Selection ? s_Selection ?? Selection.objects.ToList() : null);
            Refresh();
        }

        static VisualElement Build()
        {
            var root = new VisualElement();
            root.Add(H1("Restyle"));
            root.Add(Muted("Converts materials to Looksmith shaders. Your materials change in place; materials inside models are extracted first " +
                           "(Unity's standard step); package materials get restyled copies. Shaders Looksmith doesn't know are listed and left alone. " +
                           "Everything is backed up first and reverts exactly."));

            var scopeRow = Row();
            var scope = new EnumField("Scope", s_Scope) { name = "restyle-scope" };
            scope.RegisterValueChangedCallback(e => { s_Scope = (RestyleScope)e.newValue; s_Plan = null; Refresh(); });
            scopeRow.Add(scope);
            if (s_Scope == RestyleScope.Folder)
            {
                var folder = new ObjectField { objectType = typeof(DefaultAsset), value = s_Folder, allowSceneObjects = false };
                folder.RegisterValueChangedCallback(e => { s_Folder = e.newValue as DefaultAsset; s_Plan = null; });
                scopeRow.Add(folder);
            }
            if (s_Scope == RestyleScope.Selection)
                scopeRow.Add(new Button(() => { s_Selection = Selection.objects.ToList(); Preview(false); }) { text = "Use current selection" });
            root.Add(scopeRow);

            var actions = Row();
            var preview = new Button(() => Preview(false)) { text = "Preview restyle", name = "restyle-preview" };
            preview.AddToClassList("looksmith-primary");
            actions.Add(preview);
            actions.Add(new Button(() => Preview(true)) { text = "Preview: convert Looksmith materials to URP", tooltip = "The inverse mapping, for materials you want back on URP Lit (no backup of the originals needed)." });
            actions.Add(new Button(() => LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Changes)) { text = "Restore originals…", tooltip = "Revert a Restyle from its backup in Hub > Changes." });
            root.Add(actions);

            if (s_Plan != null)
                root.Add(BuildPlan(s_Plan));
            return root;
        }

        static VisualElement BuildPlan(RestylePlan plan)
        {
            var page = new VisualElement { name = "restyle-plan" };
            page.Add(H2(plan.inverse ? $"Convert to URP: {plan.scopeLabel}" : $"Preview: {plan.scopeLabel}"));
            if (plan.items.Count == 0)
            {
                page.Add(Text(plan.inverse ? "No Looksmith materials in this scope." : "No materials in this scope."));
                return page;
            }

            // Coverage before Apply: how much of the camera's view gets Looksmith's lighting with the groups as ticked.
            System.Action refreshCoverage = null;
            var camera = plan.inverse ? null : LooksmithLookFit.ReferenceCamera();
            if (camera != null)
            {
                var box = new VisualElement { name = "restyle-coverage" };
                box.AddToClassList("looksmith-card");
                refreshCoverage = () =>
                {
                    box.Clear();
                    var c = LooksmithCoverage.Measure(camera, plan);
                    if (c == null || c.sky >= 0.999f)
                    {
                        box.Add(Muted($"{camera.name} sees nothing to measure."));
                        return;
                    }
                    box.Add(Text(c.Summary() + " (as ticked below)."));
                    box.Add(Muted(c.Meaning()));
                };
                refreshCoverage();
                page.Add(box);
            }

            // Dry-run table: source shader → target, with counts.
            foreach (var row in plan.items.Where(i => i.spec != null).GroupBy(i => (i.sourceShader, i.spec.targetShaderName)).OrderByDescending(g => g.Count()))
                page.Add(Muted($"{row.Count(),4}  {row.Key.sourceShader}  →  {row.Key.targetShaderName}"));

            foreach (var group in LooksmithRestyle.AllGroups)
            {
                var items = plan.InGroup(group).ToList();
                if (items.Count == 0)
                    continue;
                bool converting = LooksmithRestyle.ConvertingGroups.Contains(group);
                var box = new VisualElement();
                box.AddToClassList("looksmith-card");
                if (converting)
                {
                    var t = new Toggle($"{group} ({items.Count})") { value = plan.Enabled(group), name = "group-" + group };
                    t.RegisterValueChangedCallback(e =>
                    {
                        plan.groupEnabled[group] = e.newValue;
                        refreshCoverage?.Invoke();
                    });
                    box.Add(t);
                    box.Add(Thumbnails(items.Take(4)));
                }
                else
                    box.Add(Text($"{group} ({items.Count})"));

                if (group == LooksmithRestyle.GroupSkipped)
                    foreach (var byShader in items.GroupBy(i => i.sourceShader))
                        box.Add(SkippedRow(plan, byShader.Key, byShader.ToList()));
                else
                {
                    foreach (var i in items.Take(12))
                        box.Add(Muted($"• {i.material.name}  ·  {i.sourceShader}  ·  {Where(i)}" + (i.reason != null ? "  ·  " + i.reason : "")));
                    if (items.Count > 12)
                        box.Add(Muted($"…and {items.Count - 12} more"));
                }
                page.Add(box);
            }

            if (plan.ConvertibleCount == 0)
            {
                page.Add(Text("Nothing to convert."));
                return page;
            }
            var backup = new Toggle("Back up first") { value = s_BackupFirst, tooltip = "Copies every file about to change to LooksmithBackups/ (outside Assets) so Revert is exact." };
            backup.RegisterValueChangedCallback(e => s_BackupFirst = e.newValue);
            page.Add(backup);
            var git = LooksmithHubWindow.GitNotice();
            if (git != null)
                page.Add(git);
            var apply = new Button(() =>
            {
                var cs = plan.ToChangeset();
                if (cs.changes.Count == 0)
                    return;
                if (LooksmithHubWindow.ApplyWithPrompts(cs, s_BackupFirst))
                    s_Plan = null;
                Refresh();
            }) { text = ChangesetRunner.isRunning ? "Applying…" : plan.inverse ? "Convert" : "Restyle", name = "restyle-apply" };
            apply.AddToClassList("looksmith-primary");
            apply.SetEnabled(!ChangesetRunner.isRunning && !LooksmithPlayMode.active);
            if (LooksmithPlayMode.active)
                apply.tooltip = LooksmithPlayMode.WritesDisabled;
            page.Add(apply);
            return page;
        }

        static string Where(RestyleItem i) => i.location switch
        {
            MaterialLocation.Embedded => "in " + System.IO.Path.GetFileName(i.assetPath),
            MaterialLocation.ReadOnly => (i.assetPath.StartsWith("Packages/") ? "package" : "built-in") + $", {i.uses.Count} use(s) in the scene",
            _ => i.assetPath,
        };

        // ------------------------------------------------------------------ Map manually…

        static readonly (string target, string label, ShaderPropertyType type)[] k_ManualSlots =
        {
            ("_BaseMap", "Base map", ShaderPropertyType.Texture),
            ("_BaseColor", "Base colour", ShaderPropertyType.Color),
            ("_BumpMap", "Normal map", ShaderPropertyType.Texture),
            ("_EmissionColor", "Emission colour", ShaderPropertyType.Color),
            ("_Smoothness", "Smoothness", ShaderPropertyType.Range),
            ("_Metallic", "Metallic", ShaderPropertyType.Range),
        };

        static VisualElement SkippedRow(RestylePlan plan, string shaderName, List<RestyleItem> items)
        {
            var col = new VisualElement();
            var row = Row();
            row.Add(Text($"{shaderName}: {items.Count} material(s)"));
            var shader = items[0].material.shader;
            bool mappable = items.Any(i => i.location != MaterialLocation.Unsupported) && shader != null && !plan.inverse;
            if (mappable)
                row.Add(new Button(() => { s_Mapping = s_Mapping == shaderName ? null : shaderName; Refresh(); }) { text = "Map manually…" });
            row.Add(Muted("Leave as is (default)"));
            col.Add(row);
            col.Add(Muted(items[0].reason ?? ""));
            if (mappable && s_Mapping == shaderName)
                col.Add(MappingEditor(shader));
            return col;
        }

        static VisualElement MappingEditor(Shader source)
        {
            var box = new VisualElement();
            box.AddToClassList("looksmith-card");
            var rules = LooksmithRestyleRule.All().Where(r => !r.inverse && r.recipe != RestyleRecipe.Terrain).GroupBy(r => r.recipe).Select(g => g.First()).ToList();
            if (rules.Count == 0)
            {
                box.Add(Muted("No Looksmith target shaders found."));
                return box;
            }
            var targets = rules.Select(r => r.target.name).ToList();
            var targetField = new PopupField<string>("Target shader", targets, 0);
            box.Add(targetField);
            var props = Enumerable.Range(0, source.GetPropertyCount())
                .Select(i => (name: source.GetPropertyName(i), type: source.GetPropertyType(i))).ToList();
            var choices = new Dictionary<string, PopupField<string>>();
            foreach (var (target, label, type) in k_ManualSlots)
            {
                var compatible = new List<string> { "(none)" };
                compatible.AddRange(props.Where(p => Compatible(p.type, type)).Select(p => p.name));
                int guess = compatible.FindIndex(n => n == target || (target == "_BaseMap" && LooksmithMaterialConverter.BaseMapNames.Contains(n)) ||
                                                      (target == "_BaseColor" && LooksmithMaterialConverter.BaseColorNames.Contains(n)));
                var field = new PopupField<string>(label, compatible, Mathf.Max(0, guess));
                choices[target] = field;
                box.Add(field);
            }
            box.Add(new Button(() =>
            {
                var rule = rules[targets.IndexOf(targetField.value)];
                var pairs = choices.Where(kv => kv.Value.value != "(none)").Select(kv => new PropertyPair(kv.Value.value, kv.Key));
                s_Manual[source.name] = RestyleSpec.For(rule.target, rule.recipe, "Manual: " + source.name, 0f, pairs);
                s_Mapping = null;
                Preview(false);
            }) { text = "Use this mapping (saved as a rule when you restyle)" });
            return box;
        }

        static bool Compatible(ShaderPropertyType from, ShaderPropertyType to)
        {
            bool IsFloat(ShaderPropertyType t) => t == ShaderPropertyType.Float || t == ShaderPropertyType.Range || t == ShaderPropertyType.Int;
            return from == to || (IsFloat(from) && IsFloat(to)) || (from == ShaderPropertyType.Vector && to == ShaderPropertyType.Color) ||
                   (from == ShaderPropertyType.Color && to == ShaderPropertyType.Vector);
        }

        // ------------------------------------------------------------------ thumbnails

        static VisualElement Thumbnails(IEnumerable<RestyleItem> items)
        {
            var row = Row();
            foreach (var i in items)
            {
                if (i.spec == null || i.spec.recipe == RestyleRecipe.Terrain || i.spec.recipe == RestyleRecipe.ToUrpTerrain)
                    continue;
                var pair = new VisualElement { style = { flexDirection = FlexDirection.Row, marginRight = 10 }, tooltip = i.material.name };
                pair.Add(new Image { image = MaterialThumbnail.Get(i.material, null), style = { width = 56, height = 56 } });
                pair.Add(new Label("→") { style = { unityTextAlign = TextAnchor.MiddleCenter } });
                pair.Add(new Image { image = MaterialThumbnail.Get(i.material, i.spec), style = { width = 56, height = 56 } });
                row.Add(pair);
            }
            return row;
        }

        // ------------------------------------------------------------------ helpers (same classes as the Hub)

        static Label H1(string t) { var l = new Label(t); l.AddToClassList("looksmith-h1"); return l; }
        static Label H2(string t) { var l = new Label(t); l.AddToClassList("looksmith-h2"); return l; }
        static Label Text(string t) { var l = new Label(t); l.AddToClassList("looksmith-text"); return l; }
        static Label Muted(string t) { var l = new Label(t); l.AddToClassList("looksmith-muted"); return l; }
        static VisualElement Row() { var r = new VisualElement(); r.AddToClassList("looksmith-row"); return r; }
    }

    /// <summary>Small sphere previews of a material as it is, or as it would be after a conversion (in memory only).</summary>
    public static class MaterialThumbnail
    {
        static readonly Dictionary<(string, string), Texture2D> s_Cache = new Dictionary<(string, string), Texture2D>();

        [InitializeOnLoadMethod]
        static void ResetStatics() => s_Cache.Clear();

        public static Texture2D Get(Material material, RestyleSpec after, int size = 112)
        {
            var key = (LooksmithObjectIds.Get(material), after == null ? "" : after.targetShaderName + after.recipe + after.vertexColorAmount + after.pairs.Count);
            if (s_Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            Material temp = null;
            try
            {
                var m = material;
                if (after != null)
                {
                    temp = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                    if (!LooksmithMaterialConverter.Convert(temp, after))
                        return null;
                    m = temp;
                }
                return s_Cache[key] = Render(m, size);
            }
            finally
            {
                if (temp != null)
                    Object.DestroyImmediate(temp);
            }
        }

        static Mesh s_Sphere;

        static Mesh Sphere()
        {
            // The built-in sphere (CreatePrimitive would add, then remove, an object in the open scene and dirty it).
            if (s_Sphere == null)
                s_Sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx") ?? Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            return s_Sphere;
        }

        /// <summary>
        /// The Look previews show: the open scene's own Look, or the first look (Storybook; Cel Anime in Lite) for a scene
        /// that isn't styled yet (Restyle is usually the first step). Preview cameras get no Looksmith setup pass, so its
        /// globals are pushed here.
        /// </summary>
        static LooksmithLook PreviewLook()
        {
            var volume = LooksmithLookGallery.SceneLookVolume(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out LooksmithLook look))
                return look;
            var shipped = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(LooksmithLooks.ShippedPath(LooksmithLooks.FirstLook));
            return shipped != null && shipped.TryGet(out LooksmithLook first) ? first : null;
        }

        public static Texture2D Render(Material m, int size)
        {
            var pru = new PreviewRenderUtility();
            bool asyncWas = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;   // a variant compiling in the background would draw nothing
            float enabledWas = Shader.GetGlobalFloat(LooksmithShaderIDs.Enabled);
            var state = LooksmithLookState.CreateDetached();
            try
            {
                if (m.shader != null && m.shader.name.StartsWith("Looksmith/"))
                {
                    state.Evaluate(PreviewLook());
                    var cmd = new UnityEngine.Rendering.CommandBuffer { name = "Looksmith preview look" };
                    state.Push(cmd);
                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Release();
                }
                pru.camera.fieldOfView = 30f;
                pru.camera.nearClipPlane = 0.5f;
                pru.camera.farClipPlane = 10f;
                pru.camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4.2f), Quaternion.identity);
                pru.camera.clearFlags = CameraClearFlags.SolidColor;
                pru.camera.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
                pru.lights[0].intensity = 1.1f;
                pru.lights[0].transform.rotation = Quaternion.Euler(40f, -40f, 0f);
                pru.lights[1].intensity = 0.4f;
                pru.ambientColor = new Color(0.25f, 0.25f, 0.28f);
                pru.BeginStaticPreview(new Rect(0, 0, size, size));
                pru.DrawMesh(Sphere(), Matrix4x4.identity, m, 0);
                pru.camera.Render();
                return pru.EndStaticPreview();
            }
            finally
            {
                pru.Cleanup();
                state.Release();
                ShaderUtil.allowAsyncCompilation = asyncWas;
                Shader.SetGlobalFloat(LooksmithShaderIDs.Enabled, enabledWas);   // cameras push their own Look again
            }
        }
    }
}
