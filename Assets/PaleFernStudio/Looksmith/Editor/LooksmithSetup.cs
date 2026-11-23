using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Builds the Home stepper's combined "Apply" preview and the uninstall steps.</summary>
    public static class LooksmithSetup
    {
        public const string GeneratedFolder = "Assets/Looksmith Generated";

        public static string ProfilePathFor(Scene scene) =>
            $"{GeneratedFolder}/Looks/{(string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name)} Look.asset";

        /// <summary>Project fixes (ticked per the scan) + the scene root and Look Profile when the scene isn't styled.</summary>
        public static Changeset BuildApply(IEnumerable<ScanFinding> findings, Scene scene, string lookName = null)
        {
            lookName ??= LooksmithLooks.FirstLook;
            var cs = new Changeset { label = "Set up Looksmith" };
            cs.changes.AddRange(LooksmithScan.RecommendedFixes(findings));
            bool styled = LooksmithFind.All<LooksmithLookVolume>(FindObjectsInactive.Include)
                .Any(v => v.gameObject.scene == scene);
            if (!styled && scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                var profilePath = ProfilePathFor(scene);
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) == null)
                    cs.changes.Add(new CreateLookProfileChange { profilePath = profilePath, lookName = lookName });
                cs.changes.Add(new AddLooksmithRootChange { scenePath = scene.path, profilePath = profilePath });
            }
            // Materials to restyle in this scene, one toggle per group in the preview (guessed mappings stay off).
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                cs.changes.AddRange(LooksmithRestyle.Plan(RestyleScope.Scene).ToChangeset().changes);
            return cs;
        }

        /// <summary>Uninstall step 1: revert every applied backup, newest first. Returns what couldn't be reverted cleanly.</summary>
        public static List<string> RevertAll()
        {
            var notes = new List<string>();
            foreach (var m in LooksmithBackups.List().Where(m => m.status == SnapshotManifest.Applied || m.status == SnapshotManifest.Failed))
            {
                var plan = Revert.Plan(m);
                foreach (var c in plan.conflicts)
                    notes.Add($"{m.label}: kept your version of {c.entry.path} ({c.reason})");
                Revert.Execute(plan);
            }
            return notes;
        }

        /// <summary>Uninstall step 2: remove every Looksmith-created scene object in the open scenes.</summary>
        public static int RemoveSceneRoots() => Revert.RemoveOwnedObjects(null);
    }
}
