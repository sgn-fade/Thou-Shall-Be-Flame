using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Scene view overlay "Looksmith": the scene's look, Bypass, the before and after split and the debug views. Everything here is a view
    /// setting except the look, which is the same undoable scene edit as picking it in the gallery.
    /// </summary>
    [Overlay(typeof(SceneView), "looksmith-overlay", "Looksmith", true)]
    public sealed class LooksmithSceneOverlay : Overlay
    {
        static readonly List<LooksmithSceneOverlay> s_Instances = new List<LooksmithSceneOverlay>();

        /// <summary>The debug views in the order the overlay lists them.</summary>
        public static readonly (LooksmithDebugView view, string label)[] Views =
        {
            (LooksmithDebugView.None, "Final image"),
            (LooksmithDebugView.ValueCheck, "Value Check"),
            (LooksmithDebugView.LightingOnly, "Lighting Only"),
            (LooksmithDebugView.LinesOnly, "Lines Only"),
            (LooksmithDebugView.Normals, "Normals"),
            (LooksmithDebugView.Depth, "Depth"),
            (LooksmithDebugView.InputsQuad, "Inputs (4-up)"),
        };

        VisualElement m_Root;

        public override void OnCreated()
        {
            s_Instances.Add(this);
            LooksmithRuntime.StateChanged += Refresh;
            LooksmithCompare.Changed += SyncCompare;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            Undo.undoRedoPerformed += Refresh;
        }

        public override void OnWillBeDestroyed()
        {
            s_Instances.Remove(this);
            LooksmithRuntime.StateChanged -= Refresh;
            LooksmithCompare.Changed -= SyncCompare;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            Undo.undoRedoPerformed -= Refresh;
        }

        void OnSceneChanged(Scene from, Scene to) => Refresh();

        /// <summary>Rebuilds every open overlay (after a look is applied elsewhere).</summary>
        public static void RefreshAll()
        {
            foreach (var o in s_Instances.ToList())
                o.Refresh();
        }

        public override VisualElement CreatePanelContent()
        {
            m_Root = new VisualElement { name = "looksmith-overlay", style = { minWidth = 230, paddingLeft = 4, paddingRight = 4, paddingBottom = 4 } };
            Fill(m_Root);
            return m_Root;
        }

        // The split moves while a slider or the line is dragged: update values in place, never rebuild.
        void SyncCompare()
        {
            if (m_Root == null)
                return;
            m_Root.Q<Toggle>("compare")?.SetValueWithoutNotify(LooksmithCompare.active);
            var split = m_Root.Q<Slider>("split");
            if (split != null)
            {
                split.SetValueWithoutNotify(LooksmithCompare.split);
                split.SetEnabled(LooksmithCompare.active);
            }
        }

        void Refresh()
        {
            if (m_Root == null)
                return;
            m_Root.Clear();
            Fill(m_Root);
        }

        /// <summary>The overlay's controls (also used by tests, which have no Scene view).</summary>
        public static void Fill(VisualElement root)
        {
            var volume = LooksmithLookGallery.SceneLookVolume(SceneManager.GetActiveScene());
            if (volume == null)
            {
                root.Add(new Label("This scene isn't styled.") { style = { whiteSpace = WhiteSpace.Normal } });
                root.Add(new Button(() => LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Home)) { text = "Open the Looksmith Hub", name = "open-hub" });
            }
            else
            {
                var looks = LooksmithLooks.All.Select(d => d.name).ToList();
                var shipped = LooksmithLookGallery.ShippedLookName(volume.sharedProfile);
                var variant = shipped != null ? LooksmithLooks.FindVariant(shipped) : null;
                var current = variant != null ? variant.look : shipped ?? (volume.sharedProfile != null ? volume.sharedProfile.name : "(none)");
                if (shipped == null)
                    looks.Insert(0, current);
                var look = new DropdownField("Look", looks, current) { name = "look", tooltip = "The scene's look. Undo puts the previous one back." };
                look.RegisterValueChangedCallback(e =>
                {
                    if (LooksmithLookGallery.ShippedProfile(e.newValue) != null)
                        LooksmithLookGallery.ApplyLook(volume, e.newValue);
                });
                root.Add(look);
                if (shipped != null)
                {
                    // the look's variants: Night, Realistic, Low Poly, Cozy and its own
                    var kinds = new[] { "Default" }.Concat(LooksmithLooks.VariantsOf(current).Select(v => v.kind)).ToList();
                    var kind = new DropdownField("Variant", kinds, variant != null ? variant.kind : "Default") { name = "variant",
                        tooltip = "The look fitted to your game: Night for dark scenes, Realistic for detailed assets, Low Poly, Cozy, and a few of its own." };
                    kind.RegisterValueChangedCallback(e =>
                    {
                        var name = e.newValue == "Default" ? current : $"{current} ({e.newValue})";
                        if (LooksmithLookGallery.ShippedProfile(name) != null)
                            LooksmithLookGallery.ApplyLook(volume, name);
                    });
                    root.Add(kind);
                }
            }

            var bypass = new Toggle("Bypass") { value = LooksmithRuntime.Bypass, name = "bypass", tooltip = "Looksmith off: materials render as standard PBR and every Looksmith pass is skipped." };
            bypass.RegisterValueChangedCallback(e => LooksmithUserSettings.SetBypass(e.newValue));
            root.Add(bypass);

            var compare = new Toggle("Before and after") { value = LooksmithCompare.active, name = "compare", tooltip = "Without Looksmith on the left of the line, with your look on the right. Drag the line in the view to move it." };
            compare.RegisterValueChangedCallback(e =>
            {
                LooksmithCompare.active = e.newValue;
                SceneView.RepaintAll();
            });
            root.Add(compare);
            var split = new Slider(0f, 1f) { value = LooksmithCompare.split, name = "split", tooltip = "Where the line sits." };
            split.SetEnabled(LooksmithCompare.active);
            split.RegisterValueChangedCallback(e =>
            {
                LooksmithCompare.split = e.newValue;
                SceneView.RepaintAll();
            });
            root.Add(split);

            var labels = Views.Select(v => v.label).ToList();
            int index = System.Array.FindIndex(Views, v => v.view == LooksmithDebug.mode);
            var view = new DropdownField("View", labels, Mathf.Max(index, 0)) { name = "debug-view",
                tooltip = "Value Check: greys only. Lighting Only: Looksmith surfaces in mid-grey. Lines Only: the Look's lines on white." };
            view.RegisterValueChangedCallback(e =>
            {
                LooksmithDebug.mode = Views[labels.IndexOf(e.newValue)].view;
                SceneView.RepaintAll();
            });
            root.Add(view);
        }
    }

    /// <summary>Draws the before and after line in Scene views and lets you drag it.</summary>
    [InitializeOnLoad]
    static class LooksmithCompareSceneGUI
    {
        static bool s_Dragging;

        static LooksmithCompareSceneGUI()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!LooksmithCompare.active || LooksmithRuntime.Bypass)
                return;
            var size = view.position.size;
            float x = LooksmithCompare.split * size.x;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Handles.BeginGUI();
            var hit = new Rect(x - 5f, 0f, 10f, size.y);
            EditorGUIUtility.AddCursorRect(hit, MouseCursor.ResizeHorizontal);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && hit.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    s_Dragging = true;
                    e.Use();
                    break;
                case EventType.MouseDrag when s_Dragging && GUIUtility.hotControl == id:
                    LooksmithCompare.split = e.mousePosition.x / Mathf.Max(size.x, 1f);
                    e.Use();
                    SceneView.RepaintAll();
                    break;
                case EventType.MouseUp when s_Dragging:
                    s_Dragging = false;
                    if (GUIUtility.hotControl == id)
                        GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint:
                    EditorGUI.DrawRect(new Rect(x - 1f, 0f, 2f, size.y), new Color(1f, 1f, 1f, 0.85f));
                    var style = EditorStyles.whiteMiniLabel;
                    GUI.Label(new Rect(x - 52f, size.y - 44f, 48f, 16f), "A plain", style);
                    GUI.Label(new Rect(x + 6f, size.y - 44f, 48f, 16f), "B Looksmith", style);
                    break;
            }
            Handles.EndGUI();
        }
    }
}
