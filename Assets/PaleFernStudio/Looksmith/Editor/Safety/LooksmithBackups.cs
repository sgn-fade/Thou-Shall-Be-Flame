using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    [Serializable]
    public sealed class SnapshotEntry
    {
        public string path;               // project-relative, forward slashes
        public string kind;               // "modified" | "created"
        public string sha256Before;
        public string metaSha256Before;
        public string sha256After;
        public string metaSha256After;
    }

    [Serializable]
    public sealed class LedgerEntry
    {
        public const string Created = "created", Remap = "remap";

        public string scenePath;
        public string globalObjectId;
        public string name;
        public int instanceId;            // valid for the current editor session only
        public string entityId;            // Unity 6.6+ entity ID, valid for the current editor session only
        public string kind = Created;     // "created" (a Looksmith-owned object) | "remap" (a renderer pointed at a restyled copy)
        public string detail;
    }

    [Serializable]
    public sealed class SnapshotManifest
    {
        public const string Running = "running", Applied = "applied", Failed = "failed", Reverted = "reverted", PartiallyReverted = "partially reverted";

        public string id;
        public string label;
        public string createdUtc;
        public string unityVersion;
        public string status;
        public int undoGroup;
        public bool backupTaken;
        public List<SnapshotEntry> entries = new List<SnapshotEntry>();
        public List<LedgerEntry> ledger = new List<LedgerEntry>();
        public List<SerializedChange> changes = new List<SerializedChange>();
        public int nextChange;            // journal cursor while running
        public string error;

        public string Directory => Path.Combine(LooksmithBackups.Root, id);
        public string FilesDirectory => Path.Combine(Directory, "files");

        public void Save()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Directory, LooksmithBackups.ManifestName), JsonUtility.ToJson(this, true));
        }

        public string BackupPathOf(string projectPath) => Path.Combine(FilesDirectory, projectPath.Replace('/', Path.DirectorySeparatorChar));

        public long SizeOnDisk()
        {
            if (!System.IO.Directory.Exists(Directory))
                return 0;
            return new DirectoryInfo(Directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
    }

    /// <summary>
    /// Backups live in &lt;project&gt;/LooksmithBackups/ (outside Assets: never imported, never in builds, no GUID clashes).
    /// </summary>
    public static class LooksmithBackups
    {
        public const string FolderName = "LooksmithBackups";
        public const string ManifestName = "manifest.json";

        const string OverrideKey = "Looksmith.BackupsRootOverride";
        static string s_RootOverride;

        /// <summary>Tests point this at a scratch folder. Survives domain reloads (SessionState) so a resumed run finds its journal.</summary>
        public static string rootOverride
        {
            get
            {
                if (s_RootOverride == null)
                {
                    var saved = UnityEditor.SessionState.GetString(OverrideKey, string.Empty);
                    s_RootOverride = saved.Length > 0 ? saved : null;
                }
                return s_RootOverride;
            }
            set
            {
                s_RootOverride = value;
                UnityEditor.SessionState.SetString(OverrideKey, value ?? string.Empty);
            }
        }

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string Root => rootOverride ?? Path.Combine(ProjectRoot, FolderName);

        public static string FullPath(string projectPath) => Path.Combine(ProjectRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));

        public const string DirectoryHash = "dir";

        /// <summary>SHA-256 of a file, <see cref="DirectoryHash"/> for a folder, null when absent.</summary>
        public static string Hash(string projectPath)
        {
            var full = FullPath(projectPath);
            if (Directory.Exists(full))
                return DirectoryHash;
            if (!File.Exists(full))
                return null;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(full))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        public static string MetaPath(string projectPath) => projectPath.StartsWith("Assets/") ? projectPath + ".meta" : null;

        public static List<SnapshotManifest> List()
        {
            var list = new List<SnapshotManifest>();
            if (!Directory.Exists(Root))
                return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var file = Path.Combine(dir, ManifestName);
                if (!File.Exists(file))
                    continue;
                try
                {
                    list.Add(JsonUtility.FromJson<SnapshotManifest>(File.ReadAllText(file)));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Looksmith: unreadable backup manifest {file}: {e.Message}");
                }
            }
            return list.OrderByDescending(m => m.createdUtc, StringComparer.Ordinal).ToList();
        }

        public static SnapshotManifest Load(string id)
        {
            var file = Path.Combine(Root, id, ManifestName);
            return File.Exists(file) ? JsonUtility.FromJson<SnapshotManifest>(File.ReadAllText(file)) : null;
        }

        public static long TotalSize() => List().Sum(m => m.SizeOnDisk());

        /// <summary>Old backups beyond the retention count (never running ones). Deleting them asks the user first.</summary>
        public static List<SnapshotManifest> BeyondRetention(int keep)
        {
            return List().Where(m => m.status != SnapshotManifest.Running).Skip(Math.Max(1, keep)).ToList();
        }

        /// <summary>Permanently deletes the given backups (the UI confirms first).</summary>
        public static void Delete(IEnumerable<SnapshotManifest> snapshots)
        {
            foreach (var m in snapshots)
                if (Directory.Exists(m.Directory))
                    Directory.Delete(m.Directory, true);
        }

        internal static string NewId(string label)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safe = new string((label ?? "change").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            var id = $"{stamp} {safe}";
            int n = 2;
            while (Directory.Exists(Path.Combine(Root, id)))
                id = $"{stamp} {safe} ({n++})";
            return id;
        }
    }
}
