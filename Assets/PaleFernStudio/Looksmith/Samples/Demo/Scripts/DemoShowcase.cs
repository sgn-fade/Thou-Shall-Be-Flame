using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>A panel that lives in the showcase's UI (the spot cards): built and refreshed with it.</summary>
    public interface IDemoPanel
    {
        void Build(VisualElement root);
        void Refresh();
    }

    /// <summary>
    /// The demo's on-screen panel: walk around and flip through the Looks and the times of day, switch Looksmith off,
    /// compare side by side, peek at the debug views. Keys are listed on the panel; Tab hides it. In Play Mode the
    /// current Look is a copy of its profile, so spot moods never touch the shipped looks; the copy stands in for its
    /// asset in Tweaks, so a change made there lands on the Look itself (kept if you Save it, dropped otherwise).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Looksmith/Demo/Demo Showcase")]
    public sealed class DemoShowcase : MonoBehaviour
    {
        [Serializable]
        public class LookEntry
        {
            public string name;
            [TextArea] public string description;
            public VolumeProfile profile;
            [Tooltip("The time of day this Look was tuned with (M applies it).")]
            public LooksmithSkyPreset mood;
        }

        /// <summary>An extra key on the panel (the Pro time-of-day control registers one).</summary>
        public sealed class Control
        {
            public DemoKey[] keys;
            public string label;
            public Func<string> status;
            public Action<DemoKey> onPress;
        }

        public LookEntry[] looks = Array.Empty<LookEntry>();
        public LooksmithSkyPreset[] skies = Array.Empty<LooksmithSkyPreset>();
        public Volume volume;
        public LooksmithSky sky;
        public int startLook;
        public int startSky;
        [Tooltip("Seconds a sky change takes.")]
        [Min(0f)] public float skyTransition = 2.5f;

        static readonly List<Control> s_Controls = new List<Control>();
        public static void Register(Control c) { if (!s_Controls.Contains(c)) s_Controls.Add(c); s_Dirty = true; }
        public static void Unregister(Control c) { s_Controls.Remove(c); s_Dirty = true; }
        /// <summary>Raised when the visitor picks a sky preset (the time-of-day animation stops for it).</summary>
        public static event Action SkyChosen;
        static bool s_Dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Controls.Clear();
            SkyChosen = null;
            s_Dirty = false;
        }

        static readonly LooksmithDebugView[] k_DebugViews = { LooksmithDebugView.None, LooksmithDebugView.ValueCheck, LooksmithDebugView.LightingOnly, LooksmithDebugView.LinesOnly, LooksmithDebugView.Normals };

        int m_Look, m_Sky, m_Debug;
        bool m_PanelShown = true;
        VolumeProfile m_Working;
        readonly List<IDemoPanel> m_Panels = new List<IDemoPanel>();

        /// <summary>The profile on screen: a runtime copy of the current Look in Play Mode (change it freely), else the Look itself.</summary>
        public VolumeProfile workingProfile => m_Working != null ? m_Working : (looks.Length > 0 ? looks[m_Look].profile : null);
        public LookEntry currentLook => looks.Length > 0 ? looks[m_Look] : null;
        /// <summary>Raised after the Look on screen changes (a new copy: earlier tweaks are gone).</summary>
        public event Action LookChanged;
        float m_ToastTime = -10f;

        VisualElement m_Root, m_Panel, m_Dots, m_Keys, m_Toast, m_Hint, m_SplitLine;
        Label m_Name, m_Description, m_SkyLabel, m_ModeLabel, m_ToastLabel, m_ToastSub, m_SplitLeft, m_SplitRight;

        public int lookIndex => m_Look;
        public int skyIndex => m_Sky;

        void OnEnable()
        {
            m_Look = Mathf.Clamp(startLook, 0, Mathf.Max(looks.Length - 1, 0));
            m_Sky = Mathf.Clamp(startSky, 0, Mathf.Max(skies.Length - 1, 0));
            Build();
            ApplyLook(false);
            Refresh();
        }

        void OnDisable()
        {
            DropWorking();
            if (!Application.isPlaying)
                return;
            LooksmithCompare.active = false;
            LooksmithDebug.mode = LooksmithDebugView.None;
            LooksmithRuntime.Bypass = false;
        }

        void DropWorking()
        {
            if (m_Working == null)
                return;
            foreach (var c in m_Working.components)
                if (c != null)
                {
                    LooksmithTweaksHooks.UnregisterStandIn(c);
                    Destroy(c);
                }
            LooksmithTweaksHooks.UnregisterStandIn(m_Working);
            Destroy(m_Working);
            m_Working = null;
        }

        /// <summary>Tells Tweaks the copy on screen stands in for the Look itself (a change made there lands on the Look).</summary>
        void RegisterWorking()
        {
            var source = currentLook?.profile;
            if (m_Working == null || source == null)
                return;
            var originals = source.components.Where(c => c != null).ToList();
            for (int i = 0; i < m_Working.components.Count && i < originals.Count; i++)
                LooksmithTweaksHooks.RegisterStandIn(m_Working.components[i], originals[i]);
            LooksmithTweaksHooks.RegisterStandIn(m_Working, source);
        }

        /// <summary>Shows a Look by name (spot moods); false when the showcase doesn't carry it.</summary>
        public bool ShowLook(string name, bool toast)
        {
            int i = Array.FindIndex(looks, l => l.name == name);
            if (i < 0)
                return false;
            m_Look = i;
            ApplyLook(toast);
            Refresh();
            return true;
        }

        /// <summary>Moves to a time of day (spot moods).</summary>
        public void ShowSky(LooksmithSkyPreset preset, string toastTitle = null, string toastSub = null)
        {
            if (preset == null || sky == null)
                return;
            int i = Array.IndexOf(skies, preset);
            if (i >= 0)
                m_Sky = i;
            SkyChosen?.Invoke();
            sky.TransitionTo(preset, skyTransition);
            if (toastTitle != null)
                Toast(toastTitle, toastSub);
        }

        /// <summary>Puts the current Look back as it shipped (drops every tweak).</summary>
        public void ResetLook()
        {
            ApplyLook(false);
            Toast(currentLook != null ? currentLook.name : "", "Back to the look as it comes");
        }

        public void ShowToast(string title, string sub) => Toast(title, sub);

        // ------------------------------------------------------------ input

        void Update()
        {
            if (!Application.isPlaying)
                return;
            bool changed = s_Dirty;
            s_Dirty = false;
            // the link to the Look can be lost when Play Mode starts: put it back
            if (m_Working != null && LooksmithTweaksHooks.SourceOf(m_Working) == m_Working)
                RegisterWorking();
            if (DemoInput.Pressed(DemoKey.NextLook)) { StepLook(1); changed = true; }
            if (DemoInput.Pressed(DemoKey.PrevLook)) { StepLook(-1); changed = true; }
            if (DemoInput.Pressed(DemoKey.NextSky)) { StepSky(1); changed = true; }
            if (DemoInput.Pressed(DemoKey.PrevSky)) { StepSky(-1); changed = true; }
            if (DemoInput.Pressed(DemoKey.Mood)) { ApplyMood(); changed = true; }
            if (DemoInput.Pressed(DemoKey.Bypass)) { LooksmithRuntime.Bypass = !LooksmithRuntime.Bypass; changed = true; }
            if (DemoInput.Pressed(DemoKey.Split)) { LooksmithCompare.active = !LooksmithCompare.active; LooksmithCompare.split = 0.5f; changed = true; }
            if (DemoInput.Pressed(DemoKey.DebugView)) { m_Debug = (m_Debug + 1) % k_DebugViews.Length; LooksmithDebug.mode = k_DebugViews[m_Debug]; changed = true; }
            if (DemoInput.Pressed(DemoKey.Panel)) { m_PanelShown = !m_PanelShown; changed = true; }
            foreach (var c in s_Controls)
                foreach (var k in c.keys)
                    if (DemoInput.Pressed(k)) { c.onPress?.Invoke(k); changed = true; }

            // with the mouse free, the before and after line follows it
            var player = DemoPlayer.current;
            if (LooksmithCompare.active && (player == null || !player.mouseCaptured))
                LooksmithCompare.split = Mathf.Clamp01(DemoInput.MousePosition.x / Mathf.Max(Screen.width, 1));

            if (changed || Time.frameCount % 15 == 0)
                Refresh();
            float toast = Time.unscaledTime - m_ToastTime;
            if (m_Toast != null)
                m_Toast.style.opacity = toast < 1.6f ? 1f : Mathf.Clamp01(1f - (toast - 1.6f) / 0.6f);
            if (m_Hint != null)
                m_Hint.style.display = player != null && !player.mouseCaptured ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_SplitLine != null)
            {
                m_SplitLine.style.display = LooksmithCompare.active ? DisplayStyle.Flex : DisplayStyle.None;
                m_SplitLine.style.left = Length.Percent(LooksmithCompare.split * 100f);
            }
        }

        public void StepLook(int d)
        {
            if (looks.Length == 0)
                return;
            m_Look = (m_Look + d + looks.Length) % looks.Length;
            ApplyLook(true);
        }

        public void StepSky(int d)
        {
            if (skies.Length == 0 || sky == null)
                return;
            m_Sky = (m_Sky + d + skies.Length) % skies.Length;
            SkyChosen?.Invoke();
            sky.TransitionTo(skies[m_Sky], skyTransition);
            Toast(skies[m_Sky].name, "Time of day");
        }

        void ApplyMood()
        {
            if (looks.Length == 0 || looks[m_Look].mood == null || sky == null)
                return;
            int i = Array.IndexOf(skies, looks[m_Look].mood);
            if (i >= 0)
                m_Sky = i;
            SkyChosen?.Invoke();
            sky.TransitionTo(looks[m_Look].mood, skyTransition);
            Toast(looks[m_Look].mood.name, "The Look's own mood");
        }

        void ApplyLook(bool toast)
        {
            if (looks.Length == 0 || volume == null)
                return;
            var entry = looks[m_Look];
            DropWorking();
            if (Application.isPlaying && entry.profile != null)
            {
                // a copy to tweak (an asset changed in Play Mode would stay changed)
                m_Working = Instantiate(entry.profile);
                m_Working.name = entry.profile.name + " (demo copy)";
                var copies = new List<VolumeComponent>();
                foreach (var c in entry.profile.components)
                    if (c != null)
                        copies.Add(Instantiate(c));
                m_Working.components = copies;
                RegisterWorking();
            }
            volume.sharedProfile = m_Working != null ? m_Working : entry.profile;
            volume.enabled = entry.profile != null;
            if (toast)
                Toast(entry.name, entry.profile == null ? "No look, just URP" : "Look");
            LookChanged?.Invoke();
        }

        void Toast(string title, string sub)
        {
            if (m_ToastLabel == null)
                return;
            m_ToastLabel.text = title;
            m_ToastSub.text = sub;
            m_ToastTime = Time.unscaledTime;
        }

        // ------------------------------------------------------------ UI

        static readonly Color k_Panel = new Color(0.07f, 0.08f, 0.1f, 0.74f);
        static readonly Color k_Text = new Color(0.95f, 0.94f, 0.9f);
        static readonly Color k_Muted = new Color(0.72f, 0.72f, 0.7f);
        static readonly Color k_Accent = new Color(0.96f, 0.72f, 0.42f);

        static Label Text(string s, int size, Color colour, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(s);
            l.style.fontSize = size;
            l.style.color = colour;
            l.style.unityFontStyleAndWeight = style;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            l.style.paddingLeft = l.style.paddingRight = 0;
            return l;
        }

        static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        static VisualElement Divider()
        {
            var d = new VisualElement();
            d.style.height = 1;
            d.style.marginTop = 10;
            d.style.marginBottom = 10;
            d.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            return d;
        }

        void Build()
        {
            var doc = GetComponent<UIDocument>();
            m_Root = doc.rootVisualElement;
            if (m_Root == null)
                return;
            m_Root.Clear();
            m_Root.pickingMode = PickingMode.Ignore;
            m_Root.style.flexGrow = 1;

            m_Panel = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Panel.style.position = Position.Absolute;
            m_Panel.style.left = 28;
            m_Panel.style.top = 28;
            m_Panel.style.width = 390;
            m_Panel.style.paddingLeft = m_Panel.style.paddingRight = 20;
            m_Panel.style.paddingTop = m_Panel.style.paddingBottom = 18;
            m_Panel.style.backgroundColor = k_Panel;
            Round(m_Panel, 16);
            m_Root.Add(m_Panel);

            var brand = Text("LOOKSMITH  ·  LOOK SHOWCASE", 12, k_Accent, FontStyle.Bold);
            brand.style.letterSpacing = 2;
            m_Panel.Add(brand);
            m_Name = Text("", 30, k_Text, FontStyle.Bold);
            m_Name.style.marginTop = 6;
            m_Panel.Add(m_Name);
            m_Description = Text("", 14, k_Muted);
            m_Panel.Add(m_Description);
            m_Dots = new VisualElement();
            m_Dots.style.flexDirection = FlexDirection.Row;
            m_Dots.style.marginTop = 10;
            m_Panel.Add(m_Dots);
            m_Panel.Add(Divider());
            m_SkyLabel = Text("", 15, k_Text);
            m_Panel.Add(m_SkyLabel);
            m_ModeLabel = Text("", 15, k_Text);
            m_Panel.Add(m_ModeLabel);
            m_Panel.Add(Divider());
            m_Keys = new VisualElement();
            m_Panel.Add(m_Keys);

            m_Toast = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Toast.style.position = Position.Absolute;
            m_Toast.style.left = 0;
            m_Toast.style.right = 0;
            m_Toast.style.top = Length.Percent(12);
            m_Toast.style.alignItems = Align.Center;
            m_ToastLabel = Text("", 46, k_Text, FontStyle.Bold);
            m_ToastLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_ToastLabel.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 8, color = new Color(0, 0, 0, 0.6f) };
            m_ToastSub = Text("", 16, k_Accent, FontStyle.Bold);
            m_ToastSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_ToastSub.style.letterSpacing = 2;
            m_ToastSub.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 6, color = new Color(0, 0, 0, 0.6f) };
            m_Toast.Add(m_ToastSub);
            m_Toast.Add(m_ToastLabel);
            m_Toast.style.opacity = 0f;
            m_Root.Add(m_Toast);

            m_Hint = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Hint.style.position = Position.Absolute;
            m_Hint.style.left = 0;
            m_Hint.style.right = 0;
            m_Hint.style.bottom = 36;
            m_Hint.style.alignItems = Align.Center;
            var hint = Text("Click to look around   ·   WASD walk   ·   Shift run   ·   Space jump", 15, k_Text);
            hint.style.backgroundColor = k_Panel;
            hint.style.paddingLeft = hint.style.paddingRight = 16;
            hint.style.paddingTop = hint.style.paddingBottom = 8;
            Round(hint, 10);
            m_Hint.Add(hint);
            m_Root.Add(m_Hint);

            m_SplitLine = new VisualElement { pickingMode = PickingMode.Ignore };
            m_SplitLine.style.position = Position.Absolute;
            m_SplitLine.style.top = 0;
            m_SplitLine.style.bottom = 0;
            m_SplitLine.style.width = 2;
            m_SplitLine.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            m_SplitLeft = Text("WITHOUT LOOKSMITH", 13, k_Text, FontStyle.Bold);
            m_SplitRight = Text("WITH LOOKSMITH", 13, k_Text, FontStyle.Bold);
            foreach (var (l, left) in new[] { (m_SplitLeft, true), (m_SplitRight, false) })
            {
                l.style.position = Position.Absolute;
                l.style.bottom = 90;
                if (left) l.style.right = 10; else l.style.left = 10;
                l.style.backgroundColor = k_Panel;
                l.style.paddingLeft = l.style.paddingRight = 8;
                l.style.paddingTop = l.style.paddingBottom = 4;
                l.style.letterSpacing = 2;
                Round(l, 6);
                m_SplitLine.Add(l);
            }
            m_Root.Add(m_SplitLine);

            m_Panels.Clear();
            GetComponents(m_Panels);
            foreach (var p in m_Panels)
                p.Build(m_Root);
        }

        VisualElement KeyRow(string keys, string label, string status = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;
            foreach (var k in keys.Split(' '))
            {
                var cap = Text(k, 12, k_Text, FontStyle.Bold);
                cap.style.minWidth = 22;
                cap.style.unityTextAlign = TextAnchor.MiddleCenter;
                cap.style.paddingLeft = cap.style.paddingRight = 6;
                cap.style.paddingTop = cap.style.paddingBottom = 2;
                cap.style.marginRight = 4;
                cap.style.marginBottom = 0;
                cap.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                Round(cap, 5);
                row.Add(cap);
            }
            var l = Text(label, 13, k_Muted);
            l.style.marginLeft = 6;
            l.style.marginBottom = 0;
            row.Add(l);
            if (!string.IsNullOrEmpty(status))
            {
                var s = Text(status, 13, k_Accent);
                s.style.marginLeft = 6;
                s.style.marginBottom = 0;
                row.Add(s);
            }
            return row;
        }

        void Refresh()
        {
            if (m_Panel == null)
                return;
            m_Panel.style.display = m_PanelShown ? DisplayStyle.Flex : DisplayStyle.None;
            var entry = looks.Length > 0 ? looks[m_Look] : null;
            m_Name.text = entry != null ? entry.name : "No Looks";
            m_Description.text = entry != null ? entry.description : "";
            m_Dots.Clear();
            for (int i = 0; i < looks.Length; i++)
            {
                var dot = new VisualElement();
                dot.style.width = i == m_Look ? 18 : 7;
                dot.style.height = 7;
                dot.style.marginRight = 5;
                dot.style.backgroundColor = i == m_Look ? k_Accent : new Color(1f, 1f, 1f, 0.28f);
                Round(dot, 4);
                m_Dots.Add(dot);
            }
            string skyName = sky != null && sky.preset != null ? sky.preset.name : (skies.Length > 0 ? skies[m_Sky].name : "-");
            m_SkyLabel.text = $"<color=#B8B8B2>Time of day</color>   {skyName}";
            string mode = LooksmithRuntime.Bypass ? "Looksmith off" : LooksmithCompare.active ? "Before and after, without Looksmith on the left" : "Looksmith on";
            if (k_DebugViews[m_Debug] != LooksmithDebugView.None)
                mode += "   ·   " + DebugName(k_DebugViews[m_Debug]);
            m_ModeLabel.text = $"<color=#B8B8B2>Showing</color>   {mode}";
            m_Keys.Clear();
            m_Keys.Add(KeyRow("Q E", "Previous / next Look"));
            m_Keys.Add(KeyRow("Z C", "Previous / next time of day"));
            m_Keys.Add(KeyRow("M", "This Look's own mood", entry != null && entry.mood != null ? entry.mood.name : null));
            foreach (var c in s_Controls)
            {
                var keys = string.Join(" ", Array.ConvertAll(c.keys, DemoInput.Label));
                m_Keys.Add(KeyRow(keys, c.label, c.status?.Invoke()));
            }
            m_Keys.Add(KeyRow("B", "Looksmith on / off"));
            m_Keys.Add(KeyRow("V", "Before and after (move the mouse)"));
            m_Keys.Add(KeyRow("G", "Debug views"));
            m_Keys.Add(KeyRow("F", "Fly", DemoPlayer.current != null && DemoPlayer.current.flying ? "on" : null));
            m_Keys.Add(KeyRow("Tab", "Hide this panel"));
            foreach (var p in m_Panels)
                p.Refresh();
        }

        /// <summary>A row of key caps and a label, styled like the panel's (for the other panels).</summary>
        public VisualElement Row(string keys, string label, string status = null) => KeyRow(keys, label, status);
        public static Label Label(string s, int size, Color colour, FontStyle style = FontStyle.Normal) => Text(s, size, colour, style);
        public static VisualElement Card()
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.position = Position.Absolute;
            card.style.paddingLeft = card.style.paddingRight = 18;
            card.style.paddingTop = card.style.paddingBottom = 14;
            card.style.backgroundColor = k_Panel;
            Round(card, 14);
            return card;
        }
        public static Color TextColour => k_Text;
        public static Color MutedColour => k_Muted;
        public static Color AccentColour => k_Accent;

        static string DebugName(LooksmithDebugView v)
        {
            switch (v)
            {
                case LooksmithDebugView.ValueCheck: return "Values only";
                case LooksmithDebugView.LightingOnly: return "Lighting only";
                case LooksmithDebugView.LinesOnly: return "Lines only";
                case LooksmithDebugView.Normals: return "Normals";
            }
            return v.ToString();
        }
    }
}
