using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Window > Looksmith > Hub: the one home. Home (Welcome, then ① Check → ② Apply → ③ Style, then the dashboard),
    /// Looks, Restyle, Changes, Help, and a Pro tab in Lite. Every write goes through a previewed Changeset with a backup.
    /// </summary>
    public sealed class LooksmithHubWindow : EditorWindow
    {
        public enum Tab { Home, Looks, Tweaks, Restyle, Changes, Help, Pro }
        public enum Step { Welcome, Check, Apply, Style, Dashboard }

        [SerializeField] Tab m_Tab;
        [SerializeField] Step m_Step;
        [SerializeField] bool m_BackupFirst = true;

        List<ScanFinding> m_Findings;
        Changeset m_Pending;
        VisualElement m_Page;
        ScrollView m_Scroll;
        Tab m_ScrollTab;
        readonly Dictionary<Tab, Button> m_TabButtons = new Dictionary<Tab, Button>();

        /// <summary>Optional page builders other modules contribute (the Look Core fills the Looks tab and the Style step).</summary>
        public static Func<VisualElement> looksPageFactory;
        public static Func<VisualElement> stylePageFactory;
        public static Func<VisualElement> restylePageFactory;
        /// <summary>The Tweaks tab (set by the Tweaks panel).</summary>
        public static Func<VisualElement> tweaksPageFactory;

        public Tab tab => m_Tab;
        public Step step => m_Step;

        /// <summary>The Hub on its Tweaks tab: every setting of the scene's Looksmith modules in one place.</summary>
        [MenuItem("Window/Looksmith/Tweaks", priority = 2)]
        static void OpenTweaks() => ShowTab(Tab.Tweaks);

        [MenuItem("Window/Looksmith/Hub", priority = 1)]
        public static LooksmithHubWindow Open()
        {
            var w = GetWindow<LooksmithHubWindow>();
            w.titleContent = new GUIContent("Looksmith");
            w.minSize = new Vector2(520, 420);
            return w;
        }

        void OnEnable()
        {
            ChangesetRunner.Completed += OnChangesetCompleted;
            LooksmithRuntime.StateChanged += Rebuild;
            // Light checks re-run when a scene opens, the pipeline changes or the quality level changes (plan §4.4).
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            RenderPipelineManager.activeRenderPipelineTypeChanged += Rebuild;
            QualitySettings.activeQualityLevelChanged += OnQualityChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Rebuild();
        }

        void OnDisable()
        {
            ChangesetRunner.Completed -= OnChangesetCompleted;
            LooksmithRuntime.StateChanged -= Rebuild;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= Rebuild;
            QualitySettings.activeQualityLevelChanged -= OnQualityChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnSceneChanged(Scene from, Scene to) => OnSceneSwitch();
        void OnSceneOpened(Scene scene, OpenSceneMode mode) => OnSceneSwitch();
        void OnQualityChanged(int from, int to) => Rebuild();

        void OnSceneSwitch()
        {
            m_Findings = null;
            m_Pending = null;
            Rebuild();
        }

        void OnChangesetCompleted(SnapshotManifest m)
        {
            if (m.status == SnapshotManifest.Applied && m_Step == Step.Apply)
                m_Step = Step.Style;
            m_Findings = LooksmithScan.Run();
            Rebuild();
        }

        public void CreateGUI()
        {
            // Returning to a project that is already set up opens on the dashboard, not the welcome.
            if (m_Step == Step.Welcome && (AnySceneStyled() || LooksmithBackups.List().Count > 0))
                m_Step = Step.Dashboard;
            Rebuild();
        }

        static bool AnySceneStyled() =>
            LooksmithFind.All<LooksmithLookVolume>(FindObjectsInactive.Include).Length > 0;

        public void Rebuild()
        {
            // a rebuild on the same tab (a look picked far down the gallery, a fix applied) keeps the page where it was
            // scrolled to; another tab opens at its top
            var keep = m_Scroll != null && m_ScrollTab == m_Tab ? m_Scroll.scrollOffset : Vector2.zero;
            var root = rootVisualElement;
            root.Clear();
            var uss = FindStyleSheet();
            if (uss != null && !root.styleSheets.Contains(uss))
                root.styleSheets.Add(uss);
            root.AddToClassList("looksmith-root");

            var header = new VisualElement();
            header.AddToClassList("looksmith-header");
            var title = new Label(LooksmithEdition.isPro ? "Looksmith" : "Looksmith Lite");
            title.AddToClassList("looksmith-title");
            header.Add(title);
            var bypass = new Toggle("Turn Looksmith off") { value = LooksmithRuntime.Bypass, name = "bypass", tooltip = "Shows your scene without Looksmith so you can compare. Looksmith materials draw as standard PBR and Looksmith's effects are skipped." };
            bypass.RegisterValueChangedCallback(e => LooksmithUserSettings.SetBypass(e.newValue));
            header.Add(bypass);
            var issues = LooksmithHealth.Issues();
            var badge = new Label(LooksmithHealth.Label(issues)) { name = "health", tooltip = issues.Count > 0 ? string.Join("\n", issues) : "Nothing critical has changed." };
            badge.AddToClassList("looksmith-badge");
            badge.AddToClassList(issues.Count == 0 ? "looksmith-badge--ok" : "looksmith-badge--warn");
            header.Add(badge);
            root.Add(header);

            if (m_Tab == Tab.Pro && !LooksmithProCards.tabShown)
                m_Tab = Tab.Home;
            var tabs = new VisualElement();
            tabs.AddToClassList("looksmith-tabs");
            m_TabButtons.Clear();
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                if (t == Tab.Pro && !LooksmithProCards.tabShown)
                    continue;   // Lite only, unless the user hid it
                var tab = t;
                var b = new Button(() => { m_Tab = tab; Rebuild(); }) { text = t.ToString(), name = "tab-" + t };
                b.AddToClassList("looksmith-tab");
                if (t == m_Tab)
                    b.AddToClassList("looksmith-tab--active");
                m_TabButtons[t] = b;
                tabs.Add(b);
            }
            root.Add(tabs);

            if (LooksmithPlayMode.active)
            {
                var banner = new Label(LooksmithPlayMode.Note) { name = "play-mode-note" };
                banner.AddToClassList("looksmith-play-note");
                root.Add(banner);
            }

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            m_Page = new VisualElement();
            m_Page.AddToClassList("looksmith-page");
            scroll.Add(m_Page);
            root.Add(scroll);
            m_Scroll = scroll;
            m_ScrollTab = m_Tab;
            if (keep.y > 0f || keep.x > 0f)
                RestoreScroll(scroll, keep);

            switch (m_Tab)
            {
                case Tab.Home: BuildHome(m_Page); break;
                case Tab.Looks: m_Page.Add(looksPageFactory?.Invoke() ?? Muted("Looks appear here once the Look Core is installed.")); break;
                case Tab.Tweaks: m_Page.Add(tweaksPageFactory?.Invoke() ?? Muted("Tweaks appear here once the Look Core is installed.")); break;
                case Tab.Restyle: m_Page.Add(restylePageFactory?.Invoke() ?? Muted("Restyle converts your materials to Looksmith shaders with a preview and a backup.")); break;
                case Tab.Changes: BuildChanges(m_Page); break;
                case Tab.Help: BuildHelp(m_Page); break;
                case Tab.Pro: LooksmithProCards.Build(m_Page); break;
            }
        }

        // The offset is clamped to the content's size, which is known only once the new page is laid out.
        static void RestoreScroll(ScrollView scroll, Vector2 offset)
        {
            EventCallback<GeometryChangedEvent> restore = null;
            restore = e =>
            {
                if (scroll.contentContainer.layout.height < 1f)
                    return;
                scroll.contentContainer.UnregisterCallback(restore);
                scroll.scrollOffset = offset;
            };
            scroll.contentContainer.RegisterCallback(restore);
        }

        static StyleSheet FindStyleSheet()
        {
            var guid = AssetDatabase.FindAssets("LooksmithHub t:StyleSheet").FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
        }

        // ------------------------------------------------------------------ Home

        void BuildHome(VisualElement page)
        {
            if (m_Step == Step.Dashboard)
            {
                BuildDashboard(page);
                return;
            }
            if (m_Findings == null && m_Step != Step.Welcome)
                m_Findings = LooksmithScan.Run();
            if (m_Step == Step.Welcome)
            {
                page.Add(H1("Welcome to Looksmith"));
                page.Add(Text("One look for your whole game: lighting, colour and lines from one profile. Nothing has changed in your project yet."));
                var row = Row();
                var scan = new Button(() => { m_Findings = LooksmithScan.Run(); m_Step = Step.Check; Rebuild(); }) { text = "Scan my project", name = "scan" };
                scan.AddToClassList("looksmith-primary");
                row.Add(scan);
                // the edition's own demo: the village in Pro, its centre in Lite (both match the search)
                var demos = AssetDatabase.FindAssets("t:Scene LooksmithDemo").Select(AssetDatabase.GUIDToAssetPath).ToList();
                var own = LooksmithEdition.isPro ? "LooksmithDemo" : "LooksmithDemoLite";
                var demoPath = demos.FirstOrDefault(d => System.IO.Path.GetFileNameWithoutExtension(d) == own) ?? demos.FirstOrDefault();
                var demo = new Button(() => EditorSceneManager.OpenScene(demoPath)) { text = "Open the demo", name = "demo" };
                demo.SetEnabled(demoPath != null && !EditorSceneManager.GetActiveScene().isDirty);
                row.Add(demo);
                page.Add(row);
                page.Add(Muted("The scan is read-only and takes a second or two."));
                page.Add(DocLink("Getting started", LooksmithDocs.GettingStarted));
                return;
            }

            var stepper = new VisualElement();
            stepper.AddToClassList("looksmith-stepper");
            foreach (var (s, label) in new[] { (Step.Check, "① Check"), (Step.Apply, "② Apply"), (Step.Style, "③ Style") })
            {
                var chip = new Button(() => { m_Step = s; Rebuild(); }) { text = label, name = "step-" + s };
                chip.AddToClassList("looksmith-step");
                if (s == m_Step) chip.AddToClassList("looksmith-step--active");
                else if (s < m_Step) chip.AddToClassList("looksmith-step--done");
                stepper.Add(chip);
            }
            page.Add(stepper);

            if (m_Step == Step.Check) BuildCheck(page);
            else if (m_Step == Step.Apply) BuildApply(page);
            else BuildStyle(page);
        }

        void BuildCheck(VisualElement page)
        {
            page.Add(H1("Check"));
            int blocking = m_Findings.Count(f => f.severity == ScanSeverity.Blocking);
            page.Add(Muted(blocking > 0 ? $"{blocking} blocking issue(s) must be solved first." : "Nothing blocks Looksmith. Recommended fixes are ticked; untick anything you'd rather keep."));
            foreach (ScanSeverity sev in Enum.GetValues(typeof(ScanSeverity)))
            {
                var group = m_Findings.Where(f => f.severity == sev).ToList();
                if (group.Count == 0)
                    continue;
                var foldout = new Foldout { text = $"{SeverityName(sev)} ({group.Count})", value = sev != ScanSeverity.Ready, name = "group-" + sev };
                foreach (var f in group)
                    foldout.Add(FindingRow(f));
                page.Add(foldout);
            }
            var next = new Button(() => { m_Pending = null; m_Step = Step.Apply; Rebuild(); }) { text = "Next: preview changes", name = "next-apply" };
            next.AddToClassList("looksmith-primary");
            next.SetEnabled(blocking == 0);
            page.Add(next);
            page.Add(new Button(() => { m_Findings = LooksmithScan.Run(); Rebuild(); }) { text = "Scan again", name = "scan-again" });
        }

        VisualElement FindingRow(ScanFinding f)
        {
            var card = new VisualElement { name = "finding-" + f.id };
            card.AddToClassList("looksmith-row");
            var sev = new Label(SeverityName(f.severity));
            sev.AddToClassList("looksmith-sev");
            sev.AddToClassList("looksmith-sev--" + f.severity.ToString().ToLowerInvariant());
            card.Add(sev);
            var col = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            if (f.fix != null)
            {
                var t = new Toggle(f.title) { value = f.fix.enabled, tooltip = f.writes.Length > 0 ? "Writes: " + f.writes : "" };
                t.RegisterValueChangedCallback(e => f.fix.enabled = e.newValue);
                col.Add(t);
            }
            else
                col.Add(Text(f.title));
            if (!string.IsNullOrEmpty(f.detail))
                col.Add(Muted(f.detail));
            card.Add(col);
            return card;
        }

        void BuildApply(VisualElement page)
        {
            var scene = SceneManager.GetActiveScene();
            m_Pending ??= LooksmithSetup.BuildApply(m_Findings.Where(f => f.fix == null || f.fix.enabled), scene);
            page.Add(H1("Apply"));
            page.Add(Muted("One preview, one backup, one Undo. Untick anything you don't want."));
            if (m_Pending.changes.Count == 0)
                page.Add(Text("Nothing to change: this project and scene are ready."));
            foreach (ChangeGroup g in Enum.GetValues(typeof(ChangeGroup)))
            {
                var items = m_Pending.changes.Where(c => c.group == g).ToList();
                if (items.Count == 0)
                    continue;
                page.Add(H2(GroupName(g)));
                if (g == ChangeGroup.Materials)
                {
                    foreach (var byGroup in items.GroupBy(c => (c as IRestyleChange)?.restyleGroup ?? "Other"))
                    {
                        var list = byGroup.ToList();
                        var t = new Toggle($"{byGroup.Key} ({list.Count} change(s))") { value = list.All(c => c.enabled), name = "restyle-group",
                            tooltip = "Converts these materials to Looksmith shaders; the originals are backed up first." };
                        t.RegisterValueChangedCallback(e => list.ForEach(c => c.enabled = e.newValue));
                        page.Add(t);
                        page.Add(Muted(string.Join(", ", list.Take(4).Select(c => c.title)) + (list.Count > 4 ? ", …" : "")));
                    }
                    page.Add(Muted("Shaders Looksmith doesn't know are left as they are; see the Restyle tab to map them."));
                    continue;
                }
                foreach (var c in items)
                {
                    var t = new Toggle(c.title) { value = c.enabled, tooltip = c.detail };
                    t.RegisterValueChangedCallback(e => c.enabled = e.newValue);
                    page.Add(t);
                    if (!string.IsNullOrEmpty(c.detail))
                        page.Add(Muted(c.detail));
                }
            }
            var backup = new Toggle("Back up first") { value = m_BackupFirst, name = "backup-first", tooltip = "Copies every file about to change to LooksmithBackups/ (outside Assets) so Revert is exact." };
            backup.RegisterValueChangedCallback(e => m_BackupFirst = e.newValue);
            page.Add(backup);
            var git = GitNotice();
            if (git != null)
                page.Add(git);
            var apply = new Button(() => ApplyPending(scene)) { text = ChangesetRunner.isRunning ? "Applying…" : "Apply", name = "apply" };
            apply.AddToClassList("looksmith-primary");
            apply.SetEnabled(!ChangesetRunner.isRunning && !LooksmithPlayMode.active && m_Pending.Enabled.Any());
            if (LooksmithPlayMode.active)
                apply.tooltip = LooksmithPlayMode.WritesDisabled;
            page.Add(apply);
            page.Add(DocLink("What Apply changes, and how Revert works", LooksmithDocs.Backups));
        }

        void ApplyPending(Scene scene)
        {
            if (ApplyWithPrompts(m_Pending, m_BackupFirst))
                m_Pending = null;
            Rebuild();
        }

        /// <summary>Test hook: replaces the confirmation dialogs (title, message, ok, cancel → ok?). Null shows the real dialogs.</summary>
        public static Func<string, string, string, string, bool> dialogOverride;

        /// <summary>Asks the user (or the test hook).</summary>
        public static bool Confirm(string title, string message, string ok, string cancel) =>
            dialogOverride?.Invoke(title, message, ok, cancel) ?? EditorUtility.DisplayDialog(title, message, ok, cancel);

        /// <summary>P8: in a git project, recommend a commit next to the Apply button (a note, not another dialog).</summary>
        public static VisualElement GitNotice()
        {
            if (!VcsGuard.gitDetected)
                return null;
            var note = Muted("This project uses git: commit first if you'd like your own restore point as well as Looksmith's backup.");
            note.name = "git-notice";
            return note;
        }

        /// <summary>
        /// The one way the Hub applies a Changeset: offers to save unsaved work (backups need clean files), then starts
        /// the run. Returns false if the user cancelled. (The git recommendation, P8, sits next to each Apply button.)
        /// </summary>
        public static bool ApplyWithPrompts(Changeset changeset, bool backupFirst)
        {
            if (LooksmithPlayMode.active)
            {
                Debug.Log("Looksmith: " + LooksmithPlayMode.WritesDisabled);
                return false;
            }
            bool dirtyScenes = changeset.Enabled.SelectMany(c => c.Scenes()).Any(p => SceneManager.GetSceneByPath(p).isDirty);
            if (DirtyAssets.Paths().Any() || dirtyScenes)
            {
                if (!Confirm("Save first?", "Looksmith backs up files before changing them, so it only works on saved work. Save the scene and project now?", "Save and continue", "Cancel"))
                    return false;
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
            }
            try
            {
                ChangesetRunner.Start(changeset, backupFirst);
                return true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Looksmith", e.Message, "OK");
                return false;
            }
        }

        /// <summary>Opens the Hub on a tab (Restyle… menu, "Restore originals").</summary>
        public static LooksmithHubWindow ShowTab(Tab tab)
        {
            var w = Open();
            w.m_Tab = tab;
            w.Rebuild();
            return w;
        }

        void BuildStyle(VisualElement page)
        {
            page.Add(H1("Style"));
            page.Add(stylePageFactory?.Invoke() ?? Muted("Pick a look from the gallery. Your scene is set up."));
            var done = new Button(() => { m_Step = Step.Dashboard; Rebuild(); }) { text = "Done", name = "style-done", tooltip = "Go to the Home dashboard. You can change the look any time." };
            done.AddToClassList("looksmith-primary");
            page.Add(done);
        }

        // ------------------------------------------------------------------ Dashboard (plan §4.4)

        void BuildDashboard(VisualElement page)
        {
            var scene = SceneManager.GetActiveScene();
            page.Add(SceneCard(scene));
            page.Add(LookDoctorCard());
            page.Add(FeaturesCard());
            page.Add(DoctorCard());
            page.Add(ChangesCard());
            page.Add(HelpCard());
        }

        VisualElement SceneCard(Scene active)
        {
            var card = Card("Scenes", "card-scene");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                var status = SceneStatus(scene, out int unstyledMaterials);
                var row = Row();
                row.name = "scene-" + scene.name;
                row.Add(new Label(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name) { style = { unityFontStyleAndWeight = scene == active ? FontStyle.Bold : FontStyle.Normal, flexGrow = 1 } });
                var chip = new Label(status) { name = "scene-status" };
                chip.AddToClassList("looksmith-badge");
                chip.AddToClassList(status == "Styled" ? "looksmith-badge--ok" : "looksmith-badge--warn");
                row.Add(chip);
                card.Add(row);
                if (scene != active)
                    continue;
                if (status == "Not styled")
                {
                    var add = new Button(() => { m_Findings = LooksmithScan.Run(); m_Pending = null; m_Step = Step.Check; Rebuild(); }) { text = "Add Looksmith to this scene", name = "add-looksmith",
                        tooltip = "Scans, then previews the Look Volume (and any recommended fixes) before anything is written." };
                    add.AddToClassList("looksmith-primary");
                    card.Add(add);
                    continue;
                }
                var volume = LooksmithLookGallery.SceneLookVolume(scene);
                var lookName = LooksmithLookGallery.ShippedLookName(volume != null ? volume.sharedProfile : null)
                               ?? (volume != null && volume.sharedProfile != null ? volume.sharedProfile.name : "(none)");
                var lookRow = Row();
                lookRow.Add(Text($"Look: {lookName}"));
                lookRow.Add(new Button(() => { m_Tab = Tab.Looks; Rebuild(); }) { text = "Change look…", name = "change-look", tooltip = "Opens the look gallery." });
                card.Add(lookRow);
                if (volume != null && volume.sharedProfile != null)
                    card.Add(FitRow(volume.sharedProfile));
                var labels = LooksmithSceneOverlay.Views.Select(v => v.label).ToList();
                int index = Array.FindIndex(LooksmithSceneOverlay.Views, v => v.view == LooksmithDebug.mode);
                var view = new DropdownField("Debug view", labels, Mathf.Max(index, 0)) { name = "debug-view",
                    tooltip = "Value Check: greys only. Lighting Only: Looksmith surfaces in mid-grey. Lines Only: the Look's lines on white." };
                view.RegisterValueChangedCallback(e =>
                {
                    LooksmithDebug.mode = LooksmithSceneOverlay.Views[labels.IndexOf(e.newValue)].view;
                    SceneView.RepaintAll();
                });
                card.Add(view);
                card.Add(LooksmithCompareControls.Build());
                if (unstyledMaterials > 0)
                {
                    var restyle = new Button(() => { m_Tab = Tab.Restyle; Rebuild(); }) { text = $"{unstyledMaterials} material(s) in this scene aren't restyled yet → Restyle", name = "restyle-chip",
                        tooltip = "Opens Restyle with a preview; nothing changes until you apply." };
                    restyle.AddToClassList("looksmith-chip");
                    card.Add(restyle);
                }
            }
            return card;
        }

        // The Look Advisor on the scene card: a look is a starting point, so the card says how it fits this scene and
        // opens the look's "Fit to this scene" section (checked on demand: it renders a small frame).
        List<LooksmithAdvice> m_FitAdvice;
        VolumeProfile m_FitFor;

        VisualElement FitRow(VolumeProfile profile)
        {
            var row = Row();
            row.name = "fit-row";
            if (m_FitFor != profile)
                m_FitAdvice = null;
            string text = m_FitAdvice == null ? "Fit to this scene: not checked yet"
                : m_FitAdvice.Count == 0 ? "Fit to this scene: the look fits as it is"
                : $"Fit to this scene: {m_FitAdvice.Count(a => a.level == LooksmithAdviceLevel.Recommended)} recommended, {m_FitAdvice.Count(a => a.level == LooksmithAdviceLevel.Tip)} tip(s)";
            row.Add(new Label(text) { name = "fit-summary", style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
            row.Add(new Button(() =>
            {
                var cam = LooksmithLookFit.ReferenceCamera();
                if (cam != null)
                {
                    m_FitAdvice = LooksmithLookAdvisor.Advise(profile, LooksmithLookFit.Read(cam));
                    m_FitFor = profile;
                }
                Rebuild();
            }) { text = m_FitAdvice == null ? "Check" : "Check again", name = "fit-check",
                tooltip = "The Look Advisor reads the scene (light, fog, sky, a small render from the main camera) and suggests tweaks so the look reads as intended here." });
            if (m_FitAdvice != null && m_FitAdvice.Count > 0)
                row.Add(new Button(() =>
                {
                    Selection.activeObject = profile;
                    EditorGUIUtility.PingObject(profile);
                }) { text = "Show…", name = "fit-show", tooltip = "Selects the Look Profile: its inspector lists each suggestion, why, and Apply." });
            return row;
        }

        /// <summary>Styled, Not styled, or Partially restyled (a Look Volume, but materials Looksmith could convert remain).</summary>
        public static string SceneStatus(Scene scene, out int unstyledMaterials)
        {
            unstyledMaterials = 0;
            bool hasVolume = LooksmithFind.All<LooksmithLookVolume>(FindObjectsInactive.Include)
                .Any(v => v.gameObject.scene == scene);
            if (!hasVolume)
                return "Not styled";
            if (scene == SceneManager.GetActiveScene())
                unstyledMaterials = LooksmithRestyle.Plan(RestyleScope.Scene).ConvertibleCount;
            return unstyledMaterials > 0 ? "Partially restyled" : "Styled";
        }

        VisualElement DoctorCard()
        {
            var card = Card("Setup Doctor", "card-doctor");
            m_Findings ??= LooksmithScan.Run();
            var problems = m_Findings.Where(f => f.severity == ScanSeverity.Blocking || f.severity == ScanSeverity.Recommended).ToList();
            if (problems.Count == 0)
                card.Add(Text("Everything Looksmith needs is in place."));
            foreach (var f in problems)
            {
                var row = Row();
                row.name = "doctor-" + f.id;
                var sev = new Label(SeverityName(f.severity));
                sev.AddToClassList("looksmith-sev");
                sev.AddToClassList("looksmith-sev--" + f.severity.ToString().ToLowerInvariant());
                row.Add(sev);
                row.Add(new Label(f.title) { tooltip = f.detail, style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
                if (f.fix != null)
                {
                    var finding = f;
                    var fix = new Button(() => Fix(finding)) { text = "Fix…", name = "fix", tooltip = $"Previews the change ({f.writes}), backs it up, then applies it. Hub > Changes reverts it." };
                    fix.SetEnabled(!LooksmithPlayMode.active);
                    row.Add(fix);
                }
                card.Add(row);
            }
            card.Add(new Button(() => { m_Findings = LooksmithScan.Run(); Rebuild(); }) { text = "Check again", name = "doctor-recheck" });
            return card;
        }

        /// <summary>The Setup Doctor's one-click fix: one change, previewed, backed up and logged like every other write.</summary>
        public static bool Fix(ScanFinding finding)
        {
            if (finding?.fix == null || ChangesetRunner.isRunning || LooksmithPlayMode.active)
                return false;
            var change = finding.fix;
            change.enabled = true;
            var writes = string.Join("\n", change.Writes().Take(6));
            if (!Confirm("Looksmith: " + change.title,
                    $"{change.title}\n\n{(string.IsNullOrEmpty(change.detail) ? finding.detail : change.detail)}\n\nWrites:\n{writes}\n\nA backup is made first; Hub > Changes reverts it." +
                    (VcsGuard.gitDetected ? "\nThis project uses git: commit first if you'd like your own restore point too." : ""),
                    "Back up and apply", "Cancel"))
                return false;
            return ApplyWithPrompts(new Changeset("Fix: " + finding.title, new[] { change }), backupFirst: true);
        }

        // "It doesn't look right": the usual causes, checked on demand (it renders a small frame), each with its fix.
        List<LookDoctorItem> m_LookDoctor;

        VisualElement LookDoctorCard()
        {
            var card = Card("Doesn't look right?", "card-look-doctor");
            if (m_LookDoctor == null)
                card.Add(Muted("Checks the usual reasons a look misses here: what isn't restyled, too dark or too bright, two fogs, camera settings, a low render scale, grain under the styles, water Looksmith can't see. Nothing changes until you click a fix."));
            else if (m_LookDoctor.Count == 0)
                card.Add(Text("Nothing looks off: the look fits this scene as it is."));
            else
                foreach (var item in m_LookDoctor)
                {
                    var row = Row();
                    row.name = "look-doctor-" + item.id;
                    var col = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
                    col.Add(Text(item.title));
                    col.Add(Muted(item.why));
                    row.Add(col);
                    if (item.action != null)
                    {
                        var run = item.run;
                        var b = new Button(() => { run?.Invoke(); m_LookDoctor = null; Rebuild(); }) { text = item.action, name = "look-doctor-fix" };
                        b.SetEnabled(!LooksmithPlayMode.active || item.id == "bypass");
                        row.Add(b);
                    }
                    card.Add(row);
                }
            card.Add(new Button(() => { m_LookDoctor = LooksmithLookDoctor.Diagnose(SceneManager.GetActiveScene()); Rebuild(); })
                { text = m_LookDoctor == null ? "Check what's off" : "Check again", name = "look-doctor-run" });
            return card;
        }

        /// <summary>Looksmith's other pieces, one click each (its GameObject > Looksmith menu item: an ordinary, undoable scene edit).</summary>
        public static readonly (string title, string what, string menu, string type, bool pro, bool many)[] Features =
        {
            ("Sky and time of day", "A sky with sun, moon, stars and clouds that lights the scene by the time of day.", "GameObject/Looksmith/Sky", "LooksmithSky", false, false),
            ("Wind", "One wind for everything that sways: grass, plants and trees, and particles through a Wind Zone.", "GameObject/Looksmith/Wind", "LooksmithWind", false, false),
            ("Atmosphere", "Height fog, ground mist on the water, sun shafts between trees and drifting cloud shadows.", "GameObject/Looksmith/Atmosphere", "LooksmithAtmosphere", true, false),
            ("Water", "A lake, river or sea lit by the look: waves, foam, reflections, caustics.", "GameObject/Looksmith/Water Surface", "LooksmithWaterSurface", true, true),
            ("Grass", "A field of grass that sways with the wind and parts where things walk.", "GameObject/Looksmith/Grass Field", "LooksmithGrassField", true, true),
            ("Look Zone", "A different look inside an area: a cave, an interior, a dream.", "GameObject/Looksmith/Look Zone", "LooksmithLookZone", true, true),
        };

        /// <summary>Runs one of Looksmith's GameObject > Looksmith menu items (the Hub's "Add to your scene").</summary>
        public static bool AddFeature(string menu) => EditorApplication.ExecuteMenuItem(menu);

        VisualElement FeaturesCard()
        {
            var card = Card("Add to your scene", "card-features");
            card.Add(Muted("Looksmith's other pieces, one click each (an ordinary scene edit: Undo takes it back)."));
            var present = new HashSet<string>(LooksmithFind.All<MonoBehaviour>(FindObjectsInactive.Include)
                .Where(m => m != null).Select(m => m.GetType().Name));
            foreach (var f in Features)
            {
                if (f.pro && !LooksmithEdition.isPro)
                    continue;   // Lite lists what it has (the Pro tab shows the rest)
                var row = Row();
                row.name = "feature-" + f.title;
                var col = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
                col.Add(Text(f.title));
                col.Add(Muted(f.what));
                row.Add(col);
                bool has = present.Contains(f.type);
                var menu = f.menu;
                var add = new Button(() => { AddFeature(menu); Rebuild(); }) { text = has && !f.many ? "In the scene" : "Add", name = "add-" + f.title,
                    tooltip = "Adds it to the open scene (Undo takes it back)." };
                add.SetEnabled(!(has && !f.many) && !LooksmithPlayMode.active);
                row.Add(add);
                card.Add(row);
            }
            var water = MarkWaterChange.Unmarked(SceneManager.GetActiveScene());
            if (water.Count > 0)
            {
                var row = Row();
                row.name = "feature-mark-water";
                var col = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
                col.Add(Text($"Your water ({water.Count})"));
                col.Add(Muted("Marks it so lines, art-style marks and fog treat it as a surface instead of the bed under it."));
                row.Add(col);
                row.Add(new Button(() =>
                {
                    var finding = LooksmithScan.Run().FirstOrDefault(x => x.id == "water-unmarked");
                    if (finding != null)
                        Fix(finding);
                    Rebuild();
                }) { text = "Mark…", name = "mark-water" });
                card.Add(row);
            }
            return card;
        }

        VisualElement ChangesCard()
        {
            var card = Card("Changes", "card-changes");
            var list = LooksmithBackups.List();
            if (list.Count == 0)
                card.Add(Text("Looksmith hasn't changed anything in this project."));
            else
            {
                var last = list[0];
                card.Add(Text($"Last change: {last.label}  ·  {LocalTime(last.createdUtc)}  ·  {last.status}"));
                card.Add(Muted($"{list.Count} backup(s), {EditorUtility.FormatBytes(list.Sum(m => m.SizeOnDisk()))} in {LooksmithBackups.FolderName}/ (outside Assets)."));
            }
            card.Add(new Button(() => { m_Tab = Tab.Changes; Rebuild(); }) { text = "Revert…", name = "changes-revert", tooltip = "Every backup, with Revert and conflict choices." });
            return card;
        }

        VisualElement HelpCard()
        {
            var card = Card("Help", "card-help");
            var row = Row();
            row.Add(PdfButton());
            row.Add(new Button(() => { m_Tab = Tab.Help; Rebuild(); }) { text = "Known limitations", name = "help-limits" });
            row.Add(new Button(() => { m_Tab = Tab.Help; Rebuild(); }) { text = "This project", name = "help-project", tooltip = "What the scan found about this project." });
            card.Add(row);
            card.Add(Muted("Support: Pale Fern Studio · paleferndev@gmail.com"));
            return card;
        }

        // ------------------------------------------------------------------ Changes

        void BuildChanges(VisualElement page)
        {
            page.Add(H1("Changes"));
            var list = LooksmithBackups.List();
            page.Add(Muted($"{list.Count} backup(s), {EditorUtility.FormatBytes(list.Sum(m => m.SizeOnDisk()))} in {LooksmithBackups.FolderName}/ (outside Assets)."));
            if (list.Count == 0)
                page.Add(Text("Looksmith hasn't changed anything in this project."));
            foreach (var m in list)
                page.Add(SnapshotCard(m));

            var retention = LooksmithProjectSettings.instance.backupRetention;
            var old = LooksmithBackups.BeyondRetention(retention);
            var del = new Button(() =>
            {
                if (Confirm("Delete old backups?", $"Permanently delete {old.Count} backup(s) beyond the newest {retention}? You can't revert those changes afterwards.", "Delete", "Cancel"))
                {
                    LooksmithBackups.Delete(old);
                    Rebuild();
                }
            }) { text = $"Delete old backups ({old.Count})", name = "delete-old", tooltip = "Backups beyond Project Settings > Looksmith > Backups to keep (asks first)." };
            del.SetEnabled(old.Count > 0);
            page.Add(del);

            page.Add(H2("Uninstall"));
            page.Add(Muted("1. Revert every backup (or keep your materials and convert them back to URP Lit with Restyle). 2. Remove Looksmith scene roots. 3. Delete the Looksmith folder yourself; the wizard never deletes its own package."));
            var row = Row();
            row.Add(new Button(() =>
            {
                if (!Confirm("Revert everything?", "Revert every Looksmith backup, newest first. Files you changed after Looksmith are kept.", "Revert all", "Cancel"))
                    return;
                var notes = LooksmithSetup.RevertAll();
                EditorUtility.DisplayDialog("Looksmith", notes.Count == 0 ? "Everything is reverted." : string.Join("\n", notes), "OK");
                Rebuild();
            }) { text = "1. Revert all", name = "revert-all", tooltip = "Reverts every backup, newest first; files you changed since are kept." });
            row.Add(new Button(() =>
            {
                int n = LooksmithSetup.RemoveSceneRoots();
                EditorUtility.DisplayDialog("Looksmith", $"Removed {n} Looksmith object(s) from the open scenes (Undo restores them).", "OK");
            }) { text = "2. Remove scene roots", name = "remove-roots", tooltip = "Removes the objects Looksmith added to the open scenes (undoable)." });
            page.Add(row);
            page.Add(DocLink("Backups and Revert", LooksmithDocs.Backups));
        }

        VisualElement SnapshotCard(SnapshotManifest m)
        {
            var card = new VisualElement { name = "snapshot-" + m.id };
            card.AddToClassList("looksmith-card");
            card.Add(Text($"{m.label}  ·  {LocalTime(m.createdUtc)}  ·  {m.status}"));
            int remaps = m.ledger.Count(l => l.kind == LedgerEntry.Remap);
            card.Add(Muted($"{m.entries.Count} file(s), {m.ledger.Count - remaps} scene object(s){(remaps > 0 ? $", {remaps} material remap(s)" : "")}, {EditorUtility.FormatBytes(m.SizeOnDisk())}" + (string.IsNullOrEmpty(m.error) ? "" : "  ·  " + m.error)));
            if (m.status == SnapshotManifest.Applied || m.status == SnapshotManifest.Failed || m.status == SnapshotManifest.PartiallyReverted)
            {
                var plan = Revert.Plan(m);
                var conflicts = plan.conflicts.ToList();
                foreach (var c in conflicts)
                {
                    var row = Row();
                    row.AddToClassList("looksmith-conflict");
                    row.Add(Text($"{c.entry.path}: {c.reason}"));
                    var choice = new EnumField(c.resolution) { tooltip = "Keep yours / restore the backup / skip", name = "conflict-resolution" };
                    choice.RegisterValueChangedCallback(e => c.resolution = (ConflictResolution)e.newValue);
                    row.Add(choice);
                    card.Add(row);
                }
                var revert = new Button(() =>
                {
                    if (Revert.Preflight().Count > 0)
                    {
                        if (!Confirm("Save first?", "Revert only touches saved work. Save the open scenes and project now?", "Save and revert", "Cancel"))
                            return;
                        EditorSceneManager.SaveOpenScenes();
                        AssetDatabase.SaveAssets();
                    }
                    var result = Revert.Execute(plan);
                    Debug.Log($"Looksmith: reverted '{m.label}': {result}");
                    Rebuild();
                }) { text = conflicts.Count > 0 ? $"Revert… ({conflicts.Count} conflict(s))" : "Revert…", name = "revert",
                    tooltip = LooksmithPlayMode.active ? LooksmithPlayMode.WritesDisabled : "Restores the backed-up files (checked by SHA-256) and removes what this change added." };
                revert.SetEnabled(!LooksmithPlayMode.active);
                card.Add(revert);
            }
            return card;
        }

        // ------------------------------------------------------------------ Help

        void BuildHelp(VisualElement page)
        {
            page.Add(H1("Help"));
            page.Add(H2("Documentation"));
            page.Add(Text("The manual ships in this package as a PDF, and every ? button in Looksmith opens it: its contents and bookmarks take you to each section. Every field in Looksmith's inspectors has a tooltip."));
            var docs = Row();
            docs.Add(PdfButton());
            page.Add(docs);
            page.Add(H2("Doesn't look right?"));
            page.Add(LookDoctorCard());
            page.Add(H2("Known limitations"));
            foreach (var line in new[]
            {
                "Looksmith objects are lit in a forward pass and Looksmith is tuned on Forward+. Deferred and Deferred+ work and looked the same in the scenes tested; with many point lights the lighting can differ slightly.",
                "Looksmith surfaces take baked light from lightmaps and light probes, but a bake doesn't see their colours yet (URP's Unlit target has no Meta pass): bake before you restyle.",
                "The Look's haze is applied by Looksmith materials; materials Looksmith didn't restyle keep Unity's fog instead (Looksmith surfaces take that too). The Looksmith Atmosphere's fog covers everything.",
                "Ambient uses SH (light probes / ambient probe); Adaptive Probe Volumes are not sampled yet.",
                "Camera stacks: depth- and normal-based passes run on the base camera; overlay cameras keep their own content.",
                "The before and after split and the debug views work in the Game and Scene views. The Pixel Camera's low-resolution image isn't split.",
            })
                page.Add(Muted("• " + line));
            page.Add(DocLink("All known limitations", LooksmithDocs.KnownLimitations));
            page.Add(H2("This project"));
            var findings = m_Findings ?? LooksmithScan.Run();
            foreach (var f in findings.Where(f => f.severity == ScanSeverity.Ready || f.severity == ScanSeverity.Info))
                page.Add(Muted("• " + f.title));
            page.Add(H2("Support"));
            page.Add(Text("Pale Fern Studio · paleferndev@gmail.com"));
        }

        // ------------------------------------------------------------------ helpers

        static string SeverityName(ScanSeverity s) => s switch
        {
            ScanSeverity.Blocking => "Blocking",
            ScanSeverity.Recommended => "Recommended",
            ScanSeverity.HeadsUp => "Heads-up",
            ScanSeverity.Info => "Info",
            _ => "Ready",
        };

        static string GroupName(ChangeGroup g) => g switch
        {
            ChangeGroup.Project => "Project changes",
            ChangeGroup.Scene => "Scene objects to add",
            ChangeGroup.Assets => "Assets to create",
            _ => "Materials to restyle",
        };

        static string LocalTime(string utc) => DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("g") : utc;

        static Label H1(string t) { var l = new Label(t); l.AddToClassList("looksmith-h1"); return l; }
        static Label H2(string t) { var l = new Label(t); l.AddToClassList("looksmith-h2"); return l; }
        static Label Text(string t) { var l = new Label(t); l.AddToClassList("looksmith-text"); return l; }
        static Label Muted(string t) { var l = new Label(t); l.AddToClassList("looksmith-muted"); return l; }
        static VisualElement Row() { var r = new VisualElement(); r.AddToClassList("looksmith-row"); return r; }

        static VisualElement Card(string title, string name)
        {
            var card = new VisualElement { name = name };
            card.AddToClassList("looksmith-card");
            card.Add(H2(title));
            return card;
        }

        /// <summary>The manual that ships in the package (Documentation/Looksmith Manual.pdf), opened offline.</summary>
        static Button PdfButton()
        {
            var b = new Button(OpenOfflineManual) { text = "Manual (PDF)", name = "manual-pdf", tooltip = "The manual in this package, in your PDF viewer (works offline)." };
            b.AddToClassList("looksmith-link");
            return b;
        }

        /// <summary>Opens the packaged PDF manual.</summary>
        internal static void OpenOfflineManual() => LooksmithLinks.Open(LooksmithDocs.Home);

        static Button LinkButton(string text, string url)
        {
            var b = new Button(() => LooksmithLinks.Open(url)) { text = text, tooltip = LooksmithLinks.Describe(url), name = "link" };
            b.AddToClassList("looksmith-link");
            return b;
        }

        /// <summary>A "Read more" link to a manual page.</summary>
        public static VisualElement DocLink(string text, string url)
        {
            var b = LinkButton("? " + text, url);
            b.style.alignSelf = Align.FlexStart;
            return b;
        }

        /// <summary>Test hook: puts the Hub in a state without clicking through it.</summary>
        public void SetStateForTests(Tab tab, Step step, List<ScanFinding> findings)
        {
            m_Tab = tab;
            m_Step = step;
            m_Findings = findings;
            m_Pending = null;
            Rebuild();
        }
    }

    /// <summary>Light health checks for the Hub badge: things that matter if they change after setup.</summary>
    public static class LooksmithHealth
    {
        public static List<string> Issues()
        {
            var issues = new List<string>();
            if (LooksmithRuntime.Bypass)
                return issues;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp)
            {
                issues.Add("URP is not the active render pipeline.");
                return issues;
            }
            if (PlayerSettings.colorSpace == ColorSpace.Gamma)
                issues.Add("The project uses Gamma colour space (Linear is recommended).");
            var renderers = new SerializedObject(urp).FindProperty("m_RendererDataList");
            for (int i = 0; renderers != null && i < renderers.arraySize; i++)
                if (renderers.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data &&
                    (data.renderingMode == RenderingMode.Deferred || data.renderingMode == RenderingMode.DeferredPlus))
                    issues.Add($"{data.name} uses the {data.renderingMode} path: Looksmith works there, but it is tuned on Forward+ (many point lights can light slightly differently).");
            foreach (var v in LooksmithFind.All<LooksmithLookVolume>())
            {
                var volume = v.GetComponent<Volume>();
                if (volume == null || volume.sharedProfile == null || !volume.sharedProfile.TryGet<LooksmithLook>(out var look) || !look.IsActive())
                    issues.Add($"The Look Volume in {v.gameObject.scene.name} has no active Look.");
            }
            return issues;
        }

        public static string Label(List<string> issues)
        {
            if (LooksmithRuntime.Bypass)
                return "Bypass on";
            if (issues.Count > 0)
                return issues.Count == 1 ? "1 issue" : $"{issues.Count} issues";
            return LooksmithRuntime.ActiveVolumes.Count > 0 ? "Styled" : "Ready";
        }
    }
}
