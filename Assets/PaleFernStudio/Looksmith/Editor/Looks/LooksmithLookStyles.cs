using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The Art Styles of a Look Profile, drawn in the Look inspector: each style with an on/off switch, its intensity and
    /// its key settings, plus "Add Art Style". Undoable edits of the profile asset.
    /// </summary>
    public static class LooksmithLookStyles
    {
        /// <summary>The settings shown for each style in the Look inspector (the rest stay in the style's own panel).</summary>
        static readonly Dictionary<string, string[]> k_KeySettings = new Dictionary<string, string[]>
        {
            { "LooksmithPosterize", new[] { "lightnessLevels", "gradientMap" } },
            { "LooksmithPainterly", new[] { "strokeSize", "brush", "canvas" } },
            { "LooksmithWatercolour", new[] { "edgeDarkening", "granulation", "bleeding" } },
            { "LooksmithInkHatching", new[] { "shadowStart", "lineWidth", "keepColor" } },
            { "LooksmithComic", new[] { "halftone", "ink" } },
            { "LooksmithPixelArt", new[] { "pixelSize", "outline", "dither" } },
            { "LooksmithRetro", new[] { "pixelate", "internalHeight", "posterizeLight", "display" } },
        };

        static readonly Dictionary<Object, SerializedObject> s_Serialized = new Dictionary<Object, SerializedObject>();

        [InitializeOnLoadMethod]
        static void Reset() => s_Serialized.Clear();   // Domain Reload may be off

        /// <summary>The Look Profile an override belongs to (null for profiles that aren't assets).</summary>
        public static VolumeProfile ProfileOf(Object component)
        {
            var path = AssetDatabase.GetAssetPath(component);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as VolumeProfile;
        }

        public static List<LooksmithStyle> StylesIn(VolumeProfile profile) =>
            profile == null ? new List<LooksmithStyle>() : profile.components.OfType<LooksmithStyle>().OrderBy(s => s.order.value).ToList();

        public static string NameOf(Type type) => ObjectNames.NicifyVariableName(type.Name.Replace("Looksmith", ""));

        /// <summary>
        /// The Art Styles "Add Art Style" offers: every one this edition draws (Lite has Posterize; when a Pro install shows
        /// the Lite UI, Pro's are left out as a real Lite has none). From the type cache, which is ready before the
        /// VolumeManager is (an inspector can draw before any render).
        /// </summary>
        public static Type[] AvailableTypes() => TypeCache.GetTypesDerivedFrom<LooksmithStyle>()
            .Where(t => !t.IsAbstract && (LooksmithEdition.isPro || t.Assembly.GetName().Name != LooksmithEdition.ProAssembly))
            .OrderBy(t => t.Name).ToArray();

        /// <summary>Adds a style to the profile (on, at full intensity) as a sub-asset of the profile.</summary>
        public static LooksmithStyle Add(VolumeProfile profile, Type type)
        {
            if (profile.Has(type))
                return (LooksmithStyle)profile.components.First(c => c.GetType() == type);
            Undo.RegisterCompleteObjectUndo(profile, $"Looksmith: add {NameOf(type)}");
            var style = (LooksmithStyle)profile.Add(type, true);
            style.name = type.Name;
            style.intensity.Override(1f);
            if (AssetDatabase.Contains(profile))
                AssetDatabase.AddObjectToAsset(style, profile);
            Undo.RegisterCreatedObjectUndo(style, $"Looksmith: add {NameOf(type)}");
            EditorUtility.SetDirty(profile);
            return style;
        }

        /// <summary>Switches a style on or off (switching on a style at intensity 0 brings it to full).</summary>
        public static void SetOn(LooksmithStyle style, bool on)
        {
            Undo.RecordObject(style, $"Looksmith: {(on ? "enable" : "disable")} {NameOf(style.GetType())}");
            style.active = on;
            if (on && style.intensity.value <= 0.001f)
                style.intensity.Override(1f);
            EditorUtility.SetDirty(style);
        }

        /// <summary>The Art Styles section of the Look inspector.</summary>
        public static void Draw(Object lookComponent)
        {
            var profile = ProfileOf(lookComponent);
            if (profile == null)
                return;
            foreach (var style in StylesIn(profile))
            {
                if (!s_Serialized.TryGetValue(style, out var so) || so.targetObject == null)
                    s_Serialized[style] = so = new SerializedObject(style);
                so.Update();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool on = EditorGUILayout.ToggleLeft(new GUIContent(NameOf(style.GetType()), "Switch this Art Style on or off in this look."), style.active, EditorStyles.boldLabel);
                        if (on != style.active)
                        {
                            SetOn(style, on);
                            so.Update();
                        }
                    }
                    using (new EditorGUI.DisabledScope(!style.active))
                    {
                        Parameter(so, "intensity");
                        if (k_KeySettings.TryGetValue(style.GetType().Name, out var keys))
                            foreach (var key in keys)
                                Parameter(so, key);
                    }
                }
                so.ApplyModifiedProperties();
            }
            var missing = AvailableTypes().Where(t => !profile.Has(t)).ToList();
            if (missing.Count > 0 && GUILayout.Button(new GUIContent("Add Art Style…", "Add another Art Style to this look."), GUILayout.Width(140)))
            {
                var menu = new GenericMenu();
                foreach (var t in missing)
                {
                    var type = t;
                    menu.AddItem(new GUIContent(NameOf(type)), false, () => Add(profile, type));
                }
                menu.ShowAsContext();
            }
        }

        /// <summary>One Volume parameter (its value; editing it also ticks its override).</summary>
        static void Parameter(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null)
                return;
            var value = p.FindPropertyRelative("m_Value");
            var overrideState = p.FindPropertyRelative("m_OverrideState");
            var field = so.targetObject.GetType().GetField(name);
            var tooltip = field?.GetCustomAttributes(typeof(TooltipAttribute), true).OfType<TooltipAttribute>().FirstOrDefault()?.tooltip ?? "";
            var label = new GUIContent(ObjectNames.NicifyVariableName(name), tooltip);
            var parameter = field?.GetValue(so.targetObject);
            EditorGUI.BeginChangeCheck();
            if (parameter is ClampedFloatParameter cf)
                value.floatValue = EditorGUILayout.Slider(label, value.floatValue, cf.min, cf.max);
            else if (parameter is ClampedIntParameter ci)
                value.intValue = EditorGUILayout.IntSlider(label, value.intValue, ci.min, ci.max);
            else
                EditorGUILayout.PropertyField(value, label);
            if (EditorGUI.EndChangeCheck() && overrideState != null)
                overrideState.boolValue = true;
        }
    }
}
