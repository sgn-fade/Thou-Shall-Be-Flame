using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The Hub's Tweaks tab (plan §4.6): the modules in the open scenes, each with its main settings first and every
    /// setting below, the list of unsaved changes, Save and Revert this session. Modules the scene doesn't have offer
    /// Add. It works in Play Mode too (saved changes are written when Play Mode ends).
    /// </summary>
    public static class LooksmithTweaksPanel
    {
        static readonly Dictionary<string, bool> s_Open = new Dictionary<string, bool>();
        static readonly Dictionary<string, bool> s_Advanced = new Dictionary<string, bool>();
        static readonly Dictionary<string, List<TweakSuggestion>> s_Suggestions = new Dictionary<string, List<TweakSuggestion>>();
        static string s_Status = "";
        static string s_Focus;
        static bool s_ChangesOpen = true;

        [InitializeOnLoadMethod]
        static void Hook() => LooksmithHubWindow.tweaksPageFactory = Build;

        /// <summary>Opens the Hub on the Tweaks tab with one module open (Open in Tweaks).</summary>
        public static void Focus(string module)
        {
            s_Focus = module;
            if (module != null)
            {
                foreach (var m in LooksmithTweaks.modules)
                    s_Open[m.name] = m.name == module;
            }
            LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Tweaks);
        }

        static VisualElement Build()
        {
            var root = new VisualElement { name = "tweaks" };
            var gui = new IMGUIContainer(Draw) { name = "tweaks-gui" };
            root.Add(gui);
            void Repaint() => gui.MarkDirtyRepaint();
            LooksmithTweaks.Changed += Repaint;
            root.RegisterCallback<DetachFromPanelEvent>(_ => LooksmithTweaks.Changed -= Repaint);
            return root;
        }

        static void Draw()
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
            {
                Undo.IncrementCurrentGroup();     // one drag, one undo step
                LooksmithTweaks.EndStep();
            }
            // while playing, Tweaks has its own undo (Unity's would reach older changes to the same assets)
            if (Application.isPlaying && Event.current.type == EventType.KeyDown && (Event.current.control || Event.current.command))
            {
                bool redo = Event.current.keyCode == KeyCode.Y || Event.current.keyCode == KeyCode.Z && Event.current.shift;
                if (redo || Event.current.keyCode == KeyCode.Z)
                {
                    if (redo) LooksmithTweaks.RedoStep(); else LooksmithTweaks.UndoStep();
                    Event.current.Use();
                }
            }
            EditorGUILayout.LabelField(Application.isPlaying
                    ? "Changes show right away. Save keeps them, and they're written to your project when you stop playing. Anything you don't save goes back."
                    : "Changes show right away but aren't kept until you press Save. Ctrl+Z undoes. Looksmith's own looks and presets stay as they are: saving a change to one makes your own copy.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);
            bool first = true;
            foreach (var module in LooksmithTweaks.modules)
                if (DrawModule(module, first))
                    first = false;
            EditorGUILayout.Space(8);
            DrawChanges();
        }

        // ------------------------------------------------------------------ modules

        /// <summary>Draws a module; true when the scene has it.</summary>
        static bool DrawModule(TweakModule module, bool firstPresent)
        {
            bool present = module.present != null && module.present();
            if (!present)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(new GUIContent(module.name, module.summary), EditorStyles.boldLabel, GUILayout.Width(110));
                    EditorGUILayout.LabelField("Not in this scene.", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(Application.isPlaying || string.IsNullOrEmpty(module.addMenu)))
                        if (GUILayout.Button(new GUIContent("Add", "Adds " + module.name + " to the scene."), GUILayout.Width(60)))
                            EditorApplication.ExecuteMenuItem(module.addMenu);
                }
                return false;
            }
            // the first module starts open, unless Open in Tweaks asked for another
            if (!s_Open.TryGetValue(module.name, out bool open))
                open = s_Open[module.name] = s_Focus != null ? s_Focus == module.name : firstPresent;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int unsaved = LooksmithTweaks.CountSettings(LooksmithTweaks.changes.Where(c => c.module == module.name && !c.saved));
                    open = EditorGUILayout.Foldout(open, module.name + (unsaved > 0 ? $"  ({unsaved} unsaved)" : ""), true, EditorStyles.foldoutHeader);
                    if (GUILayout.Button(new GUIContent("Revert to default", $"Puts {module.name} back to its defaults. You can still undo it, or use Revert this session."),
                            EditorStyles.miniButton, GUILayout.Width(110)))
                        RevertToDefault(module);
                }
                s_Open[module.name] = open;
                if (!open)
                    return true;
                if (!string.IsNullOrEmpty(module.summary))
                    EditorGUILayout.LabelField(module.summary, EditorStyles.wordWrappedMiniLabel);
                foreach (var knob in module.essentials?.Invoke() ?? new List<TweakKnob>())
                    DrawKnob(module, knob);
                if (module.suggestions != null)
                    DrawSuggestions(module);
                s_Advanced.TryGetValue(module.name, out bool adv);
                adv = EditorGUILayout.Foldout(adv, "Every setting", true);
                s_Advanced[module.name] = adv;
                if (adv)
                {
                    using (new EditorGUI.IndentLevelScope())
                        foreach (var o in module.objects?.Invoke() ?? new List<Object>())
                            DrawAdvanced(module, o);
                }
            }
            return true;
        }

        static void RevertToDefault(TweakModule module)
        {
            int n = LooksmithTweaks.RevertToDefault(module, false);
            if (n == 0)
            {
                s_Status = $"{module.name} is already at its defaults.";
                return;
            }
            if (!EditorUtility.DisplayDialog($"Revert {module.name} to default?",
                    $"This puts {LooksmithTweaks.Plural(n, "setting")} back to {module.name}'s defaults. Ctrl+Z undoes it.",
                    "Revert to default", "Cancel"))
                return;
            LooksmithTweaks.RevertToDefault(module);
            // for something Looksmith ships, the default is how it's saved: going back to it leaves nothing to save
            int left = LooksmithTweaks.CountSettings(LooksmithTweaks.changes.Where(c => c.module == module.name && !c.saved));
            s_Status = left > 0 ? $"{module.name} is back to its defaults. Press Save to keep it." : $"{module.name} is back to its defaults.";
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// A slider dragged close to the default, or to where the setting started, lands exactly on it (so it reads as
        /// the default, or as unchanged). Only while dragging: a typed value stays as typed.
        /// </summary>
        internal static float Snap(TweakKnob k, float value, object defaultValue, object start)
        {
            float reach = Mathf.Abs(k.max - k.min) * 0.01f;
            foreach (var magnet in new[] { defaultValue, start })
                if (magnet is float m && Mathf.Abs(value - m) <= reach)
                    return m;
            return value;
        }

        static bool Dragging => Event.current != null && (Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseDown);

        static void DrawKnob(TweakModule module, TweakKnob k)
        {
            if (k.target == null)
                return;
            var value = LooksmithTweaks.Get(k.target, k.property);
            if (value == null)
                return;
            var draft = LooksmithTweaks.DraftOf(k.target, k.property);
            bool unsaved = draft != null && !draft.saved;
            var defaultValue = LooksmithTweaks.DefaultOf(k.target, k.property);
            bool atDefault = defaultValue == null || TweakValue.Equal(value, defaultValue);
            var label = new GUIContent(k.label, k.hint);
            object next = value;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                using (new BoldLabel(unsaved))
                {
                    if (k.choices != null)
                    {
                        int i = k.choices.FindIndex(c => TweakValue.Equal(c.Value, value));
                        int j = EditorGUILayout.Popup(label, i, k.choices.Select(c => new GUIContent(c.Key)).ToArray());
                        if (j >= 0)
                            next = k.choices[j].Value;
                    }
                    else
                    {
                        switch (value)
                        {
                            case bool b: next = EditorGUILayout.Toggle(label, b); break;
                            case Color c: next = EditorGUILayout.ColorField(label, c, true, true, true); break;
                            case int i when k.options != null: next = EditorGUILayout.Popup(label, i, k.options); break;
                            case int i: next = EditorGUILayout.IntSlider(label, i, (int)k.min, (int)k.max); break;
                            case float f when k.whole && k.options != null:
                                next = (float)EditorGUILayout.Popup(label, Mathf.RoundToInt(f), k.options);
                                break;
                            case float f:
                                float slid = EditorGUILayout.Slider(label, f, Mathf.Min(k.min, f), Mathf.Max(k.max, f));
                                next = Dragging ? Snap(k, slid, defaultValue, draft != null ? draft.before : f) : slid;
                                break;
                            default:
                                EditorGUILayout.LabelField(label, new GUIContent(TweakValue.Describe(value)));
                                break;
                        }
                    }
                }
                bool edited = EditorGUI.EndChangeCheck();
                if (!atDefault)
                {
                    if (GUILayout.Button(new GUIContent("↺", "Back to the default: " + TweakValue.Describe(defaultValue)), EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        LooksmithTweaks.Set(k.target, k.property, defaultValue, module.name, k.label);
                        GUIUtility.ExitGUI();
                    }
                }
                else
                    GUILayout.Space(25);
                if (edited)
                    LooksmithTweaks.Set(k.target, k.property, next, module.name, k.label);
            }
            if (!string.IsNullOrEmpty(k.note))
                EditorGUILayout.LabelField(k.note, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Draws the label of a setting with an unsaved change in bold.</summary>
        readonly struct BoldLabel : System.IDisposable
        {
            readonly FontStyle m_Was;
            readonly bool m_On;

            public BoldLabel(bool on)
            {
                m_On = on;
                m_Was = EditorStyles.label.fontStyle;
                if (on)
                    EditorStyles.label.fontStyle = FontStyle.Bold;
            }

            public void Dispose()
            {
                if (m_On)
                    EditorStyles.label.fontStyle = m_Was;
            }
        }

        // ------------------------------------------------------------------ suggestions

        static void DrawSuggestions(TweakModule module)
        {
            s_Suggestions.TryGetValue(module.name, out var list);
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Suggestions for this view", EditorStyles.miniBoldLabel);
                if (GUILayout.Button(new GUIContent(list == null ? "Suggest" : "Check again",
                        "Looks at what the camera sees and suggests changes to the look. Nothing changes until you press Apply."),
                        EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    s_Suggestions[module.name] = Suggest(module);
                    GUIUtility.ExitGUI();
                }
            }
            if (list == null)
                return;
            if (list.Count == 0)
                EditorGUILayout.LabelField("Nothing to suggest. The look suits what the camera sees.", EditorStyles.wordWrappedMiniLabel);
            foreach (var s in list)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s.title, EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        s.apply?.Invoke();
                        list.Remove(s);
                        s_Status = $"Applied \"{s.title}\". Save to keep it.";
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField(s.why, EditorStyles.wordWrappedMiniLabel);
            }
        }

        internal static List<TweakSuggestion> Suggest(TweakModule module)
        {
            try
            {
                return module.suggestions?.Invoke() ?? new List<TweakSuggestion>();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return new List<TweakSuggestion>();
            }
        }

        // ------------------------------------------------------------------ every setting

        static void DrawAdvanced(TweakModule module, Object o)
        {
            if (o == null)
                return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(Title(o), EditorStyles.boldLabel);
                if (GUILayout.Button(new GUIContent("Select", "Shows it in the Project window or the Hierarchy."), EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    var real = LooksmithTweaksHooks.SourceOf(o);
                    EditorGUIUtility.PingObject(real is VolumeComponent && AssetDatabase.Contains(real) ? AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(real)) : real);
                }
            }
            if (o is Material m)
                DrawMaterial(module, m);
            else if (o is VolumeComponent vc)
                DrawVolumeComponent(module, vc);
            else
                DrawSerialized(module, o);
            EditorGUILayout.Space(2);
        }

        static string Title(Object o)
        {
            if (o is Component c)
                return ObjectNames.NicifyVariableName(c.GetType().Name.Replace("Looksmith", "")) + " on " + c.gameObject.name;
            if (o is VolumeComponent)
                return ObjectNames.NicifyVariableName(o.GetType().Name.Replace("Looksmith", ""));
            return o.name;
        }

        static void DrawSerialized(TweakModule module, Object o)
        {
            var so = new SerializedObject(o);
            so.Update();
            var p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = false;
                if (p.propertyPath == "m_Script")
                    continue;
                var leaves = Leaves(p);
                var before = leaves.ToDictionary(l => l, l => TweakValue.Read(so.FindProperty(l)));
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(p, true);
                if (!EditorGUI.EndChangeCheck())
                    continue;
                so.ApplyModifiedProperties();
                foreach (var leaf in leaves)
                {
                    var now = TweakValue.Read(o, leaf);
                    if (!TweakValue.Equal(now, before[leaf]))
                        LooksmithTweaks.Record(o, leaf, before[leaf], module.name, ObjectNames.NicifyVariableName(leaf.Split('.').Last()));
                }
                so.Update();
            }
        }

        /// <summary>The leaf settings under a property (itself when it's a leaf).</summary>
        static List<string> Leaves(SerializedProperty p)
        {
            var list = new List<string>();
            if (p.propertyType != SerializedPropertyType.Generic)
            {
                list.Add(p.propertyPath);
                return list;
            }
            if (p.isArray)
                return list;
            var it = p.Copy();
            var end = it.GetEndProperty();
            while (it.Next(true) && !SerializedProperty.EqualContents(it, end))
                if (it.propertyType != SerializedPropertyType.Generic && !it.isArray)
                    list.Add(it.propertyPath);
            return list;
        }

        static void DrawVolumeComponent(TweakModule module, VolumeComponent vc)
        {
            foreach (var field in vc.GetType().GetFields())
            {
                if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType))
                    continue;
                var parameter = field.GetValue(vc) as VolumeParameter;
                var label = new GUIContent(ObjectNames.NicifyVariableName(field.Name),
                    (field.GetCustomAttributes(typeof(TooltipAttribute), true).FirstOrDefault() as TooltipAttribute)?.tooltip);
                string valuePath = field.Name + ".m_Value", overridePath = field.Name + ".m_OverrideState";
                var value = TweakValue.Read(vc, valuePath);
                if (value == null)
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = TweakValue.Read(vc, overridePath) is bool b && b;
                    EditorGUI.BeginChangeCheck();
                    bool nextOn = EditorGUILayout.Toggle(new GUIContent("", "On: this look sets it. Off: it's left to other volumes or the default."), on, GUILayout.Width(28));
                    if (EditorGUI.EndChangeCheck())
                        LooksmithTweaks.Set(vc, overridePath, nextOn, module.name, label.text + " (on)");
                    using (new EditorGUI.DisabledScope(!on))
                    {
                        EditorGUI.BeginChangeCheck();
                        object next = value;
                        switch (value)
                        {
                            case float f when parameter is ClampedFloatParameter cf: next = EditorGUILayout.Slider(label, f, cf.min, cf.max); break;
                            case float f when parameter is MinFloatParameter mf: next = Mathf.Max(mf.min, EditorGUILayout.FloatField(label, f)); break;
                            case float f: next = EditorGUILayout.FloatField(label, f); break;
                            case int i when parameter is ClampedIntParameter ci: next = EditorGUILayout.IntSlider(label, i, ci.min, ci.max); break;
                            case int i: next = EditorGUILayout.IntField(label, i); break;
                            case bool b2: next = EditorGUILayout.Toggle(label, b2); break;
                            case Color c: next = EditorGUILayout.ColorField(label, c, true, true, true); break;
                            case Vector4 v: next = EditorGUILayout.Vector4Field(label, v); break;
                            default: EditorGUILayout.LabelField(label, new GUIContent(TweakValue.Describe(value))); break;
                        }
                        if (EditorGUI.EndChangeCheck())
                            LooksmithTweaks.Set(vc, valuePath, next, module.name, label.text);
                    }
                }
            }
        }

        static void DrawMaterial(TweakModule module, Material m)
        {
            var shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0)
                    continue;
                var type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture)
                    continue;
                var name = shader.GetPropertyName(i);
                var label = new GUIContent(shader.GetPropertyDescription(i), name);
                var value = TweakValue.Read(m, name);
                EditorGUI.BeginChangeCheck();
                object next = value;
                switch (type)
                {
                    case ShaderPropertyType.Range:
                        var limits = shader.GetPropertyRangeLimits(i);
                        next = EditorGUILayout.Slider(label, (float)value, limits.x, limits.y);
                        break;
                    case ShaderPropertyType.Float: next = EditorGUILayout.FloatField(label, (float)value); break;
                    case ShaderPropertyType.Int: next = EditorGUILayout.IntField(label, (int)value); break;
                    case ShaderPropertyType.Color:
                        bool hdr = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HDR) != 0;
                        next = EditorGUILayout.ColorField(label, (Color)value, true, true, hdr);
                        break;
                    case ShaderPropertyType.Vector: next = EditorGUILayout.Vector4Field(label, (Vector4)value); break;
                }
                if (EditorGUI.EndChangeCheck())
                    LooksmithTweaks.Set(m, name, next, module.name, label.text);
            }
        }

        // ------------------------------------------------------------------ changes, Save, Revert

        static void DrawChanges()
        {
            var unsaved = LooksmithTweaks.changes.Where(c => !c.saved).ToList();
            var rows = unsaved.GroupBy(c => c.targetId + "|" + LooksmithTweaks.BaseOf(c.property)).ToList();
            int saved = LooksmithTweaks.CountSettings(LooksmithTweaks.changes.Where(c => c.saved));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_ChangesOpen = EditorGUILayout.Foldout(s_ChangesOpen,
                    rows.Count == 0 ? "No unsaved changes" : LooksmithTweaks.Plural(rows.Count, "unsaved change"), true, EditorStyles.foldoutHeader);
                if (s_ChangesOpen)
                    foreach (var row in rows)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var c = row.FirstOrDefault(x => !x.property.EndsWith(".m_OverrideState")) ?? row.First();
                            EditorGUILayout.LabelField($"{c.module}: {c.label.Replace(" (on)", "")}", GUILayout.MinWidth(140));
                            string what = c.property.EndsWith(".m_OverrideState")
                                ? (c.after is bool on && on ? "switched on in this look" : "switched off in this look")
                                : $"{TweakValue.Describe(c.before)} → {TweakValue.Describe(c.after)}";
                            EditorGUILayout.LabelField(what, EditorStyles.miniLabel, GUILayout.MinWidth(120));
                            if (GUILayout.Button(new GUIContent("↺", "Undo this change: back to " + TweakValue.Describe(c.before) + "."), EditorStyles.miniButton, GUILayout.Width(22)))
                            {
                                foreach (var x in row.ToList())
                                    LooksmithTweaks.Revert(x);
                                GUIUtility.ExitGUI();
                            }
                        }
                if (saved > 0 && Application.isPlaying)
                    EditorGUILayout.LabelField($"{LooksmithTweaks.Plural(saved, "saved change")} will be written when you stop playing.", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(unsaved.Count == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Save",
                                "Keeps your changes. Looksmith backs up the files first, and the save shows in Hub > Changes, where you can undo it.")))
                        {
                            s_Status = SafeSave();
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button(new GUIContent("Revert this session", "Puts every unsaved change back.")))
                        {
                            LooksmithTweaks.RevertSession();
                            s_Status = "Everything is back to how it was when you started.";
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                if (Application.isPlaying)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!LooksmithTweaks.canUndo))
                            if (GUILayout.Button(new GUIContent("Undo", "Undoes your last change while playing (Ctrl+Z in the game's Tweaks panel).")))
                            {
                                LooksmithTweaks.UndoStep();
                                GUIUtility.ExitGUI();
                            }
                        using (new EditorGUI.DisabledScope(!LooksmithTweaks.canRedo))
                            if (GUILayout.Button(new GUIContent("Redo", "Redoes what you just undid (Ctrl+Y in the game's Tweaks panel).")))
                            {
                                LooksmithTweaks.RedoStep();
                                GUIUtility.ExitGUI();
                            }
                    }
                if (!string.IsNullOrEmpty(s_Status))
                    EditorGUILayout.LabelField(s_Status, EditorStyles.wordWrappedMiniLabel);
            }
        }

        static string SafeSave()
        {
            try
            {
                return LooksmithTweaks.Save();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return "Couldn't save: " + e.Message;
            }
        }
    }
}
