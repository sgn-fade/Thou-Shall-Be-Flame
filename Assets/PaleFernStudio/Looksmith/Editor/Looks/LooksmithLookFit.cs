using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Copies a shipped look into Looksmith Generated and points the scene's Look Volumes at the copy: shipped looks stay
    /// read-only, so the Advisor's tweaks (and the user's) go on a copy of their own.
    /// </summary>
    [Serializable]
    public sealed class ForkLookChange : LooksmithChange
    {
        public string shippedPath;
        public string copyPath;
        public string scenePath;

        public override ChangeGroup group => ChangeGroup.Assets;
        public override string title => $"Copy {Path.GetFileNameWithoutExtension(shippedPath)} to {copyPath} (shipped looks stay read-only)";
        public override IEnumerable<string> Writes()
        {
            if (!AssetDatabase.IsValidFolder(LooksmithSetup.GeneratedFolder))
            {
                yield return LooksmithSetup.GeneratedFolder;
                yield return LooksmithSetup.GeneratedFolder + ".meta";
            }
            if (!AssetDatabase.IsValidFolder(LooksmithSetup.GeneratedFolder + "/Looks"))
            {
                yield return LooksmithSetup.GeneratedFolder + "/Looks";
                yield return LooksmithSetup.GeneratedFolder + "/Looks.meta";
            }
            yield return copyPath;
            if (!string.IsNullOrEmpty(scenePath))
                yield return scenePath;
        }
        public override IEnumerable<string> Scenes()
        {
            if (!string.IsNullOrEmpty(scenePath))
                yield return scenePath;
        }

        public override void Apply(ChangeContext context)
        {
            CreateLookProfileChange.EnsureFolder(Path.GetDirectoryName(copyPath).Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(shippedPath, copyPath))
                throw new InvalidOperationException($"Couldn't copy {shippedPath} to {copyPath}");
            var copy = AssetDatabase.LoadAssetAtPath<VolumeProfile>(copyPath);
            if (string.IsNullOrEmpty(scenePath))
                return;
            var scene = SceneManager.GetSceneByPath(scenePath);
            foreach (var root in scene.GetRootGameObjects())
            foreach (var volume in root.GetComponentsInChildren<Volume>(true))
            {
                if (volume.sharedProfile == null || AssetDatabase.GetAssetPath(volume.sharedProfile) != shippedPath || volume.GetComponent<LooksmithLookVolume>() == null)
                    continue;
                Undo.RecordObject(volume, "Looksmith: use the fitted look");
                volume.sharedProfile = copy;
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>Sets parameters on a Look Profile (advice from the Look Advisor), overriding them.</summary>
    [Serializable]
    public sealed class SetLookParametersChange : LooksmithChange
    {
        public string profilePath;
        public string label;
        public List<LooksmithAdviceChange> changes = new List<LooksmithAdviceChange>();

        public override ChangeGroup group => ChangeGroup.Assets;
        public override string title => label;
        public override string detail => string.Join("\n", changes.Select(c => c.label));
        public override IEnumerable<string> Writes() { yield return profilePath; }

        public override void Apply(ChangeContext context)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) ?? throw new InvalidOperationException("Look Profile not found: " + profilePath);
            foreach (var c in changes)
            {
                LooksmithLookAdvisor.FindParameter(profile, c.component, c.parameter, out var owner);
                if (owner != null)
                    Undo.RecordObject(owner, "Looksmith: fit the look");
                if (!LooksmithLookAdvisor.Set(profile, c))
                    throw new InvalidOperationException($"{c.component}.{c.parameter} not found on {profilePath}");
                if (owner != null)
                    EditorUtility.SetDirty(owner);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
    }

    /// <summary>
    /// "Fit to this scene": the Look Advisor run on the open scene, from the scene view or the main camera, and its
    /// advice applied like every Looksmith write (a preview, a backup, one Undo group, revertable from Hub > Changes).
    /// </summary>
    public static class LooksmithLookFit
    {
        /// <summary>The camera the advice is judged from: the main camera, else the first enabled camera.</summary>
        public static Camera ReferenceCamera()
        {
            var cam = Camera.main;
            if (cam != null)
                return cam;
            return LooksmithFind.All<Camera>().FirstOrDefault(c => c.isActiveAndEnabled);
        }

        /// <summary>Reads the scene and a small render of it from the reference camera.</summary>
        public static LooksmithSceneReading Read(Camera camera, int outputHeight = 0)
        {
            var reading = LooksmithLookAdvisor.ReadScene(camera);
            var frame = LooksmithLookAdvisor.CaptureFrame(camera);
            try
            {
                LooksmithLookAdvisor.ReadFrame(ref reading, frame);
            }
            finally
            {
                if (frame != null)
                    UnityEngine.Object.DestroyImmediate(frame);
            }
            reading.outputHeight = outputHeight > 0 ? outputHeight : GameViewHeight();
            return reading;
        }

        /// <summary>The height the game is shown at in the editor (the Game view's target), 1080 when unknown.</summary>
        static int GameViewHeight()
        {
            UnityEditor.PlayModeWindow.GetRenderingResolution(out uint w, out uint h);
            return h > 0 ? (int)h : 1080;
        }

        /// <summary>The changes that carry out advice on a profile: a fork first when it's a shipped look.</summary>
        public static Changeset Build(VolumeProfile profile, IList<LooksmithAdvice> advice, Scene scene)
        {
            var path = AssetDatabase.GetAssetPath(profile);
            var list = new List<LooksmithChange>();
            if (path.StartsWith(LooksmithLooks.ShippedFolder + "/"))
            {
                // GenerateUniqueAssetPath returns "" for a folder that doesn't exist yet (a new project's first fit)
                var copy = $"{LooksmithSetup.GeneratedFolder}/Looks/{Path.GetFileNameWithoutExtension(path)} (fitted).asset";
                if (AssetDatabase.IsValidFolder($"{LooksmithSetup.GeneratedFolder}/Looks"))
                    copy = AssetDatabase.GenerateUniqueAssetPath(copy);
                list.Add(new ForkLookChange { shippedPath = path, copyPath = copy, scenePath = scene.IsValid() ? scene.path : null });
                path = copy;
            }
            foreach (var a in advice)
            {
                if (a.changes.Count > 0)
                    list.Add(new SetLookParametersChange { profilePath = path, label = a.title, changes = a.changes.ToList() });
                if (a.turnOffUnityFog && scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                    list.Add(new SetSceneFogChange { scenePath = scene.path, fog = false });
                if (a.turnOnPostProcessing && SetCameraPostProcessingChange.For(ReferenceCamera()) is SetCameraPostProcessingChange pp)
                    list.Add(pp);
            }
            var title = advice.Count == 1 ? "Fit the look: " + advice[0].title : $"Fit the look to {scene.name} ({advice.Count} changes)";
            return new Changeset(title, list);
        }

        /// <summary>Previews, backs up and applies advice (the Look inspector's Apply buttons).</summary>
        public static bool Apply(VolumeProfile profile, IList<LooksmithAdvice> advice)
        {
            if (profile == null || advice == null || advice.Count == 0)
                return false;
            var scene = SceneManager.GetActiveScene();
            var changeset = Build(profile, advice, scene);
            var writes = string.Join("\n", changeset.Enabled.SelectMany(c => c.Writes()).Distinct().Take(8));
            var what = string.Join("\n", advice.SelectMany(a => a.changes.Select(c => "• " + c.label)
                .Concat(a.turnOffUnityFog ? new[] { "• Unity fog: off" } : Array.Empty<string>())
                .Concat(a.turnOnPostProcessing ? new[] { "• The camera's Post Processing: on" } : Array.Empty<string>())));
            if (!EditorUtility.DisplayDialog("Looksmith: fit the look", $"{what}\n\nWrites:\n{writes}\n\nA backup is made first; Hub > Changes reverts it.", "Back up and apply", "Cancel"))
                return false;
            return LooksmithHubWindow.ApplyWithPrompts(changeset, backupFirst: true);
        }
    }
}
