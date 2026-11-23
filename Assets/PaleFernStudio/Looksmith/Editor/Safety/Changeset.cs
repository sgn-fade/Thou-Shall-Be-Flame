using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Handed to each change while it applies.</summary>
    public sealed class ChangeContext
    {
        readonly SnapshotManifest m_Manifest;

        internal ChangeContext(SnapshotManifest manifest) => m_Manifest = manifest;

        public string snapshotId => m_Manifest.id;

        /// <summary>Registers a scene object this change created: marks it LooksmithOwned, makes it undoable, logs it.</summary>
        public void RegisterCreatedObject(GameObject go, string reason)
        {
            var owned = go.GetComponent<LooksmithOwned>() ?? go.AddComponent<LooksmithOwned>();
            owned.snapshotId = m_Manifest.id;
            owned.reason = reason;
            Undo.RegisterCreatedObjectUndo(go, "Looksmith: " + reason);
            var entry = new LedgerEntry { scenePath = go.scene.path, name = go.name };
            SetObjectId(entry, go);
            m_Manifest.ledger.Add(entry);
        }

        /// <summary>Logs a scene object whose material slots this change re-pointed (restored with the scene file on Revert).</summary>
        public void RecordRemap(UnityEngine.Object target, string detail)
        {
            var go = target is Component c ? c.gameObject : target as GameObject;
            var entry = new LedgerEntry
            {
                scenePath = go != null ? go.scene.path : null, name = go != null ? go.name : target.name,
                kind = LedgerEntry.Remap, detail = detail,
            };
            SetObjectId(entry, target);
            m_Manifest.ledger.Add(entry);
        }

        static void SetObjectId(LedgerEntry entry, UnityEngine.Object target)
        {
#if UNITY_6000_6_OR_NEWER
            entry.entityId = EntityId.ToULong(target.GetEntityId()).ToString(CultureInfo.InvariantCulture);
#else
            entry.instanceId = target.GetInstanceID();
#endif
        }
    }

    /// <summary>
    /// A previewed set of changes, applied as one unit: one Snapshot (backup), one Undo group, one entry in
    /// Hub > Changes (P3). Changes apply one per editor tick and the cursor is journaled in the snapshot
    /// manifest, so a domain reload in the middle resumes where it stopped instead of losing work.
    /// </summary>
    public sealed class Changeset
    {
        public readonly List<LooksmithChange> changes = new List<LooksmithChange>();
        public string label = "Looksmith changes";

        public Changeset() { }
        public Changeset(string label, IEnumerable<LooksmithChange> items)
        {
            this.label = label;
            changes.AddRange(items);
        }

        public IEnumerable<LooksmithChange> Enabled => changes.Where(c => c.enabled);

        public List<string> WriteSet() =>
            Enabled.SelectMany(c => c.Writes().Concat(c.Scenes())).Select(Normalize).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        /// <summary>Reasons the changeset can't start (dirty scenes, not-editable files...). Empty = ready.</summary>
        public List<string> Preflight()
        {
            var problems = new List<string>();
            foreach (var scenePath in Enabled.SelectMany(c => c.Scenes()).Distinct())
            {
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (!scene.IsValid() || !scene.isLoaded)
                    problems.Add($"Scene not open: {scenePath}");
                else if (scene.isDirty)
                    problems.Add($"Save the scene first (backups need a clean file): {scenePath}");
            }
            if (ChangesetRunner.isRunning)
                problems.Add("Another Looksmith change is still applying.");
            var dirty = DirtyAssets.Paths().ToList();
            if (dirty.Count > 0)
                problems.Add($"Save your project first: {dirty.Count} asset(s) have unsaved changes ({string.Join(", ", dirty.Take(4))}{(dirty.Count > 4 ? ", …" : "")}). Looksmith backs up files before changing them, so it only writes saved work.");
            return problems;
        }

        internal static string Normalize(string p) => p.Replace('\\', '/');
    }

    /// <summary>Persistent assets (and project settings) with unsaved in-memory changes.</summary>
    public static class DirtyAssets
    {
        public static IEnumerable<string> Paths()
        {
            var seen = new HashSet<string>();
            foreach (var o in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (o == null || !EditorUtility.IsPersistent(o) || !EditorUtility.IsDirty(o))
                    continue;
                var path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path) || path.EndsWith(".unity"))
                    continue;
                // Only what SaveAssets would actually write: native assets in Assets/ and project settings.
                // Imported assets (models, textures, shader graphs) and built-in resources are never written back.
                bool settings = path.StartsWith("ProjectSettings/");
                if (!settings && !(path.StartsWith("Assets/") && AssetDatabase.IsNativeAsset(o)))
                    continue;
                if (seen.Add(path))
                    yield return path;
            }
        }
    }

    /// <summary>Drives a Changeset across editor ticks; recovers a journaled run after a domain reload.</summary>
    [InitializeOnLoad]
    public static class ChangesetRunner
    {
        static SnapshotManifest s_Active;
        static List<LooksmithChange> s_Changes;

        public static event Action<SnapshotManifest> Completed;
        public static bool isRunning => s_Active != null;
        public static SnapshotManifest active => s_Active;

        static ChangesetRunner()
        {
            // The first editor tick after a reload, not delayCall: delayCall waits for an inspector update, which an
            // unfocused editor doesn't do, so a run interrupted while you're in another app would sit until you return.
            EditorApplication.update -= RecoverOnFirstTick;
            EditorApplication.update += RecoverOnFirstTick;
        }

        static void RecoverOnFirstTick()
        {
            EditorApplication.update -= RecoverOnFirstTick;
            Recover();
        }

        /// <summary>
        /// Takes the backup and starts applying. With <paramref name="synchronous"/> the whole set applies now
        /// (scripts, tests); otherwise one change per editor tick (Hub, long restyles).
        /// </summary>
        public static SnapshotManifest Start(Changeset changeset, bool backupFirst = true, bool synchronous = false)
        {
            var problems = changeset.Preflight();
            if (problems.Count > 0)
                throw new InvalidOperationException("Looksmith can't apply yet:\n" + string.Join("\n", problems));

            var enabled = changeset.Enabled.ToList();
            var writes = changeset.WriteSet();
            VcsGuard.PrepareForWrite(writes);

            var manifest = new SnapshotManifest
            {
                id = LooksmithBackups.NewId(changeset.label),
                label = changeset.label,
                createdUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                status = SnapshotManifest.Running,
                backupTaken = backupFirst,
                changes = enabled.Select(SerializedChange.From).ToList(),
            };
            Directory.CreateDirectory(manifest.FilesDirectory);
            foreach (var path in writes)
            {
                var entry = new SnapshotEntry { path = path, sha256Before = LooksmithBackups.Hash(path) };
                var meta = LooksmithBackups.MetaPath(path);
                if (meta != null)
                    entry.metaSha256Before = LooksmithBackups.Hash(meta);
                entry.kind = entry.sha256Before == null ? "created" : "modified";
                if (backupFirst && entry.sha256Before != null && entry.sha256Before != LooksmithBackups.DirectoryHash)
                {
                    CopyIn(path, manifest);
                    if (meta != null && entry.metaSha256Before != null)
                        CopyIn(meta, manifest);
                }
                manifest.entries.Add(entry);
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Looksmith: " + changeset.label);
            manifest.undoGroup = Undo.GetCurrentGroup();
            manifest.Save();

            s_Active = manifest;
            s_Changes = enabled;
            if (synchronous)
            {
                while (s_Active != null)
                    StepOnce(waitForPendingReload: false);
            }
            else
            {
                EditorApplication.update -= Step;
                EditorApplication.update += Step;
            }
            return manifest;
        }

        static void CopyIn(string projectPath, SnapshotManifest manifest)
        {
            var dst = manifest.BackupPathOf(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(LooksmithBackups.FullPath(projectPath), dst, true);
        }

        static readonly Func<bool> s_ReloadRequested = CreateReloadRequested();

        static Func<bool> CreateReloadRequested()
        {
            var method = typeof(UnityEditorInternal.InternalEditorUtility).GetMethod("IsScriptReloadRequested",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return method != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method) : () => false;
        }

        static bool ReloadRequested() => s_ReloadRequested();

        static void Step() => StepOnce(waitForPendingReload: true);

        static void StepOnce(bool waitForPendingReload)
        {
            var m = s_Active;
            if (m == null)
            {
                EditorApplication.update -= Step;
                return;
            }
            // Don't apply further changes while a script reload or compile is pending: the reload would land in the
            // middle of the next change. Waiting makes it land between changes, where the journal resumes it.
            if (waitForPendingReload && (EditorApplication.isCompiling || ReloadRequested()))
                return;
            if (m.nextChange < s_Changes.Count)
            {
                var change = s_Changes[m.nextChange];
                try
                {
                    CaptureSideEffects(m);
                    change.Apply(new ChangeContext(m));
                }
                catch (Exception e)
                {
                    m.error = $"{change.title}: {e.Message}";
                    Debug.LogException(e);
                    Finish(m, SnapshotManifest.Failed);
                    return;
                }
                m.nextChange++;
                m.Save();   // journal: a reload after this point resumes at the next change
                return;
            }
            Finish(m, SnapshotManifest.Applied);
        }

        /// <summary>
        /// Preflight guarantees nothing was dirty before the run, so any asset dirty now was dirtied by Looksmith's own
        /// changes (often indirectly, e.g. a pipeline asset reacting to its renderer). Its file on disk is still the
        /// original: back it up before anything saves it, so Revert stays exact.
        /// </summary>
        static void CaptureSideEffects(SnapshotManifest m)
        {
            foreach (var path in DirtyAssets.Paths())
            {
                if (m.entries.Any(e => e.path == path))
                    continue;
                var entry = new SnapshotEntry { path = path, sha256Before = LooksmithBackups.Hash(path), kind = "modified" };
                var meta = LooksmithBackups.MetaPath(path);
                if (meta != null)
                    entry.metaSha256Before = LooksmithBackups.Hash(meta);
                if (entry.sha256Before == null)
                    entry.kind = "created";
                else if (m.backupTaken)
                {
                    CopyIn(path, m);
                    if (meta != null && entry.metaSha256Before != null)
                        CopyIn(meta, m);
                }
                m.entries.Add(entry);
            }
        }

        static void Finish(SnapshotManifest m, string status)
        {
            EditorApplication.update -= Step;
            CaptureSideEffects(m);
            AssetDatabase.SaveAssets();   // everything dirty now is Looksmith's (preflight required a saved project)
            var scenes = m.entries.Select(e => e.path).Where(p => p.EndsWith(".unity")).ToList();
            foreach (var path in scenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    EditorSceneManager.SaveScene(scene);
            }
            foreach (var l in m.ledger)
            {
#if UNITY_6000_6_OR_NEWER
                var entityId = string.IsNullOrEmpty(l.entityId)
                    ? EntityId.None
                    : EntityId.FromULong(ulong.Parse(l.entityId, CultureInfo.InvariantCulture));
                var obj = entityId.IsValid() ? EditorUtility.EntityIdToObject(entityId) : null;
#else
                var obj = EditorUtility.EntityIdToObject(l.instanceId);
#endif
                if (obj != null)
                    l.globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            }
            foreach (var e in m.entries)
            {
                e.sha256After = LooksmithBackups.Hash(e.path);
                var meta = LooksmithBackups.MetaPath(e.path);
                if (meta != null)
                    e.metaSha256After = LooksmithBackups.Hash(meta);
            }
            m.entries.RemoveAll(e => e.kind == "created" && e.sha256After == null);
            m.status = status;
            m.Save();
            if (m.undoGroup > 0)
                Undo.CollapseUndoOperations(m.undoGroup);
            s_Active = null;
            s_Changes = null;
            Completed?.Invoke(m);
        }

        /// <summary>Resumes a run that a domain reload interrupted (journal = manifest with status "running").</summary>
        static void Recover()
        {
            if (s_Active != null)
                return;
            SnapshotManifest running = null;
            try
            {
                running = LooksmithBackups.List().FirstOrDefault(x => x.status == SnapshotManifest.Running);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (running == null)
                return;
            try
            {
                s_Changes = running.changes.Select(c => c.Restore()).ToList();
            }
            catch (Exception e)
            {
                running.error = "Could not resume after a domain reload: " + e.Message;
                running.status = SnapshotManifest.Failed;
                running.Save();
                return;
            }
            s_Active = running;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
        }

        /// <summary>Test hook: run any pending recovery now.</summary>
        internal static void RecoverNow() => Recover();
    }
}
