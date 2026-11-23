using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The inspector of a Looksmith component or asset (plan §4.6): a short summary and Open in Tweaks, where every setting
    /// is, with drafts, Save and Revert. Project Settings > Looksmith > Show all fields in the Inspector brings Unity's fields
    /// back (for scripting and debugging; changes made there are ordinary edits, not Tweaks drafts).
    /// </summary>
    public abstract class LooksmithTweaksInspector : UnityEditor.Editor
    {
        protected abstract string module { get; }

        protected virtual string Summary() => null;

        public override void OnInspectorGUI() => Header(module, Summary(), DrawDefaultInspector);

        /// <summary>The summary, Open in Tweaks, then the fields when the user asked to see them.</summary>
        public static void Header(string module, string summary, System.Func<bool> drawFields)
        {
            if (!string.IsNullOrEmpty(summary))
                EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
            if (GUILayout.Button(new GUIContent("Open in Tweaks", $"All of {module}'s settings in one place, with Save and Revert."),
                    GUILayout.Height(26)))
                LooksmithTweaksPanel.Focus(module);
            if (LooksmithUserSettings.instance.showAllFields)
            {
                EditorGUILayout.Space(4);
                drawFields?.Invoke();
            }
            else
                EditorGUILayout.LabelField("You'll find its settings in Tweaks. To show them here as well, turn on Show all fields in the Inspector in Project Settings > Looksmith.",
                    EditorStyles.wordWrappedMiniLabel);
        }
    }

    [CustomEditor(typeof(LooksmithSky))]
    sealed class LooksmithSkyInspector : LooksmithTweaksInspector
    {
        protected override string module => "Sky & Time";

        protected override string Summary()
        {
            var sky = (LooksmithSky)target;
            return sky.preset != null ? $"Current sky: {sky.preset.name.Replace(" Sky", "")}." : "No sky preset picked yet.";
        }
    }

    [CustomEditor(typeof(LooksmithSkyPreset))]
    sealed class LooksmithSkyPresetInspector : LooksmithTweaksInspector
    {
        protected override string module => "Sky & Time";

        protected override string Summary()
        {
            var p = (LooksmithSkyPreset)target;
            bool shipped = LooksmithTweaks.IsShipped(AssetDatabase.GetAssetPath(p));
            return $"The sun at {p.sunElevation:0}°, {p.cloudCover:P0} cloud and the moon at phase {p.moonPhase:0.##}." +
                   (shipped ? " This preset comes with Looksmith, so saving a change in Tweaks makes your own copy." : "");
        }
    }

    [CustomEditor(typeof(LooksmithWind))]
    sealed class LooksmithWindInspector : LooksmithTweaksInspector
    {
        protected override string module => "Wind";

        protected override string Summary()
        {
            var w = (LooksmithWind)target;
            return $"Wind strength {w.strength:0.##}, blowing from {w.direction:0}°, gusts {w.gustiness:0.##}.";
        }
    }

    [CustomEditor(typeof(LooksmithLookVolume))]
    sealed class LooksmithLookVolumeInspector : LooksmithTweaksInspector
    {
        protected override string module => "Look";

        protected override string Summary()
        {
            var v = (LooksmithLookVolume)target;
            var profile = v.TryGetComponent(out Volume volume) ? volume.sharedProfile : null;
            return profile != null ? $"Using the {profile.name.Replace(" Look", "")} look. You can switch looks in Hub > Looks." : "No look picked yet. Choose one in Hub > Looks.";
        }
    }
}
