using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Respects version control (P8): checks files out before writing; never runs git itself.</summary>
    public static class VcsGuard
    {
        /// <summary>Perforce / Unity Version Control (or any VersionControl provider) is active.</summary>
        public static bool providerActive => Provider.enabled && Provider.isActive;

        /// <summary>A git repository contains this project; the first Apply recommends committing first.</summary>
        public static bool gitDetected
        {
            get
            {
                var dir = new DirectoryInfo(LooksmithBackups.ProjectRoot);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                        return true;
                    dir = dir.Parent;
                }
                return false;
            }
        }

        /// <summary>Checks out existing files through the active provider. Returns paths that could not be made editable.</summary>
        public static List<string> PrepareForWrite(IEnumerable<string> paths)
        {
            var failed = new List<string>();
            if (!providerActive)
                return failed;
            var existing = paths.Where(p => File.Exists(LooksmithBackups.FullPath(p))).ToArray();
            if (existing.Length == 0)
                return failed;
            var notEditable = new List<string>();
            if (!AssetDatabase.MakeEditable(existing, "Looksmith changes", notEditable))
            {
                failed.AddRange(notEditable);
                Debug.LogWarning("Looksmith: version control could not check out: " + string.Join(", ", notEditable));
            }
            return failed;
        }
    }
}
