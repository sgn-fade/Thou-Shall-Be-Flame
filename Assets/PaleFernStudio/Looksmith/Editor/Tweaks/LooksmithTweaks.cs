using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>One knob in the Tweaks panel: a setting on a component, an asset or a material, named for what it does.</summary>
    public sealed class TweakKnob
    {
        public string label;
        public string hint;
        public Object target;
        /// <summary>A serialized property path, or a material property name when the target is a Material.</summary>
        public string property;
        public float min, max = 1f;
        /// <summary>Shown under the knob: what drives it, when something else does.</summary>
        public string note;
        /// <summary>Whole numbers only (a popup when <see cref="options"/> is set).</summary>
        public bool whole;
        public string[] options;
        /// <summary>A pick from named values (a sky preset, say) instead of a slider.</summary>
        public List<KeyValuePair<string, object>> choices;
    }

    /// <summary>A part of the scene the Tweaks panel tunes: the Look, the sky, the air, the water...</summary>
    public sealed class TweakModule
    {
        public string name;
        public int order;
        /// <summary>One line on what it is.</summary>
        public string summary;
        /// <summary>True when the open scenes have it.</summary>
        public Func<bool> present;
        /// <summary>The few knobs that matter most, named for what they do.</summary>
        public Func<List<TweakKnob>> essentials;
        /// <summary>The objects whose every setting shows under Advanced, and which Revert to default resets.</summary>
        public Func<List<Object>> objects;
        /// <summary>Adds it to the scene (a GameObject menu item); null when it can't be added from the panel.</summary>
        public string addMenu;
        /// <summary>Suggestions for what the camera sees now (the Look Advisor), worked out when asked for; null: none.</summary>
        public Func<List<TweakSuggestion>> suggestions;
    }

    /// <summary>A suggestion for the view: what, why, and how to carry it out (as drafts).</summary>
    public sealed class TweakSuggestion
    {
        public string title, why;
        public bool recommended;
        public Action apply;
    }

    /// <summary>A draft: a setting changed in the panel and not saved yet.</summary>
    public sealed class TweakChange
    {
        public string module, label, property;
        public string targetId;
        public Object target;
        public object before, after;
        /// <summary>Saved in Play Mode: written when Play Mode ends.</summary>
        public bool saved;
    }

    /// <summary>
    /// The Tweaks model (plan §4.6). Every change made in the panel is a draft on the live object (and on any runtime copy
    /// standing in for it), listed with its old and new value. Save keeps them: one backup, one entry in Hub > Changes
    /// (P3), and the files written. A shipped asset is never written: Save makes the project's own copy and points the
    /// open scenes at it (unless "Save into shipped assets" is on). In Play Mode, Save is written when Play Mode ends, and
    /// leaving without Save puts every draft back. Revert this session drops the drafts, Revert to default puts a module
    /// back to its defaults, and Unity's undo works on all of it (the list follows).
    /// </summary>
    [InitializeOnLoad]
    public static class LooksmithTweaks
    {
        public const string ShippedRoot = "Assets/PaleFernStudio/Looksmith/";
        const string StateKey = "Looksmith.Tweaks.State";

        static readonly List<TweakModule> s_Modules = new List<TweakModule>();
        static readonly List<TweakChange> s_Changes = new List<TweakChange>();
        static readonly Dictionary<Type, Func<Object, Object>> s_Defaults = new Dictionary<Type, Func<Object, Object>>();
        static readonly Dictionary<Type, Action<Object>> s_AfterChange = new Dictionary<Type, Action<Object>>();

        /// <summary>Raised after any draft changes (panels repaint).</summary>
        public static event Action Changed;

        public static IReadOnlyList<TweakModule> modules => s_Modules;
        public static IReadOnlyList<TweakChange> changes => s_Changes;
        /// <summary>How many settings have unsaved changes.</summary>
        public static int unsavedCount => CountSettings(s_Changes.Where(c => !c.saved));

        static LooksmithTweaks()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload -= Persist;
            AssemblyReloadEvents.beforeAssemblyReload += Persist;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            RegisterDefaults(typeof(MonoBehaviour), ComponentDefaults);
            Restore();
        }

        // ------------------------------------------------------------------ registry

        public static void Register(TweakModule module)
        {
            s_Modules.RemoveAll(m => m.name == module.name);
            s_Modules.Add(module);
            s_Modules.Sort((a, b) => a.order.CompareTo(b.order));
        }

        /// <summary>
        /// How to make an object holding a type's defaults (a temporary object the caller destroys), for Revert to default.
        /// The most specific registered type wins.
        /// </summary>
        public static void RegisterDefaults(Type type, Func<Object, Object> make)
        {
            s_Defaults[type] = make;
            s_DefaultCache.Clear();
        }

        /// <summary>Runs after a draft changes an object of this type (a sky re-reads its preset, a grass field rebuilds).</summary>
        public static void RegisterAfterChange(Type type, Action<Object> after) => s_AfterChange[type] = after;

        // ------------------------------------------------------------------ drafts

        /// <summary>
        /// Every setting changed this session and the value it had before. It stays after a change goes back, so an undo or
        /// redo that brings the change back puts it in the list again.
        /// </summary>
        static readonly Dictionary<string, TweakChange> s_Known = new Dictionary<string, TweakChange>();
        static int s_UndoCount;

        /// <summary>Goes up on every undo or redo (the overlay uses it to tell whether Unity already undid).</summary>
        internal static int undoCount => s_UndoCount;

        static void Remember(TweakChange c)
        {
            var key = c.targetId + "|" + c.property;
            if (!s_Known.ContainsKey(key))
                s_Known[key] = new TweakChange { module = c.module, label = c.label, property = c.property, targetId = c.targetId, target = c.target, before = c.before };
        }

        static void RebuildKnown()
        {
            s_Known.Clear();
            foreach (var c in s_Changes)
                Remember(c);
        }

        /// <summary>The value on screen (the stand-in's, when a runtime copy stands in for the asset).</summary>
        public static object Get(Object target, string property) => TweakValue.Read(target, property);

        /// <summary>The change made to a setting this session (saved in Play Mode or not), or null.</summary>
        public static TweakChange DraftOf(Object target, string property)
        {
            if (target == null)
                return null;
            var id = TweakTargetId.Of(LooksmithTweaksHooks.SourceOf(target));
            return s_Changes.FirstOrDefault(c => c.targetId == id && c.property == property);
        }

        /// <summary>Changes a setting as a draft: on the object (the asset a runtime copy stands in for) and its stand-ins.</summary>
        public static void Set(Object target, string property, object value, string module, string label) =>
            Set(target, property, value, module, label, true);

        static void Set(Object target, string property, object value, string module, string label, bool overrideOn)
        {
            if (target == null)
                return;
            var real = LooksmithTweaksHooks.SourceOf(target);
            // a Volume parameter only counts when it's overridden, so changing one turns its override on
            if (overrideOn && real is VolumeComponent && property.EndsWith(".m_Value"))
            {
                var overrideProp = property.Substring(0, property.Length - ".m_Value".Length) + ".m_OverrideState";
                if (!(TweakValue.Read(real, overrideProp) is bool on && on))
                    Set(target, overrideProp, true, module, label + " (on)", false);
            }
            var id = TweakTargetId.Of(real);
            var change = s_Changes.FirstOrDefault(c => c.targetId == id && c.property == property);
            if (change == null)
            {
                change = new TweakChange
                {
                    module = module, label = label, property = property, targetId = id, target = real,
                    before = TweakValue.Read(real, property),
                };
                s_Changes.Add(change);
                Remember(change);
            }
            RecordStep(real, property, TweakValue.Read(real, property), value, module, label);
            change.target = real;
            change.after = value;
            change.saved = false;
            Write(real, property, value);
            if (TweakValue.Equal(change.before, change.after))
                s_Changes.Remove(change);
            Changed?.Invoke();
        }

        /// <summary>Records a change something else already made (a property field under Advanced): before is its old value.</summary>
        public static void Record(Object target, string property, object before, string module, string label)
        {
            var real = LooksmithTweaksHooks.SourceOf(target);
            var after = TweakValue.Read(target, property);
            if (real != target)
                Write(real, property, after);
            var id = TweakTargetId.Of(real);
            var change = s_Changes.FirstOrDefault(c => c.targetId == id && c.property == property);
            if (change == null)
            {
                change = new TweakChange { module = module, label = label, property = property, targetId = id, target = real, before = before };
                s_Changes.Add(change);
                Remember(change);
            }
            change.after = after;
            change.saved = false;
            AfterChange(real);
            if (TweakValue.Equal(change.before, change.after))
                s_Changes.Remove(change);
            Changed?.Invoke();
        }

        static void Write(Object real, string property, object value, bool undo = true)
        {
            undo &= !Application.isPlaying;
            TweakValue.Write(real, property, value, undo);
            foreach (var standIn in LooksmithTweaksHooks.StandInsOf(real).ToList())
                TweakValue.Write(standIn, property, value, undo);
            AfterChange(real);
        }

        static void AfterChange(Object o)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
                if (s_AfterChange.TryGetValue(t, out var after))
                {
                    after(o);
                    return;
                }
        }

        /// <summary>After Unity's undo or redo, the list follows what's on screen again.</summary>
        static void OnUndoRedo()
        {
            s_UndoCount++;
            bool any = false;
            foreach (var k in s_Known.Values.ToList())
            {
                if (k.target == null)
                    continue;
                var now = TweakValue.Read(k.target, k.property);
                if (now == null)
                    continue;
                var change = s_Changes.FirstOrDefault(c => c.targetId == k.targetId && c.property == k.property);
                if (TweakValue.Equal(now, change != null ? change.after : k.before))
                    continue;
                any = true;
                if (TweakValue.Equal(now, k.before))
                    s_Changes.Remove(change);
                else if (change == null)
                    s_Changes.Add(new TweakChange
                    {
                        module = k.module, label = k.label, property = k.property, targetId = k.targetId, target = k.target,
                        before = k.before, after = now,
                    });
                else
                {
                    change.after = now;
                    change.saved = false;
                }
                foreach (var standIn in LooksmithTweaksHooks.StandInsOf(k.target).ToList())
                    TweakValue.Write(standIn, k.property, now, false);
                AfterChange(k.target);
            }
            if (any)
                Changed?.Invoke();
        }

        static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        static void EndUndo(int group)
        {
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
        }

        // ------------------------------------------------------------------ undo in Play Mode

        /// <summary>One undo step in Play Mode: what one drag, click or reset changed, with the values before and after.</summary>
        sealed class Step
        {
            public readonly List<(Object target, string property, object before, object after, string module, string label)> edits =
                new List<(Object, string, object, object, string, string)>();
        }

        // In Play Mode Tweaks keeps its own undo: Unity's history there also holds older changes to the same assets (from
        // editing and from earlier Play sessions), and replaying those would change them behind the panel's back.
        static readonly List<Step> s_UndoSteps = new List<Step>(), s_RedoSteps = new List<Step>();
        static Step s_OpenStep;
        static bool s_Replaying;

        /// <summary>Ends the current undo step: the next change starts a new one (each drag or click is one step).</summary>
        public static void EndStep() => s_OpenStep = null;

        public static bool canUndo => s_UndoSteps.Count > 0;
        public static bool canRedo => s_RedoSteps.Count > 0;

        static void RecordStep(Object real, string property, object before, object after, string module, string label)
        {
            if (!Application.isPlaying || s_Replaying)
                return;
            if (s_OpenStep == null)
            {
                s_OpenStep = new Step();
                s_UndoSteps.Add(s_OpenStep);
                s_RedoSteps.Clear();
            }
            int i = s_OpenStep.edits.FindIndex(e => e.target == real && e.property == property);
            if (i >= 0)
            {
                var e = s_OpenStep.edits[i];
                s_OpenStep.edits[i] = (e.target, e.property, e.before, after, e.module, e.label);
            }
            else
                s_OpenStep.edits.Add((real, property, before, after, module, label));
        }

        /// <summary>Undoes the last change made while playing. False when there's nothing to undo.</summary>
        public static bool UndoStep() => Replay(s_UndoSteps, s_RedoSteps, true);

        /// <summary>Redoes the last change undone while playing. False when there's nothing to redo.</summary>
        public static bool RedoStep() => Replay(s_RedoSteps, s_UndoSteps, false);

        static bool Replay(List<Step> from, List<Step> to, bool back)
        {
            EndStep();
            if (from.Count == 0)
                return false;
            var step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            s_Replaying = true;
            try
            {
                var edits = back ? Enumerable.Reverse(step.edits) : step.edits;
                foreach (var e in edits)
                    if (e.target != null)
                        Set(e.target, e.property, back ? e.before : e.after, e.module, e.label, false);
            }
            finally
            {
                s_Replaying = false;
            }
            to.Add(step);
            return true;
        }

        /// <summary>
        /// Clears Unity's undo history for Looksmith's assets as Play Mode starts (Tweaks has its own undo while playing), so
        /// Ctrl+Z can't put old values back on them behind the panel's back.
        /// </summary>
        static void ClearAssetUndo()
        {
            try
            {
                var objects = new HashSet<Object>();
                foreach (var k in s_Known.Values)
                    if (k.target != null)
                        objects.Add(k.target);
                foreach (var m in s_Modules)
                    if (m.present != null && m.present())
                        foreach (var o in m.objects?.Invoke() ?? new List<Object>())
                            if (o != null)
                                objects.Add(LooksmithTweaksHooks.SourceOf(o));
                foreach (var p in Resources.FindObjectsOfTypeAll<VolumeProfile>())
                    if (IsShipped(AssetDatabase.GetAssetPath(p)))
                    {
                        objects.Add(p);
                        foreach (var c in p.components)
                            if (c != null)
                                objects.Add(c);
                    }
                foreach (var p in Resources.FindObjectsOfTypeAll<LooksmithSkyPreset>())
                    if (IsShipped(AssetDatabase.GetAssetPath(p)))
                        objects.Add(p);
                foreach (var o in objects)
                    if (o != null && EditorUtility.IsPersistent(o))
                        Undo.ClearUndo(o);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Puts one setting back to how it was before this session.</summary>
        public static void Revert(TweakChange change)
        {
            EndStep();
            if (change.target != null)
                Set(change.target, change.property, change.before, change.module, change.label, false);
            s_Changes.Remove(change);
            EndStep();
            Changed?.Invoke();
        }

        /// <summary>Puts every unsaved change back: things are as they were when the session started.</summary>
        public static void RevertSession()
        {
            int group = BeginUndo("Looksmith: revert this session");
            EndStep();
            foreach (var c in s_Changes.Where(c => !c.saved).Reverse().ToList())
                if (c.target != null)
                    Set(c.target, c.property, c.before, c.module, c.label, false);
            s_Changes.RemoveAll(c => !c.saved);
            EndStep();
            EndUndo(group);
            Changed?.Invoke();
        }

        /// <summary>
        /// Puts every setting of a module back to its default (see <see cref="DefaultOf"/>), as drafts that Save keeps.
        /// References (which preset, which material) are never touched. Returns how many settings it changes; with
        /// <paramref name="apply"/> false it only counts them.
        /// </summary>
        public static int RevertToDefault(TweakModule module, bool apply = true)
        {
            s_DefaultCache.Clear();
            var changes = new List<(Object target, string property, object value)>();
            var dropped = new List<TweakChange>();
            var seen = new HashSet<Object>();
            foreach (var o in module.objects?.Invoke() ?? new List<Object>())
            {
                var real = LooksmithTweaksHooks.SourceOf(o);
                if (real == null || !seen.Add(real))
                    continue;
                var values = DefaultsOf(real);
                if (values == null)
                {
                    // its default is how it's saved: resetting it just drops the changes made to it
                    dropped.AddRange(s_Changes.Where(c => c.target == real));
                    continue;
                }
                foreach (var kv in values)
                {
                    var now = TweakValue.Read(real, kv.Key);
                    if (now == null || TweakValue.Equal(now, kv.Value))
                        continue;
                    // a Volume parameter that's off both ways doesn't count, whatever value it holds
                    if (kv.Key.EndsWith(".m_Value"))
                    {
                        var overrideProp = BaseOf(kv.Key) + ".m_OverrideState";
                        bool wantOn = values.TryGetValue(overrideProp, out var w) && w is bool wb && wb;
                        bool isOn = TweakValue.Read(real, overrideProp) is bool ib && ib;
                        if (!wantOn && !isOn)
                            continue;
                    }
                    changes.Add((real, kv.Key, kv.Value));
                }
            }
            int n = CountSettings(dropped.Select(c => c.targetId + "|" + BaseOf(c.property))
                .Concat(changes.Select(c => TweakTargetId.Of(c.target) + "|" + BaseOf(c.property))));
            if (!apply || n == 0)
                return n;
            int group = BeginUndo("Looksmith: revert " + module.name + " to default");
            EndStep();
            foreach (var c in dropped)
                if (c.target != null)
                    Set(c.target, c.property, c.before, c.module, c.label, false);
            s_Changes.RemoveAll(dropped.Contains);
            foreach (var (target, property, value) in changes)
                Set(target, property, value, module.name, LabelFor(property), false);
            EndStep();
            EndUndo(group);
            return n;
        }

        static readonly Dictionary<Object, Dictionary<string, object>> s_DefaultCache = new Dictionary<Object, Dictionary<string, object>>();

        /// <summary>
        /// The default of one setting: what its reset button and Revert to default put it back to.
        /// <list type="bullet">
        /// <item>Something Looksmith ships (a look, a sky preset, a material, or an object in Looksmith's demo scene), or a copy made
        /// while playing: how it's saved, so going back to default just drops your changes.</item>
        /// <item>Your copy of a shipped asset ("Dusk Sky (yours)"): the shipped one.</item>
        /// <item>Anything else: how a new one starts out (a look made from a shipped look: that look).</item>
        /// </list>
        /// </summary>
        public static object DefaultOf(Object target, string property)
        {
            if (target == null)
                return null;
            var real = LooksmithTweaksHooks.SourceOf(target);
            if (!s_DefaultCache.TryGetValue(real, out var values))
                s_DefaultCache[real] = values = DefaultsOf(real);
            if (values != null && values.TryGetValue(property, out var value))
                return value;
            return SavedValue(real, property);
        }

        /// <summary>A setting as it's saved: its value before this session's change, or now when it hasn't changed.</summary>
        static object SavedValue(Object real, string property) =>
            s_Changes.FirstOrDefault(c => c.target == real && c.property == property)?.before ?? TweakValue.Read(real, property);

        /// <summary>The default of every setting of an object, or null when its default is how it's saved.</summary>
        static Dictionary<string, object> DefaultsOf(Object real)
        {
            if (DefaultIsSaved(real))
                return null;
            var defaults = MakeDefaults(real, out bool temporary);
            if (defaults == null)
                return null;
            try
            {
                var values = new Dictionary<string, object>();
                foreach (var property in TweakValue.Properties(real))
                {
                    var value = SavedValue(defaults, property);
                    if (value != null)
                        values[property] = value;
                }
                return values;
            }
            finally
            {
                if (temporary)
                {
                    if (defaults is Component c)
                        Object.DestroyImmediate(c.gameObject);
                    else
                        Object.DestroyImmediate(defaults);
                }
            }
        }

        /// <summary>
        /// True when an object's default is how it's saved: an asset Looksmith ships, an object in a scene Looksmith ships (the
        /// demo), or an object made while playing that stands in for nothing Tweaks knows (it's never compared to a blank one).
        /// </summary>
        static bool DefaultIsSaved(Object o)
        {
            if (EditorUtility.IsPersistent(o))
                return IsShipped(AssetDatabase.GetAssetPath(o));
            var scene = o is Component c ? c.gameObject.scene : o is GameObject g ? g.scene : default;
            if (!scene.IsValid())
                return true;
            return !string.IsNullOrEmpty(scene.path) && scene.path.StartsWith(ShippedRoot);
        }

        /// <summary>A setting's name without its Volume parameter part ("bands" for "bands.m_Value").</summary>
        public static string BaseOf(string property)
        {
            foreach (var suffix in new[] { ".m_Value", ".m_OverrideState" })
                if (property.EndsWith(suffix))
                    return property.Substring(0, property.Length - suffix.Length);
            return property;
        }

        /// <summary>A setting's name for people ("Band Mix" for "bandMix.m_OverrideState").</summary>
        public static string LabelFor(string property) => ObjectNames.NicifyVariableName(BaseOf(property).Replace("m_", ""));

        /// <summary>How many settings a list of changes touches (a Volume parameter's value and its switch count once).</summary>
        public static int CountSettings(IEnumerable<TweakChange> changes) =>
            CountSettings(changes.Select(c => c.targetId + "|" + BaseOf(c.property)));

        static int CountSettings(IEnumerable<string> keys) => keys.Distinct().Count();

        const string Yours = " (yours)";

        /// <summary>The object holding an object's defaults: a shipped asset (not to be destroyed), or a temporary one.</summary>
        static Object MakeDefaults(Object real, out bool temporary)
        {
            temporary = false;
            var original = ShippedOriginal(real);
            if (original != null)
                return original;
            temporary = true;
            for (var t = real.GetType(); t != null; t = t.BaseType)
                if (s_Defaults.TryGetValue(t, out var make))
                    return make(real);
            return real is ScriptableObject ? ScriptableObject.CreateInstance(real.GetType()) : null;
        }

        /// <summary>For your copy of a shipped asset ("Dusk Sky (yours)"), the shipped one (or its matching part).</summary>
        static Object ShippedOriginal(Object real)
        {
            if (!EditorUtility.IsPersistent(real))
                return null;
            var path = AssetDatabase.GetAssetPath(real);
            if (IsShipped(path))
                return null;
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main == null || !main.name.EndsWith(Yours))
                return null;
            var name = main.name.Substring(0, main.name.Length - Yours.Length);
            foreach (var guid in AssetDatabase.FindAssets("t:" + main.GetType().Name, new[] { ShippedRoot.TrimEnd('/') }))
            {
                var candidate = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null || candidate.name != name || candidate.GetType() != main.GetType())
                    continue;
                if (real == main)
                    return candidate;
                if (real is VolumeComponent && candidate is VolumeProfile profile)
                    return profile.components.FirstOrDefault(c => c != null && c.GetType() == real.GetType());
                return null;
            }
            return null;
        }

        static Object ComponentDefaults(Object o)
        {
            var go = new GameObject("Looksmith Tweaks Defaults") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);         // no OnEnable: a Looksmith Atmosphere would make itself the active one
            return go.AddComponent(o.GetType());
        }

        /// <summary>"1 change", "3 changes".</summary>
        public static string Plural(int n, string noun) => n == 1 ? "1 " + noun : $"{n} {noun}s";

        // ------------------------------------------------------------------ save

        /// <summary>Keeps the drafts. In Play Mode they're written when Play Mode ends.</summary>
        public static string Save()
        {
            var unsaved = s_Changes.Where(c => !c.saved).ToList();
            if (unsaved.Count == 0)
                return "Nothing to save.";
            if (Application.isPlaying)
            {
                foreach (var c in unsaved)
                    c.saved = true;
                Changed?.Invoke();
                return $"Saved {Plural(unsaved.Count, "change")}. They'll be written to your project when you stop playing.";
            }
            s_DefaultCache.Clear();
            var result = Write(unsaved);
            s_Changes.RemoveAll(unsaved.Contains);
            // what's on disk now is the new starting point (an undo after this makes a new change)
            foreach (var c in unsaved)
                if (s_Known.TryGetValue(c.targetId + "|" + c.property, out var k))
                    k.before = c.after;
            Changed?.Invoke();
            return result;
        }

        static bool s_Writing;

        /// <summary>
        /// An asset file holding a draft: Unity's own Save Project leaves it alone (a draft is written by Save in Tweaks
        /// only, and a shipped asset never).
        /// </summary>
        internal static bool HoldsDraft(string path) =>
            !s_Writing && s_Changes.Any(c => c.target != null && EditorUtility.IsPersistent(c.target) && AssetDatabase.GetAssetPath(c.target) == path);

        /// <summary>Writes changes to disk: shipped assets as the project's own copies, a backup first (P3).</summary>
        static string Write(List<TweakChange> list)
        {
            s_Writing = true;
            try
            {
                return WriteNow(list);
            }
            finally
            {
                s_Writing = false;
            }
        }

        static string WriteNow(List<TweakChange> list)
        {
            list = list.Where(c => c.target != null).ToList();
            if (list.Count == 0)
                return "Nothing to save.";
            bool inPlace = LooksmithUserSettings.instance.editShippedInPlace;
            var assets = new List<Object>();
            var scenes = new HashSet<Scene>();
            var copies = new List<(string from, string to)>();
            foreach (var c in list)
            {
                if (EditorUtility.IsPersistent(c.target))
                {
                    var path = AssetDatabase.GetAssetPath(c.target);
                    if (!inPlace && IsShipped(path))
                    {
                        if (!copies.Any(p => p.from == path))
                            copies.Add((path, CopyPathFor(path)));
                    }
                    else if (!assets.Contains(c.target))
                        assets.Add(c.target);
                }
                else if (c.target is Component comp && comp.gameObject.scene.IsValid())
                    scenes.Add(comp.gameObject.scene);
                else if (c.target is GameObject go && go.scene.IsValid())
                    scenes.Add(go.scene);
            }

            // the files this save writes: backed up first, as every Looksmith write is
            var writes = assets.Select(AssetDatabase.GetAssetPath).Concat(copies.Select(p => p.to))
                .Concat(scenes.Where(s => !string.IsNullOrEmpty(s.path)).Select(s => s.path)).Distinct().ToList();
            var manifest = BeginBackup(writes, $"Tweaks: {string.Join(", ", list.Select(c => c.module).Distinct())}");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Looksmith: save tweaks");
            int group = Undo.GetCurrentGroup();
            foreach (var (from, to) in copies)
            {
                var made = CopyShipped(from, to, list.Where(c => AssetDatabase.GetAssetPath(c.target) == from).ToList());
                foreach (var s in made)
                    scenes.Add(s);
            }
            foreach (var a in assets)
            {
                VcsGuard.PrepareForWrite(new[] { AssetDatabase.GetAssetPath(a) });
                EditorUtility.SetDirty(a);
                AssetDatabase.SaveAssetIfDirty(a);
            }
            foreach (var s in scenes)
            {
                EditorSceneManager.MarkSceneDirty(s);
                if (!string.IsNullOrEmpty(s.path))
                    EditorSceneManager.SaveScene(s);
            }
            Undo.CollapseUndoOperations(group);
            if (manifest != null)
            {
                manifest.undoGroup = group;
                FinishBackup(manifest);
            }
            int n = list.Count;
            return copies.Count > 0
                ? $"Saved {Plural(n, "change")}. Looksmith's own assets stay as they are, so your versions went into {LooksmithSetup.GeneratedFolder}."
                : $"Saved {Plural(n, "change")}.";
        }

        /// <summary>An asset Looksmith ships (a Look, a sky preset, a water or grass material): Save never writes it.</summary>
        public static bool IsShipped(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(ShippedRoot) && (path.EndsWith(".asset") || path.EndsWith(".mat"));

        static string CopyPathFor(string shippedPath)
        {
            string kind = shippedPath.Contains("/Looks/") ? "Looks" : shippedPath.Contains("/Sky/") ? "Sky"
                : shippedPath.Contains("/Water/") ? "Water" : shippedPath.Contains("/Grass/") ? "Grass" : "Tweaked";
            var folder = $"{LooksmithSetup.GeneratedFolder}/{kind}";
            var name = Path.GetFileNameWithoutExtension(shippedPath);
            var path = $"{folder}/{name} (yours){Path.GetExtension(shippedPath)}";
            // (Unity's unique path is empty while the folder doesn't exist yet: then nothing is in the way)
            return AssetDatabase.IsValidFolder(folder) ? AssetDatabase.GenerateUniqueAssetPath(path) : path;
        }

        /// <summary>
        /// Copies a shipped asset (as it is on disk), puts the drafts on the copy, puts the shipped one back, and points the
        /// open scenes at the copy. Returns the scenes it changed.
        /// </summary>
        static List<Scene> CopyShipped(string from, string to, List<TweakChange> drafts)
        {
            EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
            var shipped = AssetDatabase.LoadMainAssetAtPath(from);
            // the shipped one as it ships: drafts come off before the copy is made from the file
            foreach (var c in drafts)
                TweakValue.Write(c.target, c.property, c.before, false);
            if (!AssetDatabase.CopyAsset(from, to))
                throw new IOException($"Looksmith couldn't copy {from} to {to}.");
            var copy = AssetDatabase.LoadMainAssetAtPath(to);
            foreach (var c in drafts)
            {
                var mapped = MapIntoCopy(c.target, shipped, copy);
                if (mapped != null)
                    TweakValue.Write(mapped, c.property, c.after, false);
            }
            EditorUtility.SetDirty(copy);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(to))
                EditorUtility.SetDirty(sub);
            AssetDatabase.SaveAssetIfDirty(copy);
            return ReplaceReferences(shipped, copy);
        }

        static Object MapIntoCopy(Object target, Object shippedMain, Object copyMain)
        {
            if (target == shippedMain)
                return copyMain;
            if (target is VolumeComponent vc && shippedMain is VolumeProfile sp && copyMain is VolumeProfile cp)
            {
                int i = sp.components.IndexOf(vc);
                return i >= 0 && i < cp.components.Count ? cp.components[i] : null;
            }
            return null;
        }

        /// <summary>Points every reference in the open scenes from one asset to another (with Undo). Returns the scenes changed.</summary>
        public static List<Scene> ReplaceReferences(Object from, Object to)
        {
            var changed = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                bool any = false;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var comp in root.GetComponentsInChildren<Component>(true))
                    {
                        if (comp == null)
                            continue;
                        var so = new SerializedObject(comp);
                        var p = so.GetIterator();
                        bool hit = false;
                        while (p.Next(true))
                            if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == from)
                            {
                                p.objectReferenceValue = to;
                                hit = true;
                            }
                        if (hit)
                        {
                            so.ApplyModifiedProperties();
                            any = true;
                        }
                    }
                if (any)
                    changed.Add(scene);
            }
            return changed;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static SnapshotManifest BeginBackup(List<string> writes, string label)
        {
            try
            {
                VcsGuard.PrepareForWrite(writes.Where(File.Exists).ToList());
                var manifest = new SnapshotManifest
                {
                    id = LooksmithBackups.NewId(label),
                    label = label,
                    createdUtc = DateTime.UtcNow.ToString("o"),
                    unityVersion = Application.unityVersion,
                    status = SnapshotManifest.Running,
                    backupTaken = true,
                };
                Directory.CreateDirectory(manifest.FilesDirectory);
                foreach (var path in writes)
                {
                    var entry = new SnapshotEntry { path = path, sha256Before = LooksmithBackups.Hash(path) };
                    var meta = LooksmithBackups.MetaPath(path);
                    if (meta != null)
                        entry.metaSha256Before = LooksmithBackups.Hash(meta);
                    entry.kind = entry.sha256Before == null ? "created" : "modified";
                    if (entry.sha256Before != null && entry.sha256Before != LooksmithBackups.DirectoryHash)
                    {
                        CopyIn(path, manifest);
                        if (meta != null && entry.metaSha256Before != null)
                            CopyIn(meta, manifest);
                    }
                    manifest.entries.Add(entry);
                }
                manifest.Save();
                return manifest;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Looksmith: Tweaks couldn't take a backup before saving: " + e.Message);
                return null;
            }
        }

        static void CopyIn(string projectPath, SnapshotManifest manifest)
        {
            var dst = manifest.BackupPathOf(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(LooksmithBackups.FullPath(projectPath), dst, true);
        }

        static void FinishBackup(SnapshotManifest m)
        {
            foreach (var e in m.entries)
            {
                e.sha256After = LooksmithBackups.Hash(e.path);
                var meta = LooksmithBackups.MetaPath(e.path);
                if (meta != null)
                    e.metaSha256After = LooksmithBackups.Hash(meta);
            }
            m.entries.RemoveAll(e => e.kind == "created" && e.sha256After == null);
            m.status = SnapshotManifest.Applied;
            m.Save();
        }

        // ------------------------------------------------------------------ Play Mode and reloads

        [Serializable]
        sealed class StoredChange
        {
            public string module, label, property, targetId, before, after;
            public bool saved;
        }

        [Serializable]
        sealed class State
        {
            public List<StoredChange> edit = new List<StoredChange>();      // drafts made outside Play Mode
            public List<StoredChange> play = new List<StoredChange>();      // drafts made while playing
            public List<StoredChange> pending = new List<StoredChange>();   // saved while playing, written on exit
        }

        static State LoadState()
        {
            var json = SessionState.GetString(StateKey, string.Empty);
            return string.IsNullOrEmpty(json) ? new State() : JsonUtility.FromJson<State>(json) ?? new State();
        }

        static void SaveState(State s) => SessionState.SetString(StateKey, JsonUtility.ToJson(s));

        static List<StoredChange> Store(IEnumerable<TweakChange> list) => list.Select(c => new StoredChange
        {
            module = c.module, label = c.label, property = c.property, targetId = c.targetId, saved = c.saved,
            before = TweakValue.ToText(c.before), after = TweakValue.ToText(c.after),
        }).ToList();

        static List<TweakChange> Load(IEnumerable<StoredChange> list)
        {
            var result = new List<TweakChange>();
            foreach (var s in list)
            {
                var target = TweakTargetId.Resolve(s.targetId);
                result.Add(new TweakChange
                {
                    module = s.module, label = s.label, property = s.property, targetId = s.targetId, target = target, saved = s.saved,
                    before = TweakValue.FromText(s.before), after = TweakValue.FromText(s.after),
                });
            }
            return result;
        }

        static void Persist()
        {
            var state = LoadState();
            if (EditorApplication.isPlaying)
                state.play = Store(s_Changes);
            else
                state.edit = Store(s_Changes);
            SaveState(state);
        }

        static void Restore()
        {
            var state = LoadState();
            s_Changes.Clear();
            s_Changes.AddRange(Load(EditorApplication.isPlayingOrWillChangePlaymode ? state.play : state.edit).Where(c => c.target != null));
            RebuildKnown();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            s_DefaultCache.Clear();
            var state = LoadState();
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    ClearAssetUndo();
                    s_UndoSteps.Clear();
                    s_RedoSteps.Clear();
                    s_OpenStep = null;
                    // the edit session waits outside Play Mode; playing starts a session of its own
                    state.edit = Store(s_Changes);
                    state.play.Clear();
                    SaveState(state);
                    s_Changes.Clear();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    s_UndoSteps.Clear();
                    s_RedoSteps.Clear();
                    s_OpenStep = null;
                    // saved drafts are written once Play Mode ends; the rest go back (assets keep what's set in Play Mode)
                    state.pending.AddRange(Store(s_Changes.Where(c => c.saved)));
                    foreach (var c in s_Changes.Where(c => !c.saved && c.target != null && EditorUtility.IsPersistent(c.target)))
                        TweakValue.Write(c.target, c.property, c.before, false);
                    state.play.Clear();
                    SaveState(state);
                    s_Changes.Clear();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    var pending = Load(state.pending);
                    state.pending.Clear();
                    s_Changes.Clear();
                    s_Changes.AddRange(Load(state.edit).Where(c => c.target != null));
                    state.edit.Clear();
                    SaveState(state);
                    if (pending.Count > 0)
                    {
                        // scene objects were put back by Unity: their saved values go on again, then everything is written
                        foreach (var c in pending.Where(c => c.target != null))
                            TweakValue.Write(c.target, c.property, c.after, false);
                        try
                        {
                            Debug.Log("Looksmith Tweaks: " + Write(pending));
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    break;
            }
            RebuildKnown();
            Changed?.Invoke();
        }
    }

    /// <summary>Keeps drafted assets out of Unity's Save Project (Save in Tweaks writes them, through a backup).</summary>
    sealed class LooksmithTweaksSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths) => paths.Where(p => !LooksmithTweaks.HoldsDraft(p)).ToArray();
    }

    /// <summary>Reads and writes one setting of a component, an asset or a material, as a plain value.</summary>
    public static class TweakValue
    {
        public static object Read(Object target, string property)
        {
            if (target == null || string.IsNullOrEmpty(property))
                return null;
            if (target is Material m)
            {
                if (!m.HasProperty(property))
                    return null;
                int i = m.shader.FindPropertyIndex(property);
                if (i < 0)
                    return null;
                switch (m.shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: return m.GetColor(property);
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: return m.GetVector(property);
                    case UnityEngine.Rendering.ShaderPropertyType.Texture: return m.GetTexture(property);
                    case UnityEngine.Rendering.ShaderPropertyType.Int: return m.GetInteger(property);
                    default: return m.GetFloat(property);
                }
            }
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            return p == null ? null : Read(p);
        }

        public static object Read(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.Integer: return p.intValue;
                case SerializedPropertyType.Enum: return p.intValue;
                case SerializedPropertyType.Boolean: return p.boolValue;
                case SerializedPropertyType.Color: return p.colorValue;
                case SerializedPropertyType.Vector2: return (Vector4)p.vector2Value;
                case SerializedPropertyType.Vector3: return (Vector4)p.vector3Value;
                case SerializedPropertyType.Vector4: return p.vector4Value;
                case SerializedPropertyType.ObjectReference: return new ObjectRef(p.objectReferenceValue);
                case SerializedPropertyType.String: return p.stringValue;
                default: return null;
            }
        }

        public static void Write(Object target, string property, object value, bool undo = true)
        {
            if (target == null || value == null)
                return;
            if (target is Material m)
            {
                if (!m.HasProperty(property))
                    return;
                if (undo)
                    Undo.RecordObject(m, "Looksmith: tweak");
                switch (value)
                {
                    case Color c: m.SetColor(property, c); break;
                    case Vector4 v: m.SetVector(property, v); break;
                    case ObjectRef r: m.SetTexture(property, r.value as Texture); break;
                    case int i:
                        int index = m.shader.FindPropertyIndex(property);
                        if (index >= 0 && m.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Int)
                            m.SetInteger(property, i);
                        else
                            m.SetFloat(property, i);
                        break;
                    case float f: m.SetFloat(property, f); break;
                }
                return;
            }
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null)
                return;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = Convert.ToSingle(value is ObjectRef ? 0f : value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum: p.intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: p.boolValue = value is bool b ? b : Convert.ToSingle(value, CultureInfo.InvariantCulture) > 0.5f; break;
                case SerializedPropertyType.Color: if (value is Color col) p.colorValue = col; break;
                case SerializedPropertyType.Vector2: if (value is Vector4 v2) p.vector2Value = v2; break;
                case SerializedPropertyType.Vector3: if (value is Vector4 v3) p.vector3Value = v3; break;
                case SerializedPropertyType.Vector4: if (value is Vector4 v4) p.vector4Value = v4; break;
                case SerializedPropertyType.ObjectReference: if (value is ObjectRef r) p.objectReferenceValue = r.value; break;
                case SerializedPropertyType.String: p.stringValue = value as string ?? ""; break;
                default: return;
            }
            if (undo)
                so.ApplyModifiedProperties();
            else
                so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The leaf settings of an object that Revert to default resets (arrays and the script reference left out).</summary>
        public static IEnumerable<string> Properties(Object target)
        {
            if (target is Material m)
            {
                for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                {
                    if ((m.shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0)
                        continue;
                    if (m.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        continue;
                    yield return m.shader.GetPropertyName(i);
                }
                yield break;
            }
            var so = new SerializedObject(target);
            var p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = !p.isArray && p.propertyType == SerializedPropertyType.Generic;
                if (p.propertyPath == "m_Script" || p.isArray && p.propertyType != SerializedPropertyType.String)
                    continue;
                if (p.propertyType == SerializedPropertyType.ObjectReference || p.propertyType == SerializedPropertyType.ExposedReference
                    || p.propertyType == SerializedPropertyType.ManagedReference)
                    continue;
                if (p.propertyType != SerializedPropertyType.Generic)
                    yield return p.propertyPath;
            }
        }

        public static bool Equal(object a, object b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            switch (a)
            {
                case float fa when b is float fb: return Mathf.Abs(fa - fb) <= 1e-6f * Mathf.Max(1f, Mathf.Abs(fa));
                case Color ca when b is Color cb: return ((Vector4)ca - (Vector4)cb).sqrMagnitude < 1e-10f;
                case Vector4 va when b is Vector4 vb: return (va - vb).sqrMagnitude < 1e-10f;
                case ObjectRef ra when b is ObjectRef rb: return ra.value == rb.value;
                default: return a.Equals(b);
            }
        }

        /// <summary>How a value reads in the change list.</summary>
        public static string Describe(object v)
        {
            switch (v)
            {
                case null: return "-";
                case float f: return f.ToString(Mathf.Abs(f) >= 10f ? "0.#" : "0.###", CultureInfo.InvariantCulture);
                case bool b: return b ? "on" : "off";
                case Color c: return "#" + ColorUtility.ToHtmlStringRGB(c);
                case Vector4 vec: return $"({vec.x:0.##}, {vec.y:0.##}, {vec.z:0.##})";
                case ObjectRef r: return r.value != null ? r.value.name : "none";
                default: return v.ToString();
            }
        }

        public static string ToText(object v)
        {
            switch (v)
            {
                case null: return "n:";
                case float f: return "f:" + f.ToString("R", CultureInfo.InvariantCulture);
                case int i: return "i:" + i.ToString(CultureInfo.InvariantCulture);
                case bool b: return "b:" + (b ? "1" : "0");
                case Color c: return "c:" + string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
                case Vector4 v4: return "v:" + string.Join(",", new[] { v4.x, v4.y, v4.z, v4.w }.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
                case ObjectRef r: return "o:" + (r.value != null ? GlobalObjectId.GetGlobalObjectIdSlow(r.value).ToString() : "");
                case string s: return "s:" + s;
                default: return "n:";
            }
        }

        public static object FromText(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 2)
                return null;
            var body = t.Substring(2);
            float[] F() => body.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            switch (t[0])
            {
                case 'f': return float.Parse(body, CultureInfo.InvariantCulture);
                case 'i': return int.Parse(body, CultureInfo.InvariantCulture);
                case 'b': return body == "1";
                case 'c': { var a = F(); return new Color(a[0], a[1], a[2], a[3]); }
                case 'v': { var a = F(); return new Vector4(a[0], a[1], a[2], a[3]); }
                case 'o':
                    return new ObjectRef(GlobalObjectId.TryParse(body, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null);
                case 's': return body;
                default: return null;
            }
        }
    }

    /// <summary>An object reference as a value (so it compares by the object, not the wrapper).</summary>
    public sealed class ObjectRef
    {
        public readonly Object value;
        public ObjectRef(Object value) => this.value = value;
        public override bool Equals(object obj) => obj is ObjectRef r && r.value == value;
        public override int GetHashCode() => value != null ? value.GetHashCode() : 0;
    }

    /// <summary>
    /// Names an object so it can be found again across Play Mode (scene objects get fresh copies while playing): an asset by
    /// its GlobalObjectId, a scene component by its scene, its place in the hierarchy and its index among its kind.
    /// </summary>
    public static class TweakTargetId
    {
        public static string Of(Object o)
        {
            if (o == null)
                return "";
            if (EditorUtility.IsPersistent(o))
                return "asset:" + GlobalObjectId.GetGlobalObjectIdSlow(o);
            if (o is Component c)
            {
                var same = c.GetComponents(c.GetType());
                return $"scene:{c.gameObject.scene.path}|{PathOf(c.transform)}|{c.GetType().AssemblyQualifiedName}|{Array.IndexOf(same, c)}";
            }
            if (o is GameObject go)
                return $"scene:{go.scene.path}|{PathOf(go.transform)}|GameObject|0";
            return "object:" + LooksmithObjectIds.Get(o);
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent)
            {
                var siblings = t.parent != null ? Enumerable.Range(0, t.parent.childCount).Select(t.parent.GetChild)
                    : t.gameObject.scene.GetRootGameObjects().Select(g => g.transform);
                int n = siblings.TakeWhile(s => s != t).Count(s => s.name == t.name);
                parts.Add(t.name + "#" + n);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static Object Resolve(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (id.StartsWith("asset:"))
                return GlobalObjectId.TryParse(id.Substring(6), out var gid) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) : null;
            if (!id.StartsWith("scene:"))
                return null;
            var parts = id.Substring(6).Split('|');
            if (parts.Length != 4)
                return null;
            Scene scene = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == parts[0])
                    scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            Transform current = null;
            foreach (var step in parts[1].Split('/'))
            {
                int hash = step.LastIndexOf('#');
                var name = step.Substring(0, hash);
                int n = int.Parse(step.Substring(hash + 1), CultureInfo.InvariantCulture);
                var candidates = current == null ? scene.GetRootGameObjects().Select(g => g.transform)
                    : Enumerable.Range(0, current.childCount).Select(current.GetChild);
                current = candidates.Where(t => t.name == name).Skip(n).FirstOrDefault();
                if (current == null)
                    return null;
            }
            if (parts[2] == "GameObject")
                return current.gameObject;
            var type = Type.GetType(parts[2]);
            if (type == null)
                return null;
            var all = current.GetComponents(type);
            int index = int.Parse(parts[3], CultureInfo.InvariantCulture);
            return index < all.Length ? all[index] : null;
        }
    }
}
