using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Look inspector: five groups (Light, Colour, Lines, Atmosphere, Finish), macro sliders up front and the
    /// advanced values folded underneath. Palette colours are edited in OKLCH next to a normal colour picker.
    /// </summary>
    [CustomEditor(typeof(LooksmithLook))]
    sealed class LooksmithLookEditor : VolumeComponentEditor
    {
        readonly Dictionary<string, SerializedDataParameter> m_P = new Dictionary<string, SerializedDataParameter>();
        static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        static readonly string[] k_LightMacros = { "softness", "contrast", "warmth", "rim", "shine" };
        static readonly string[] k_LightAdvanced =
        {
            "bands", "bandMix", "edgeSoftness", "terminator", "wrap", "bandSpread", "gradientRamp", "gradientShadow", "gradientMid",
            "gradientLit", "painted", "brushScale", "facets", "pbrBlend", "keyLevel", "keyTint", "shadowHardness", "terminatorAccent",
            "rimStrength", "rimWidth", "rimSoftness", "rimLightFacing", "rimFromAccent", "specularStyle", "specularStrength", "specularSize",
            "specularSoftness", "ambient", "ambientFlatten", "ambientFromPalette", "reflections", "additionalLights",
        };
        static readonly string[] k_Palette = { "keyColor", "shadowColor", "skyColor", "accentColor", "fogColor" };
        static readonly string[] k_LinesAdvanced =
        {
            "lineColor", "lineFromSurface", "silhouettesOnly", "lineDepthSensitivity", "lineNormalSensitivity", "lineLightWeight",
            "lineJitter", "lineWobble", "lineFadeStart", "lineFadeEnd", "lineColorSensitivity", "lineObjectEdges", "lineLayerMask", "outlineWidth",
        };
        static readonly string[] k_AtmosAdvanced = { "hazeStart", "hazeDensity", "hazeHeightFalloff", "hazeMax", "hazeDesaturation", "hazeHeightBase", "hazeFromSky", "softParticles" };
        static readonly string[] k_FinishAdvanced =
        {
            "grainSize", "vignette", "vignetteFromPalette", "paper", "paperTexture", "paperScale", "paperOnSurfaces", "chromaticFringe", "gradingLut",
            "gradingLutContribution", "styleSky", "skyDistance", "vertexSnap", "affineWarp",
        };
        static bool s_PaletteOpen;
        static Texture s_PaletteImage;
        static LooksmithPaletteFromImage.Result s_PaletteResult;

        public override void OnEnable()
        {
            base.OnEnable();
            foreach (var field in typeof(LooksmithLook).GetFields())
                if (typeof(UnityEngine.Rendering.VolumeParameter).IsAssignableFrom(field.FieldType))
                    m_P[field.Name] = Unpack(serializedObject.FindProperty(field.Name));
        }

        public override void OnInspectorGUI()
        {
            if (!LooksmithUserSettings.instance.showAllFields)
            {
                LooksmithTweaksInspector.Header("Look", "How everything is drawn: the light, the colours, the outlines and the art style.", null);
                return;
            }
            if (GUILayout.Button(new GUIContent("Open in Tweaks", "All of the look's settings in one place, with Save and Revert.")))
                LooksmithTweaksPanel.Focus("Look");
            var path = AssetDatabase.GetAssetPath(target);
            bool shipped = !string.IsNullOrEmpty(path) && path.StartsWith(LooksmithLooks.ShippedFolder + "/");
            if (shipped)
            {
                EditorGUILayout.HelpBox("This is a shipped look, kept read-only so updates can't overwrite your work. Duplicate it to edit.", MessageType.Info);
                if (GUILayout.Button("Duplicate to edit"))
                    DuplicateShipped(path);
            }
            FitToScene();
            using (new EditorGUI.DisabledScope(shipped))
                DrawGroups();
        }

        // ------------------------------------------------------------------ Fit to this scene

        static bool s_FitOpen = true;
        List<LooksmithAdvice> m_Advice;
        string m_AdviceFor;

        /// <summary>
        /// The Look Advisor for the open scene: what this look needs to read as intended here (its light, its fog, the
        /// size it's shown at), each with why and an Apply. Looks are starting points; this is how they adapt.
        /// </summary>
        void FitToScene()
        {
            var profile = ProfileOf(target);
            if (profile == null)
                return;
            s_FitOpen = EditorGUILayout.Foldout(s_FitOpen, new GUIContent("Fit to this scene",
                "The Look Advisor reads the open scene (its light, fog, sky and a small render from the main camera) and suggests tweaks so this look reads as intended here."), true);
            if (!s_FitOpen)
                return;
            var camera = LooksmithLookFit.ReferenceCamera();
            if (camera == null)
            {
                EditorGUILayout.HelpBox("Add a camera to the scene and the Advisor will judge the look from it.", MessageType.None);
                return;
            }
            var key = LooksmithObjectIds.Get(profile) + "/" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(m_Advice == null ? "Not checked yet." : m_Advice.Count == 0 ? "This look fits the scene as it is." : $"{m_Advice.Count} suggestion(s) for {camera.name}'s view.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(new GUIContent(m_Advice == null ? "Check" : "Check again", "Renders a small frame from the camera and reads the scene."), GUILayout.Width(90)))
                {
                    m_Advice = LooksmithLookAdvisor.Advise(profile, LooksmithLookFit.Read(camera));
                    m_AdviceFor = key;
                }
            }
            if (m_AdviceFor != key)
                m_Advice = null;
            if (m_Advice == null)
                return;
            foreach (var a in m_Advice.ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var style = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
                    EditorGUILayout.LabelField((a.level == LooksmithAdviceLevel.Recommended ? "Recommended · " : "Tip · ") + a.title, style);
                    EditorGUILayout.LabelField(a.why, EditorStyles.wordWrappedMiniLabel);
                    foreach (var c in a.changes)
                        EditorGUILayout.LabelField("• " + c.label, EditorStyles.miniLabel);
                    if (a.turnOffUnityFog)
                        EditorGUILayout.LabelField("• Unity fog (Lighting > Environment): off", EditorStyles.miniLabel);
                    if (a.turnOnPostProcessing)
                        EditorGUILayout.LabelField("• The camera's Post Processing (Camera > Rendering): on", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(LooksmithPlayMode.active))
                            if (GUILayout.Button(new GUIContent("Apply", "Previews the change, backs it up, then applies it. A shipped look is copied first (it stays read-only)."), GUILayout.Width(70)))
                            {
                                if (LooksmithLookFit.Apply(profile, new[] { a }))
                                    m_Advice = null;
                                GUIUtility.ExitGUI();
                            }
                    }
                }
            }
            if (m_Advice.Count > 1)
            {
                var recommended = m_Advice.Where(a => a.level == LooksmithAdviceLevel.Recommended).ToList();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(recommended.Count == 0 || LooksmithPlayMode.active))
                        if (GUILayout.Button($"Apply the {recommended.Count} recommended", GUILayout.Width(190)))
                        {
                            if (LooksmithLookFit.Apply(profile, recommended))
                                m_Advice = null;
                            GUIUtility.ExitGUI();
                        }
                }
            }
            EditorGUILayout.Space();
        }

        static UnityEngine.Rendering.VolumeProfile ProfileOf(Object component)
        {
            var path = AssetDatabase.GetAssetPath(component);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
        }

        void DuplicateShipped(string path)
        {
            var folder = LooksmithSetup.GeneratedFolder + "/Looks";
            if (!AssetDatabase.IsValidFolder(LooksmithSetup.GeneratedFolder))
                AssetDatabase.CreateFolder("Assets", "Looksmith Generated");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(LooksmithSetup.GeneratedFolder, "Looks");
            var copy = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{System.IO.Path.GetFileNameWithoutExtension(path)}.asset");
            if (!AssetDatabase.CopyAsset(path, copy))
                return;
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(copy);
            foreach (var volume in LooksmithFind.All<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include))
            {
                if (volume.sharedProfile == null || AssetDatabase.GetAssetPath(volume.sharedProfile) != path || volume.GetComponent<LooksmithLookVolume>() == null)
                    continue;
                Undo.RecordObject(volume, "Looksmith: use duplicated look");
                volume.sharedProfile = profile;
            }
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        void DrawGroups()
        {
            Field("style", "Style cameras");
            EditorGUILayout.Space();

            Group("Light", k_LightMacros, k_LightAdvanced, doc: LooksmithDocs.Look + "#light");

            Header("Colour", LooksmithDocs.Palette);
            Field("saturation");
            foreach (var p in k_Palette)
                Oklch(m_P[p], ObjectNames.NicifyVariableName(p).Replace(" Color", ""));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Derive shadow from key", "Hue 20° toward blue-violet, chroma kept, lightness down 42%."), GUILayout.Width(180)))
                {
                    var key = m_P["keyColor"].value.vector3Value;
                    m_P["shadowColor"].overrideState.boolValue = true;
                    m_P["shadowColor"].value.vector3Value = LooksmithColor.DeriveShadow(key);
                }
            }
            PaletteFromImage();

            Group("Lines", new[] { "lineWeight" }, k_LinesAdvanced,
                LooksmithEdition.isPro
                    ? "Depth and crease lines are drawn by every edition; colour edges, object edges, layer masks and hull outlines are Pro."
                    : "Depth and crease lines, with their colour, fade and wobble.",
                LooksmithDocs.Lines);
            Group("Atmosphere", new[] { "haze" }, k_AtmosAdvanced, doc: LooksmithDocs.Look + "#atmosphere");
            Group("Finish", new[] { "grain" }, k_FinishAdvanced,
                "Grain, vignette and paper are drawn by Art Styles > Finish; vertex snapping applies to every Looksmith shader now.",
                LooksmithDocs.Look + "#finish");
            Header("Art Styles", LooksmithDocs.ArtStyles);
            LooksmithLookStyles.Draw(target);   // the ones this edition has (Lite: Posterize)
        }

        static void Header(string title, string doc)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                if (GUILayout.Button(new GUIContent("?", "Open the manual: " + title), EditorStyles.miniButton, GUILayout.Width(20)))
                    LooksmithLinks.Open(doc);
            }
        }

        void Group(string title, string[] macros, string[] advanced, string note = null, string doc = null)
        {
            Header(title, doc ?? LooksmithDocs.Look);
            if (!string.IsNullOrEmpty(note))
                EditorGUILayout.HelpBox(note, MessageType.None);
            foreach (var m in macros)
                Field(m);
            s_Foldouts.TryGetValue(title, out bool open);
            open = EditorGUILayout.Foldout(open, "Advanced", true);
            s_Foldouts[title] = open;
            if (!open)
                return;
            EditorGUI.indentLevel++;
            foreach (var a in advanced)
                if (LooksmithEdition.Shows(typeof(LooksmithLook), a))   // Lite leaves out what only Pro draws
                    Field(a);
            EditorGUI.indentLevel--;
        }

        /// <summary>A Look field's tooltip (every field has one; a test checks).</summary>
        public static string Tooltip(string field)
        {
            var tooltip = typeof(LooksmithLook).GetField(field)?.GetCustomAttributes(typeof(TooltipAttribute), false);
            return tooltip != null && tooltip.Length > 0 ? ((TooltipAttribute)tooltip[0]).tooltip : string.Empty;
        }

        void PaletteFromImage()
        {
            s_PaletteOpen = EditorGUILayout.Foldout(s_PaletteOpen, new GUIContent("Palette from Image",
                "Find the five palette roles in a picture: concept art, a screenshot or a photo (k-means in OKLab)."), true);
            if (!s_PaletteOpen)
                return;
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            s_PaletteImage = (Texture)EditorGUILayout.ObjectField(new GUIContent("Image", "Any texture in the project; it is only read."), s_PaletteImage, typeof(Texture), false);
            if (EditorGUI.EndChangeCheck())
                s_PaletteResult = s_PaletteImage != null ? LooksmithPaletteFromImage.Extract(s_PaletteImage) : null;
            EditorGUI.indentLevel--;
            if (s_PaletteResult == null)
                return;
            var strip = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true)));
            float w = strip.width / 5f;
            for (int i = 0; i < 5; i++)
            {
                var cell = new Rect(strip.x + i * w, strip.y, w - 2f, 26f);
                EditorGUI.DrawRect(cell, LooksmithColor.OklchToColor(s_PaletteResult.roles[i]));
                GUI.Label(new Rect(cell.x, cell.yMax, cell.width, 14f), LooksmithPaletteFromImage.Roles[i], EditorStyles.centeredGreyMiniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Use these five colours", "Sets Key, Shadow, Sky, Accent and Fog (Undo puts them back)."), GUILayout.Width(180)))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var p = m_P[k_Palette[i]];
                        p.overrideState.boolValue = true;
                        p.value.vector3Value = s_PaletteResult.roles[i];
                    }
                }
            }
        }

        void Field(string name, string label = null)
        {
            if (!m_P.TryGetValue(name, out var p))
                return;
            var content = new GUIContent(label ?? ObjectNames.NicifyVariableName(name), Tooltip(name));
            if (name == "lineLayerMask")
                RenderingLayersField(p, content);
            else
                PropertyField(p, content);
        }

        static string[] s_LayerNames;

        void RenderingLayersField(SerializedDataParameter p, GUIContent label)
        {
            if (s_LayerNames == null)
            {
                s_LayerNames = new string[32];
                for (int i = 0; i < 32; i++)
                {
                    var n = RenderingLayerMask.RenderingLayerToName(i);
                    s_LayerNames[i] = string.IsNullOrEmpty(n) ? $"Layer {i}" : n;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(p);
                using (new EditorGUI.DisabledScope(!p.overrideState.boolValue))
                    p.value.intValue = EditorGUILayout.MaskField(label, p.value.intValue, s_LayerNames);
            }
        }

        void Oklch(SerializedDataParameter p, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(p);
                using (new EditorGUI.DisabledScope(!p.overrideState.boolValue))
                {
                    var lch = p.value.vector3Value;
                    EditorGUI.BeginChangeCheck();
                    var picked = EditorGUILayout.ColorField(new GUIContent(label, "Pick a colour, or fine-tune lightness, chroma and hue below."),
                        LooksmithColor.OklchToColor(lch), true, false, false);
                    if (EditorGUI.EndChangeCheck())
                        p.value.vector3Value = LooksmithColor.ColorToOklch(picked);
                }
            }
            using (new EditorGUI.DisabledScope(!p.overrideState.boolValue))
            {
                EditorGUI.indentLevel += 2;
                var v = p.value.vector3Value;
                EditorGUI.BeginChangeCheck();
                v.x = EditorGUILayout.Slider(new GUIContent("Lightness", "OKLCH L: perceived lightness."), v.x, 0f, 1f);
                v.y = EditorGUILayout.Slider(new GUIContent("Chroma", "OKLCH C: colourfulness."), v.y, 0f, 0.37f);
                v.z = EditorGUILayout.Slider(new GUIContent("Hue", "OKLCH h in degrees."), v.z, 0f, 360f);
                if (EditorGUI.EndChangeCheck())
                    p.value.vector3Value = v;
                EditorGUI.indentLevel -= 2;
            }
        }
    }
}
