using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Project Settings > Looksmith. The settings file is only written when something here changes.</summary>
    static class LooksmithSettingsProvider
    {
        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider("Project/Looksmith", SettingsScope.Project)
        {
            label = "Looksmith",
            keywords = new[] { "Looksmith", "Look", "Bypass", "Backup" },
            guiHandler = _ =>
            {
                var s = LooksmithProjectSettings.instance;
                EditorGUI.BeginChangeCheck();
                LooksmithLitGUI.ManualLink("Project Settings", LooksmithDocs.ProjectSettings);
                // (the per-platform quality tiers are stored but not read by anything yet, so they aren't shown)
                EditorGUILayout.LabelField("Editor", EditorStyles.boldLabel);
                s.autoOpenHub = EditorGUILayout.Toggle(new GUIContent("Open the Hub once after import", "Per user and project; never in batchmode."), s.autoOpenHub);
                s.backupRetention = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Backups to keep", "Older backups are offered for deletion in Hub > Changes."), s.backupRetention), 1, 500);
                s.disabledInProject = EditorGUILayout.Toggle(new GUIContent("Disable Looksmith in this project", "Skips every Looksmith pass and start-up hook. Looksmith materials render as plain PBR."), s.disabledInProject);
                if (EditorGUI.EndChangeCheck())
                {
                    s.Save();
                    LooksmithRuntime.Bypass = s.disabledInProject || LooksmithUserSettings.instance.bypass;
                }
                EditorGUILayout.Space();
                bool bypass = EditorGUILayout.Toggle(new GUIContent("Bypass (this user)", "Turns Looksmith off for you, so you can compare: Looksmith materials draw as standard PBR and Looksmith's effects are skipped."), LooksmithRuntime.Bypass);
                if (bypass != LooksmithRuntime.Bypass)
                {
                    LooksmithRuntime.Bypass = bypass;
                    LooksmithUserSettings.instance.bypass = bypass;
                    LooksmithUserSettings.instance.Save();
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Tweaks (this user)", EditorStyles.boldLabel);
                var u = LooksmithUserSettings.instance;
                EditorGUI.BeginChangeCheck();
                u.showAllFields = EditorGUILayout.Toggle(new GUIContent("Show all fields in the Inspector",
                    "When this is off, Looksmith components and assets only show a short summary and an Open in Tweaks button. Turn it on to see all their fields too, which helps with scripting and debugging. Edits made there don't go through Tweaks."),
                    u.showAllFields);
                u.editShippedInPlace = EditorGUILayout.Toggle(new GUIContent("Save into shipped assets",
                    "Leave this off. Then saving a change to a look, sky preset or material that comes with Looksmith makes your own copy, so updating Looksmith never overwrites your work. Turn it on only if you're working on Looksmith itself."),
                    u.editShippedInPlace);
                if (EditorGUI.EndChangeCheck())
                {
                    u.Save();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
                if (!LooksmithEdition.isPro)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Hub (this user)", EditorStyles.boldLabel);
                    bool show = EditorGUILayout.Toggle(new GUIContent("Show the Pro tab",
                        "The Hub's tab about Looksmith Pro, with a before and after of each Pro look. Hiding it also hides the line under the Looks gallery that points to it."),
                        !u.hideProTab);
                    if (show == u.hideProTab)
                        LooksmithProCards.SetTabHidden(!show);
                }
            },
        };
    }
}
