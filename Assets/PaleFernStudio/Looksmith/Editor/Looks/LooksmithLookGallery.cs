using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The look gallery (Hub > Looks and Home ③ Style) and the creation menus. Picking a look points the scene's
    /// Look Volume at a shipped profile: an ordinary, undoable scene edit, like changing a field yourself.
    /// </summary>
    [InitializeOnLoad]
    public static class LooksmithLookGallery
    {
        static LooksmithLookGallery()
        {
            LooksmithHubWindow.looksPageFactory = BuildGallery;
            LooksmithHubWindow.stylePageFactory = BuildGallery;
        }

        public static Volume SceneLookVolume(Scene scene) =>
            LooksmithFind.All<LooksmithLookVolume>(FindObjectsInactive.Include)
                .Where(v => v.gameObject.scene == scene && v.TryGetComponent<Volume>(out var vol) && vol.isGlobal)
                .Select(v => v.GetComponent<Volume>()).FirstOrDefault();

        /// <summary>The shipped profile for a look name (null if it's missing).</summary>
        public static VolumeProfile ShippedProfile(string lookName) =>
            AssetDatabase.LoadAssetAtPath<VolumeProfile>(LooksmithLooks.ShippedPath(lookName));

        /// <summary>The shipped look (or look variant) a profile is, or null for the user's own profiles.</summary>
        public static string ShippedLookName(VolumeProfile profile)
        {
            if (profile == null)
                return null;
            var path = AssetDatabase.GetAssetPath(profile);
            return LooksmithLooks.All.Select(d => d.name).Concat(LooksmithLooks.Variants.Select(v => v.name))
                .FirstOrDefault(n => LooksmithLooks.ShippedPath(n) == path);
        }

        static VisualElement BuildGallery()
        {
            var root = new VisualElement { name = "look-gallery" };
            var scene = SceneManager.GetActiveScene();
            var volume = SceneLookVolume(scene);
            if (volume == null)
            {
                root.Add(new Label("This scene has no Looksmith Look Volume yet. Run Home ① Check → ② Apply, or use GameObject > Looksmith > Look Volume.")
                    { style = { whiteSpace = WhiteSpace.Normal } });
                return root;
            }
            var currentShipped = ShippedLookName(volume.sharedProfile);
            var current = volume.sharedProfile != null ? volume.sharedProfile.name : "(none)";
            root.Add(new Label($"Current look: {currentShipped ?? current}") { name = "current-look", style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });
            root.Add(new Label(LooksmithPlayMode.active
                    ? LooksmithPlayMode.Note
                    : "One click applies a look to this scene (Undo puts it back). Under each look, its variants fit it to your game: Night, Realistic, Low Poly, Cozy and a few of its own. Shipped looks are read-only: duplicate one in its inspector to make it yours.")
                { name = "gallery-note", style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, marginBottom = 6 } });

            var grid = new VisualElement { name = "look-grid" };
            grid.AddToClassList("looksmith-gallery");
            if (volume.sharedProfile != null && currentShipped == null)
                grid.Add(Card(volume.sharedProfile, current + " (yours)", "Your own Look Profile, in use in this scene.", true, null));
            var currentVariant = currentShipped != null ? LooksmithLooks.FindVariant(currentShipped) : null;
            bool lite = !LooksmithEdition.isPro;
            foreach (var def in LooksmithLooks.All)
            {
                var d = def;
                if (lite && !d.lite)
                    continue;   // Lite: its own looks (the Pro tab shows the others)
                var profile = ShippedProfile(d.name);
                var entry = new VisualElement { name = "entry-" + d.name };
                entry.AddToClassList("looksmith-look-entry");
                bool isCurrent = d.name == currentShipped || currentVariant?.look == d.name;
                entry.Add(Card(profile, currentVariant?.look == d.name ? $"{d.name} · {currentVariant.kind}" : d.name, d.description, isCurrent, () => ApplyLook(volume, d.name), d));
                var variants = new VisualElement();
                variants.AddToClassList("looksmith-variants");
                foreach (var variant in LooksmithLooks.VariantsOf(d.name))
                {
                    var v = variant;
                    var chip = new Button(() => ApplyLook(volume, v.name)) { text = v.kind, name = "variant-" + v.name, tooltip = v.description };
                    chip.AddToClassList("looksmith-variant");
                    if (v.name == currentShipped)
                        chip.AddToClassList("looksmith-variant--current");
                    chip.SetEnabled(ShippedProfile(v.name) != null);
                    variants.Add(chip);
                }
                entry.Add(variants);
                grid.Add(entry);
            }
            root.Add(grid);
            root.Add(LooksmithCompareControls.Build());
            if (LooksmithProCards.tabShown)
            {
                // Lite: one line pointing to the Pro tab, where the other looks are (it goes when the user hides that tab)
                var more = new VisualElement { name = "pro-pointer-row" };
                more.AddToClassList("looksmith-row");
                more.Add(new Label($"{LooksmithLooks.All.Count(d => !d.lite)} more looks come with Looksmith Pro.") { style = { opacity = 0.7f } });
                more.Add(new Button(() => LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Pro)) { text = "See them", name = "pro-pointer",
                    tooltip = "Opens the Hub's Pro tab: each Pro look before and after." });
                root.Add(more);
            }
            return root;
        }

        static VisualElement Card(VolumeProfile profile, string title, string description, bool current, System.Action apply, LooksmithLooks.Def def = null)
        {
            var card = new Button(apply) { name = "look-" + title, tooltip = description };
            card.AddToClassList("looksmith-look-card");
            if (current)
                card.AddToClassList("looksmith-look-card--current");
            card.SetEnabled(profile != null);

            var image = new Image { scaleMode = ScaleMode.ScaleAndCrop, name = "thumbnail" };
            image.AddToClassList("looksmith-look-thumb");
            var tex = LooksmithLookThumbnails.Get(profile);
            if (tex != null)
                image.image = tex;
            else
                image.Add(PaletteStrip(def, profile));   // until the thumbnail is ready
            card.Add(image);
            var label = new Label(current ? "✓ " + title : title);
            label.AddToClassList("looksmith-look-name");
            card.Add(label);

            if (tex == null && profile != null)
            {
                void OnRendered(VolumeProfile p)
                {
                    if (p != profile)
                        return;
                    image.Clear();
                    image.image = LooksmithLookThumbnails.Get(profile);
                    LooksmithLookThumbnails.Rendered -= OnRendered;
                }
                LooksmithLookThumbnails.Rendered += OnRendered;
                card.RegisterCallback<DetachFromPanelEvent>(_ => LooksmithLookThumbnails.Rendered -= OnRendered);
            }
            return card;
        }

        internal static VisualElement PaletteStrip(LooksmithLooks.Def def, VolumeProfile profile)
        {
            var strip = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            Color[] colors;
            if (def != null)
                colors = new[] { def.sky, def.key, def.accent, def.shadow, def.fog }.Select(h => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.gray).ToArray();
            else if (profile != null && profile.TryGet<LooksmithLook>(out var look))
                colors = new[] { look.skyColor, look.keyColor, look.accentColor, look.shadowColor, look.fogColor }.Select(p => LooksmithColor.OklchToColor(p.value)).ToArray();
            else
                colors = new[] { Color.gray };
            foreach (var c in colors)
                strip.Add(new VisualElement { style = { flexGrow = 1, backgroundColor = c } });
            return strip;
        }

        public static void ApplyLook(Volume volume, string lookName)
        {
            var profile = ShippedProfile(lookName);
            if (profile == null)
            {
                Debug.LogWarning($"Looksmith: shipped look '{lookName}' not found at {LooksmithLooks.ShippedPath(lookName)}.");
                return;
            }
            if (LooksmithPlayMode.active)
            {
                volume.sharedProfile = profile;   // for this Play Mode session, like any change made while playing
            }
            else
            {
                Undo.RecordObject(volume, "Looksmith: apply look " + lookName);
                volume.sharedProfile = profile;
                EditorUtility.SetDirty(volume);
                EditorSceneManager.MarkSceneDirty(volume.gameObject.scene);
            }
            SceneView.RepaintAll();
            LooksmithSceneOverlay.RefreshAll();
            var hub = Resources.FindObjectsOfTypeAll<LooksmithHubWindow>().FirstOrDefault();
            hub?.Rebuild();
        }

        [MenuItem("GameObject/Looksmith/Look Volume", true)]
        static bool CanCreateLookVolume() => !LooksmithPlayMode.active;   // it's a backed-up file write

        [MenuItem("GameObject/Looksmith/Look Volume", false, 10)]
        static void CreateLookVolume()
        {
            var scene = SceneManager.GetActiveScene();
            if (SceneLookVolume(scene) != null)
            {
                EditorUtility.DisplayDialog("Looksmith", "This scene already has a Looksmith Look Volume.", "OK");
                return;
            }
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Looksmith", "Save the scene first: Looksmith backs it up before adding the Look Volume.", "OK");
                return;
            }
            var cs = LooksmithSetup.BuildApply(Enumerable.Empty<ScanFinding>(), scene);
            cs.label = "Add Look Volume";
            if (scene.isDirty || DirtyAssets.Paths().Any())
            {
                if (!EditorUtility.DisplayDialog("Save first?", "Looksmith backs up the scene before adding the Look Volume, so it needs saved work. Save now?", "Save and continue", "Cancel"))
                    return;
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
            }
            ChangesetRunner.Start(cs, backupFirst: true);
        }

        [MenuItem("Assets/Create/Looksmith/Look Profile", false, 120)]
        static void CreateLookProfile()
        {
            var folder = "Assets";
            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected))
                folder = AssetDatabase.IsValidFolder(selected) ? selected : Path.GetDirectoryName(selected).Replace('\\', '/');
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New Look.asset");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            LooksmithLooks.Populate(profile, LooksmithLooks.All[0].name);
            AssetDatabase.SaveAssetIfDirty(profile);
            Selection.activeObject = profile;
        }
    }

    /// <summary>The before and after controls (Hub Style step, Looks tab and dashboard): a switch and the split position.</summary>
    public static class LooksmithCompareControls
    {
        public static VisualElement Build()
        {
            var row = new VisualElement { name = "compare" };
            row.AddToClassList("looksmith-compare");
            var toggle = new Toggle("Before and after") { value = LooksmithCompare.active, name = "compare-toggle",
                tooltip = "Splits the Scene and Game views in two: without Looksmith on the left of the line, with your look on the right." };
            var slider = new Slider(0f, 1f) { value = LooksmithCompare.split, name = "compare-split", tooltip = "Where the line sits across the view." };
            slider.style.flexGrow = 1;
            slider.SetEnabled(LooksmithCompare.active);
            toggle.RegisterValueChangedCallback(e =>
            {
                LooksmithCompare.active = e.newValue;
                SceneView.RepaintAll();
            });
            slider.RegisterValueChangedCallback(e =>
            {
                LooksmithCompare.split = e.newValue;
                SceneView.RepaintAll();
            });
            void Sync()
            {
                if (row.panel == null)
                    return;
                toggle.SetValueWithoutNotify(LooksmithCompare.active);
                slider.SetValueWithoutNotify(LooksmithCompare.split);
                slider.SetEnabled(LooksmithCompare.active);
            }
            row.RegisterCallback<AttachToPanelEvent>(_ => LooksmithCompare.Changed += Sync);
            row.RegisterCallback<DetachFromPanelEvent>(_ => LooksmithCompare.Changed -= Sync);
            row.Add(toggle);
            row.Add(new Label("A plain") { style = { opacity = 0.7f, marginLeft = 8 } });
            row.Add(slider);
            row.Add(new Label("B Looksmith") { style = { opacity = 0.7f } });
            return row;
        }
    }
}
