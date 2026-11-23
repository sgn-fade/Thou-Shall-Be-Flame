using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Tweaks in the Game view, in Editor Play Mode only (never in a build). F9 opens it, or a game's own key through
    /// LooksmithTweaksHooks.ToggleOverlay. It shows each module's main settings, the unsaved count, Save and Revert, with the
    /// same drafts as the Hub's Tweaks tab: saved changes are written when Play Mode ends and the rest go back. Ctrl+Z and
    /// Ctrl+Y undo and redo.
    /// </summary>
    [InitializeOnLoad]
    static class LooksmithTweaksOverlay
    {
        const KeyCode Key = KeyCode.F9;
        const float Width = 400f;
        const string FieldPrefix = "tweaks:";

        static GameObject s_Host;
        static bool s_Open;
        static Vector2 s_Scroll;
        static string s_Status = "";
        static string s_OpenModule;
        static string s_EditKey, s_EditText;
        static readonly Dictionary<string, List<TweakSuggestion>> s_Suggestions = new Dictionary<string, List<TweakSuggestion>>();
        static List<(TweakModule module, List<TweakKnob> knobs)> s_Layout;
        static GUIStyle s_Box, s_Title, s_Small, s_Heading, s_Label, s_Changed;

        static LooksmithTweaksOverlay()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying)
                Start();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                Start();
            else if (change == PlayModeStateChange.ExitingPlayMode)
                Stop();
        }

        static void Start()
        {
            if (s_Host != null)
                return;
            s_Host = new GameObject("Looksmith Tweaks Overlay") { hideFlags = HideFlags.HideAndDontSave };
            s_Host.AddComponent<LooksmithTweaksOverlayHost>();
            Object.DontDestroyOnLoad(s_Host);
            LooksmithTweaksOverlayHost.onGUI = Draw;
            LooksmithTweaksHooks.toggleOverlay = Toggle;
        }

        static void Stop()
        {
            LooksmithTweaksOverlayHost.onGUI = null;
            LooksmithTweaksHooks.toggleOverlay = null;
            LooksmithTweaksHooks.overlayOpen = false;
            LooksmithTweaksHooks.typing = false;
            s_Open = false;
            s_Suggestions.Clear();
            if (s_Host != null)
                Object.DestroyImmediate(s_Host);
            s_Host = null;
        }

        static void Toggle()
        {
            s_Open = !s_Open;
            LooksmithTweaksHooks.overlayOpen = s_Open;
            LooksmithTweaksHooks.typing = false;
            if (s_Open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        static void Styles()
        {
            if (s_Box != null)
                return;
            var bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.9f));
            bg.Apply();
            s_Box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
            s_Box.normal.background = bg;
            s_Title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            s_Title.normal.textColor = new Color(0.95f, 0.94f, 0.9f);
            s_Heading = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fixedHeight = 24 };
            s_Small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            s_Small.normal.textColor = new Color(0.75f, 0.75f, 0.72f);
            s_Label = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip };
            s_Changed = new GUIStyle(s_Label) { fontStyle = FontStyle.Bold };
            s_Changed.normal.textColor = new Color(0.96f, 0.72f, 0.42f);
        }

        static bool TypingNow() => GUI.GetNameOfFocusedControl().StartsWith(FieldPrefix);

        static void Draw()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == Key)
            {
                Toggle();
                e.Use();
            }
            if (!s_Open)
                return;
            // keep the mouse free while the panel is open (a game may grab it back)
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Styles();
            HandleKeys(e);

            // the knobs are worked out once per layout pass, and the same list is then drawn
            if (e.type == EventType.Layout || s_Layout == null)
            {
                s_Layout = LooksmithTweaks.modules.Where(m => m.present != null && m.present())
                    .Select(m => (m, m.essentials?.Invoke() ?? new List<TweakKnob>())).ToList();
                if (s_OpenModule == null && s_Layout.Count > 0)
                    s_OpenModule = s_Layout[0].module.name;
            }

            var area = new Rect(Screen.width - Width - 12f, 12f, Width, Screen.height - 24f);
            if (area.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp))
                LooksmithTweaks.EndStep();     // one drag or click, one undo step
            GUILayout.BeginArea(area, s_Box);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Tweaks", s_Title);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("×", GUILayout.Width(26)))
                {
                    Toggle();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.Label("Changes show right away. Save keeps them, and they're written to your project when you stop playing. " +
                            "Ctrl+Z undoes. Every setting is in Window > Looksmith > Tweaks.", s_Small);
            GUILayout.Space(4);
            s_Scroll = GUILayout.BeginScrollView(s_Scroll);
            foreach (var (module, knobs) in s_Layout)
            {
                bool open = s_OpenModule == module.name;
                int unsaved = LooksmithTweaks.CountSettings(LooksmithTweaks.changes.Where(c => c.module == module.name && !c.saved));
                if (GUILayout.Button((open ? "▾  " : "▸  ") + module.name + (unsaved > 0 ? $"   ({unsaved} unsaved)" : ""), s_Heading))
                {
                    s_OpenModule = open ? "" : module.name;
                    GUIUtility.ExitGUI();
                }
                if (!open)
                    continue;
                GUILayout.Space(2);
                foreach (var k in knobs)
                    DrawKnob(module, k);
                if (module.suggestions != null)
                    DrawSuggestions(module);
                GUILayout.Space(8);
            }
            GUILayout.EndScrollView();

            int count = LooksmithTweaks.unsavedCount, saved = LooksmithTweaks.CountSettings(LooksmithTweaks.changes.Where(c => c.saved));
            GUILayout.Label(count == 0 ? "No unsaved changes." : LooksmithTweaks.Plural(count, "unsaved change") + "." +
                            (saved > 0 ? $" {LooksmithTweaks.Plural(saved, "saved change")} will be written when you stop." : ""), s_Small);
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = count > 0;
                if (GUILayout.Button("Save", GUILayout.Height(26)))
                {
                    s_Status = LooksmithTweaks.Save();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Revert this session", GUILayout.Height(26)))
                {
                    LooksmithTweaks.RevertSession();
                    s_Status = "Everything is back to how it was when you started.";
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = true;
            }
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = LooksmithTweaks.canUndo;
                if (GUILayout.Button(new GUIContent("Undo", "Ctrl+Z"), GUILayout.Height(22)))
                {
                    UndoOrRedo(false);
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = LooksmithTweaks.canRedo;
                if (GUILayout.Button(new GUIContent("Redo", "Ctrl+Y"), GUILayout.Height(22)))
                {
                    UndoOrRedo(true);
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = true;
            }
            if (!string.IsNullOrEmpty(s_Status))
                GUILayout.Label(s_Status, s_Small);
            GUILayout.EndArea();

            LooksmithTweaksHooks.typing = TypingNow();
            if (area.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.ScrollWheel))
                e.Use();
        }

        /// <summary>Enter or Esc finishes typing; Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) undo and redo.</summary>
        static void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown)
                return;
            if (TypingNow())
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    GUI.FocusControl(null);
                    e.Use();
                }
                return;
            }
            if (!(e.control || e.command))
                return;
            bool redo = e.keyCode == KeyCode.Y || e.keyCode == KeyCode.Z && e.shift;
            if (!redo && e.keyCode != KeyCode.Z)
                return;
            UndoOrRedo(redo);
            e.Use();
        }

        static void UndoOrRedo(bool redo)
        {
            bool done = redo ? LooksmithTweaks.RedoStep() : LooksmithTweaks.UndoStep();
            s_Status = done ? "" : redo ? "Nothing to redo." : "Nothing to undo.";
        }

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
            var labelStyle = unsaved ? s_Changed : s_Label;
            object next = value;
            string key = LooksmithObjectIds.Get(k.target) + "/" + k.property;

            if (k.choices != null)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(label, labelStyle);
                    GUILayout.FlexibleSpace();
                    if (ResetButton(module, k, defaultValue, atDefault))
                        return;
                }
                // a value that isn't one of the choices shows none picked (never jumps to the first)
                int i = k.choices.FindIndex(c => TweakValue.Equal(c.Value, value));
                int j = GUILayout.SelectionGrid(i, k.choices.Select(c => c.Key).ToArray(), 3);
                if (j != i && j >= 0)
                    next = k.choices[j].Value;
            }
            else if (k.options != null && (value is int || value is float))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(label, labelStyle);
                    GUILayout.FlexibleSpace();
                    if (ResetButton(module, k, defaultValue, atDefault))
                        return;
                }
                int i = value is int n ? n : Mathf.RoundToInt((float)value);
                int j = GUILayout.SelectionGrid(i, k.options, k.options.Length);
                if (j != i)
                    next = value is int ? (object)j : (float)j;
            }
            else
            {
                switch (value)
                {
                    case bool b:
                        using (new GUILayout.HorizontalScope())
                        {
                            next = GUILayout.Toggle(b, new GUIContent(" " + k.label, k.hint));
                            GUILayout.FlexibleSpace();
                            if (ResetButton(module, k, defaultValue, atDefault))
                                return;
                        }
                        break;
                    case float f:
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label(label, labelStyle, GUILayout.Width(140));
                            float slid = GUILayout.HorizontalSlider(f, Mathf.Min(k.min, f), Mathf.Max(k.max, f), GUILayout.MinWidth(80));
                            float typed = NumberField(key, f);
                            if (slid != f)
                                next = LooksmithTweaksPanel.Snap(k, slid, defaultValue, draft != null ? draft.before : f);
                            else if (typed != f)
                                next = typed;
                            if (ResetButton(module, k, defaultValue, atDefault))
                                return;
                        }
                        break;
                    case int i:
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label(label, labelStyle, GUILayout.Width(140));
                            int slid = Mathf.RoundToInt(GUILayout.HorizontalSlider(i, k.min, k.max, GUILayout.MinWidth(80)));
                            int typed = Mathf.RoundToInt(NumberField(key, i));
                            if (slid != i)
                                next = slid;
                            else if (typed != i)
                                next = typed;
                            if (ResetButton(module, k, defaultValue, atDefault))
                                return;
                        }
                        break;
                    case Color c:
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label(label, labelStyle);
                            GUILayout.FlexibleSpace();
                            if (ResetButton(module, k, defaultValue, atDefault))
                                return;
                        }
                        float r = Channel(key + "/r", "Red", c.r), g = Channel(key + "/g", "Green", c.g), bl = Channel(key + "/b", "Blue", c.b);
                        next = new Color(r, g, bl, c.a);
                        break;
                    default:
                        GUILayout.Label(k.label + ": " + TweakValue.Describe(value), labelStyle);
                        break;
                }
            }
            if (!TweakValue.Equal(next, value))
                LooksmithTweaks.Set(k.target, k.property, next, module.name, k.label);
            if (!string.IsNullOrEmpty(k.note))
                GUILayout.Label(k.note, s_Small);
        }

        /// <summary>Puts one setting back to its default (an empty space when it's at the default, so the layout holds).</summary>
        static bool ResetButton(TweakModule module, TweakKnob k, object defaultValue, bool atDefault)
        {
            if (atDefault)
            {
                GUILayout.Space(26);
                return false;
            }
            if (!GUILayout.Button(new GUIContent("↺", "Back to the default: " + TweakValue.Describe(defaultValue)), GUILayout.Width(22)))
                return false;
            LooksmithTweaks.Set(k.target, k.property, defaultValue, module.name, k.label);
            GUIUtility.ExitGUI();
            return true;
        }

        /// <summary>A box to type an exact number into. It shows the value until it's clicked, then what's typed.</summary>
        static float NumberField(string key, float value)
        {
            var name = FieldPrefix + key;
            bool focused = GUI.GetNameOfFocusedControl() == name;
            if (focused && s_EditKey != key)
            {
                s_EditKey = key;
                s_EditText = TweakValue.Describe(value);
            }
            GUI.SetNextControlName(name);
            string typed = GUILayout.TextField(focused ? s_EditText : TweakValue.Describe(value), GUILayout.Width(54));
            if (!focused)
                return value;
            s_EditText = typed;
            return float.TryParse(typed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : value;
        }

        static float Channel(string key, string name, float v)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("    " + name, GUILayout.Width(140));
                float slid = GUILayout.HorizontalSlider(v, 0f, 1f, GUILayout.MinWidth(80));
                float typed = NumberField(key, v);
                v = slid != v ? slid : typed != v ? Mathf.Clamp01(typed) : v;
                GUILayout.Space(26);
            }
            return v;
        }

        static void DrawSuggestions(TweakModule module)
        {
            s_Suggestions.TryGetValue(module.name, out var list);
            GUILayout.Space(4);
            if (GUILayout.Button(new GUIContent(list == null ? "Suggest changes for this view" : "Check this view again",
                    "Looks at what the camera sees and suggests changes to the look. Nothing changes until you press Apply.")))
            {
                s_Suggestions[module.name] = LooksmithTweaksPanel.Suggest(module);
                GUIUtility.ExitGUI();
            }
            if (list == null)
                return;
            if (list.Count == 0)
                GUILayout.Label("Nothing to suggest. The look suits what the camera sees.", s_Small);
            foreach (var s in list)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(s.title);
                    if (GUILayout.Button("Apply", GUILayout.Width(56)))
                    {
                        s.apply?.Invoke();
                        list.Remove(s);
                        s_Status = $"Applied \"{s.title}\". Save to keep it.";
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.Label(s.why, s_Small);
            }
        }
    }
}
