using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Material inspector for every Looksmith graph: URP's surface options, then grouped Looksmith inputs. Look-driven colours
    /// (shadow tint, rim colour) inherit the Look by default; a per-colour switch overrides them on this material.
    /// </summary>
    public class LooksmithLitGUI : UnityEditor.BaseShaderGUI
    {
        MaterialProperty[] m_Properties;

        static readonly string[] k_InheritOptions = { "Inherit from the Look", "Override on this material" };

        static readonly (string title, string[] names)[] k_Groups =
        {
            ("Surface", new[] { "_BaseMap", "_BaseColor", "_VertexColorAmount", "_Cutoff" }),
            ("Detail", new[] { "_BumpMap", "_BumpScale", "_OcclusionMap", "_OcclusionStrength" }),
            ("Shading", new[] { "_Smoothness", "_Metallic", "_SpecularAmount", "_RimAmount" }),
            ("Emission", new[] { "_EmissionMap", "_EmissionColor" }),
            ("Gradient", new[] { "_GradientBottom", "_GradientTop", "_GradientRange", "_GradientAmount", "_LookHaze" }),
            ("Particles", new[] { "_Lit", "_SoftDistance" }),
            ("Character", new[] { "_CharacterMask", "_FaceShadowMap", "_FaceSoftness", "_HairShift" }),
            ("Effects", new[] { "_Dissolve", "_DissolveScale", "_DissolveEdgeWidth", "_DissolveEdgeColor", "_HitFlash", "_DitherFade" }),
        };

        static readonly (string name, string label, string tip)[] k_LookColours =
        {
            ("_ShadowTint", "Shadow tint", "The colour shadows take. Inherit keeps every material on the Look's palette."),
            ("_RimTint", "Rim colour", "The rim-light colour."),
        };

        static readonly HashSet<string> k_Handled = new HashSet<string>(
            k_Groups.SelectMany(g => g.names).Concat(k_LookColours.Select(c => c.name)));

        RestyleHistory.Record m_Restyled;
        string m_RestyledFor;

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            DrawRestyledHeader(editor.target as Material);
            base.OnGUI(editor, properties);
        }

        /// <summary>"Restyled by Looksmith · Revert this material" when a Restyle backup covers this material.</summary>
        void DrawRestyledHeader(Material material)
        {
            var path = material != null ? AssetDatabase.GetAssetPath(material) : null;
            if (path != m_RestyledFor)
            {
                m_RestyledFor = path;
                m_Restyled = RestyleHistory.Find(path);
            }
            if (m_Restyled == null)
                return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(new GUIContent($"Restyled by Looksmith · from {m_Restyled.fromShader}", m_Restyled.manifest.label), EditorStyles.miniLabel);
                if (m_Restyled.inPlace)
                {
                    if (GUILayout.Button(new GUIContent("Revert this material", "Restores this material from Looksmith's backup (other changes in that backup stay)."), EditorStyles.miniButton, GUILayout.Width(130)))
                    {
                        if (Revert.Preflight().Count > 0 && EditorUtility.DisplayDialog("Save first?", "Revert only touches saved work. Save now?", "Save and revert", "Cancel"))
                            AssetDatabase.SaveAssets();
                        if (Revert.Preflight().Count == 0)
                        {
                            Debug.Log("Looksmith: " + RestyleHistory.RevertMaterial(m_Restyled, path));
                            m_RestyledFor = null;
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                else
                    EditorGUILayout.LabelField("Extracted from a model: revert in Hub > Changes", EditorStyles.miniLabel);
            }
        }

        public override void FindProperties(MaterialProperty[] properties)
        {
            m_Properties = properties;
            base.FindProperties(properties);
        }

        public override void DrawSurfaceInputs(Material material)
        {
            ManualLink("Looksmith materials", LooksmithDocs.Materials);
            foreach (var (title, names) in k_Groups)
            {
                var props = names.Select(n => Find(n)).Where(p => p != null && (p.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) == 0).ToList();
                if (props.Count == 0)
                    continue;
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                foreach (var p in props)
                    DrawProperty(p);
            }

            var lookProps = k_LookColours.Where(c => Find(c.name) != null).ToList();
            if (lookProps.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Look colours", EditorStyles.boldLabel);
                foreach (var (name, label, _) in lookProps)
                    DrawInheritable(Find(name), new GUIContent(label, LooksmithMaterialTooltips.Get(name)));
            }

            // Anything a user added to a copy of the graph still shows.
            foreach (var p in m_Properties)
            {
                if (k_Handled.Contains(p.name) || (p.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0 || p.name.StartsWith("unity_"))
                    continue;
                if (IsSurfaceOption(p.name))
                    continue;
                DrawProperty(p);
            }
        }

        void DrawProperty(MaterialProperty p)
        {
            var label = new GUIContent(p.displayName, LooksmithMaterialTooltips.Get(p.name));
            if (p.propertyType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                materialEditor.TexturePropertySingleLine(label, p);
            else
                materialEditor.ShaderProperty(p, label);
        }

        /// <summary>A right-aligned "?" that opens the manual page for these materials.</summary>
        public static void ManualLink(string title, string url)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("? Manual", "Open the manual: " + title), EditorStyles.miniButton, GUILayout.Width(70)))
                    LooksmithLinks.Open(url);
            }
        }

        void DrawInheritable(MaterialProperty p, GUIContent label)
        {
            var c = p.colorValue;
            bool overridden = c.a > 0.5f;
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUILayout.Popup(label, overridden ? 1 : 0, k_InheritOptions);
            if (choice == 1)
            {
                EditorGUI.indentLevel++;
                var rgb = EditorGUILayout.ColorField(new GUIContent("Colour"), new Color(c.r, c.g, c.b, 1f), true, false, false);
                c = new Color(rgb.r, rgb.g, rgb.b, 1f);
                EditorGUI.indentLevel--;
            }
            else
                c.a = 0f;
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo(label.text);
                p.colorValue = c;
            }
        }

        MaterialProperty Find(string name) => m_Properties?.FirstOrDefault(p => p.name == name);

        static bool IsSurfaceOption(string name) => name.StartsWith("_Surface") || name.StartsWith("_Blend") || name.StartsWith("_Src") ||
                                                     name.StartsWith("_Dst") || name.StartsWith("_ZWrite") || name.StartsWith("_ZTest") ||
                                                     name == "_Cull" || name == "_AlphaClip" || name == "_AlphaToMask" || name == "_CastShadows" ||
                                                     name == "_ReceiveShadows" || name.StartsWith("_Queue") || name == "_AddPrecomputedVelocity" ||
                                                     name == "_XRMotionVectorsPass";

        public override void DrawAdvancedOptions(Material material)
        {
            materialEditor.RenderQueueField();
            base.DrawAdvancedOptions(material);
            materialEditor.DoubleSidedGIField();
        }

        // URP's Shader Graph material validation (render state + keywords from the material-override options) is internal.
        static readonly MethodInfo s_UpdateUnlit = Type.GetType("UnityEditor.ShaderGraphUnlitGUI, Unity.RenderPipelines.Universal.Editor")
            ?.GetMethod("UpdateMaterial", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        public override void ValidateMaterial(Material material) => Validate(material);

        /// <summary>Applies render state and keywords for the material's surface options (also used by tools).</summary>
        public static void Validate(Material material)
        {
            if (s_UpdateUnlit != null)
            {
                var updateType = s_UpdateUnlit.GetParameters()[1].ParameterType;   // nested internal enum
                s_UpdateUnlit.Invoke(null, new[] { material, Enum.ToObject(updateType, 0) });
            }
            else
                SetMaterialKeywords(material);
        }
    }
}
