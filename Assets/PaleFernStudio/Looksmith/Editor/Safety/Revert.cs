using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    public enum RevertAction
    {
        Restore,          // unchanged since Looksmith wrote it: put the backup back
        TrashCreated,     // Looksmith created it and nobody changed it: move to the OS trash
        AlreadyReverted,  // already matches the backup
        Conflict,         // the user changed it after Looksmith did: ask
        Missing,          // gone (deleted by the user): nothing to do
    }

    public enum ConflictResolution
    {
        KeepMine,
        RestoreBackup,
        Skip,
    }

    public sealed class RevertItem
    {
        public SnapshotEntry entry;
        public RevertAction action;
        public ConflictResolution resolution = ConflictResolution.KeepMine;
        public string reason;
    }

    public sealed class RevertPlan
    {
        public SnapshotManifest manifest;
        public readonly List<RevertItem> items = new List<RevertItem>();
        public IEnumerable<RevertItem> conflicts => items.Where(i => i.action == RevertAction.Conflict);
    }

    /// <summary>Exact, hash-checked revert of a snapshot (P5). Deleted files go to the OS trash (P4).</summary>
    public static class Revert
    {
        public static RevertPlan Plan(SnapshotManifest manifest)
        {
            var plan = new RevertPlan { manifest = manifest };
            foreach (var e in manifest.entries)
            {
                var item = new RevertItem { entry = e };
                var now = LooksmithBackups.Hash(e.path);
                var meta = LooksmithBackups.MetaPath(e.path);
                var metaNow = meta != null ? LooksmithBackups.Hash(meta) : null;
                if (e.kind == "created")
                {
                    if (now == null)
                        item.action = RevertAction.Missing;
                    else if (now == LooksmithBackups.DirectoryHash && HasForeignFiles(e.path, manifest))
                    {
                        item.action = RevertAction.Conflict;
                        item.reason = "Folder created by Looksmith now holds your files";
                    }
                    else if (Same(now, e.sha256After))
                        item.action = RevertAction.TrashCreated;
                    else
                    {
                        item.action = RevertAction.Conflict;
                        item.reason = "Created by Looksmith, then changed by you";
                    }
                }
                else if (Same(now, e.sha256Before) && Same(metaNow, e.metaSha256Before))
                    item.action = RevertAction.AlreadyReverted;
                else if (Same(now, e.sha256After) && Same(metaNow, e.metaSha256After))
                    item.action = RevertAction.Restore;
                else if (now == null)
                {
                    item.action = RevertAction.Conflict;
                    item.reason = "Deleted after Looksmith changed it";
                }
                else
                {
                    item.action = RevertAction.Conflict;
                    item.reason = "Changed after Looksmith changed it";
                }
                plan.items.Add(item);
            }
            return plan;
        }

        // Manifests round-trip through JsonUtility, which turns null strings into "": treat both as "no hash".
        static bool Same(string a, string b) => (a ?? string.Empty) == (b ?? string.Empty);

        static bool HasForeignFiles(string folder, SnapshotManifest m)
        {
            var ours = new HashSet<string>(m.entries.Select(x => x.path));
            foreach (var file in Directory.GetFiles(LooksmithBackups.FullPath(folder), "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(LooksmithBackups.ProjectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.EndsWith(".meta"))
                    rel = rel.Substring(0, rel.Length - 5);
                if (!ours.Contains(rel))
                    return true;
            }
            return false;
        }

        /// <summary>Same rule as Apply: revert only touches saved work, so unrelated unsaved edits are never written.</summary>
        public static List<string> Preflight()
        {
            var problems = new List<string>();
            var dirty = DirtyAssets.Paths().ToList();
            if (dirty.Count > 0)
                problems.Add($"Save your project first: {dirty.Count} asset(s) have unsaved changes ({string.Join(", ", dirty.Take(4))}).");
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
                if (scene.isDirty)
                    problems.Add($"Save the scene first: {(string.IsNullOrEmpty(scene.path) ? "Untitled (new, never saved)" : scene.path)}");
            return problems;
        }

        /// <summary>
        /// Reverts the plan. With <paramref name="only"/>, just those project paths are reverted (a single material from
        /// its inspector); everything else in the snapshot stays applied and the snapshot is marked partially reverted.
        /// </summary>
        public static string Execute(RevertPlan plan, ICollection<string> only = null)
        {
            var preflight = Preflight();
            if (preflight.Count > 0)
                throw new InvalidOperationException("Looksmith can't revert yet:\n" + string.Join("\n", preflight));
            var m = plan.manifest;
            var restoredAssets = new List<string>();
            var trashed = new List<string>();
            int keptConflicts = 0;
            var scenesToReload = new List<string>();

            int skipped = 0;
            var restoredOrTrashed = new HashSet<string>();
            // Deepest paths first, so files leave a created folder before the folder itself.
            foreach (var item in plan.items.OrderByDescending(i => i.entry.path.Length))
            {
                if (only != null && !only.Contains(item.entry.path))
                {
                    if (item.action != RevertAction.AlreadyReverted && item.action != RevertAction.Missing)
                        skipped++;
                    continue;
                }
                bool restore = item.action == RevertAction.Restore ||
                               (item.action == RevertAction.Conflict && item.resolution == ConflictResolution.RestoreBackup);
                bool trash = item.action == RevertAction.TrashCreated ||
                             (item.action == RevertAction.Conflict && item.resolution == ConflictResolution.RestoreBackup && item.entry.kind == "created");
                if (item.action == RevertAction.Conflict && item.resolution != ConflictResolution.RestoreBackup)
                    keptConflicts++;

                if (trash)
                {
                    if (LooksmithBackups.Hash(item.entry.path) == null)
                        continue; // went with its folder
                    if (item.entry.path.EndsWith(".meta") && LooksmithBackups.Hash(item.entry.path.Substring(0, item.entry.path.Length - 5)) != null)
                        continue; // Unity removes it with its asset
                    if (item.entry.path.StartsWith("Assets/"))
                        AssetDatabase.MoveAssetToTrash(item.entry.path);
                    else
                        File.Delete(LooksmithBackups.FullPath(item.entry.path));
                    trashed.Add(item.entry.path);
                    restoredOrTrashed.Add(item.entry.path);
                }
                else if (restore)
                {
                    RestoreFile(m, item.entry.path);
                    var meta = LooksmithBackups.MetaPath(item.entry.path);
                    if (meta != null && !string.IsNullOrEmpty(item.entry.metaSha256Before))
                        RestoreFile(m, meta);
                    restoredOrTrashed.Add(item.entry.path);
                    if (item.entry.path.EndsWith(".unity"))
                        scenesToReload.Add(item.entry.path);
                    else if (item.entry.path.StartsWith("Assets/"))
                        restoredAssets.Add(item.entry.path);
                }
            }

            // In-memory settings (PlayerSettings and friends) follow their restored files, and only those: a kept
            // conflict or a partial revert leaves the file, so its setting must stay too. They come back before any asset
            // reimports: a model reimported under the colour space being reverted caches a result for the wrong inputs
            // (Unity's "generated inconsistent result" warning).
            foreach (var sc in m.changes)
            {
                try
                {
                    var change = sc.Restore();
                    if (change.Writes().Any(restoredOrTrashed.Contains))
                        change.AfterRevert();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            foreach (var path in restoredAssets)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            foreach (var path in scenesToReload)
            {
                var scene = SceneManager.GetSceneByPath(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.OpenScene(path, SceneManager.sceneCount > 1 ? OpenSceneMode.Additive : OpenSceneMode.Single);
            }

            m.status = keptConflicts > 0 || skipped > 0 ? SnapshotManifest.PartiallyReverted : SnapshotManifest.Reverted;
            m.Save();
            return $"restored {restoredAssets.Count + scenesToReload.Count}, trashed {trashed.Count}, kept {keptConflicts} of yours";
        }

        static void RestoreFile(SnapshotManifest m, string projectPath)
        {
            var src = m.BackupPathOf(projectPath);
            if (!File.Exists(src))
                throw new FileNotFoundException("Looksmith backup is missing a file", src);
            var dst = LooksmithBackups.FullPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
        }

        /// <summary>Removes Looksmith-created scene objects of a snapshot from the open scenes (ledger + LooksmithOwned marker).</summary>
        public static int RemoveOwnedObjects(SnapshotManifest m)
        {
            int removed = 0;
            foreach (var owned in LooksmithFind.All<LooksmithOwned>(FindObjectsInactive.Include))
            {
                if (m != null && owned.snapshotId != m.id)
                    continue;
                Undo.DestroyObjectImmediate(owned.gameObject);
                removed++;
            }
            return removed;
        }
    }
}
