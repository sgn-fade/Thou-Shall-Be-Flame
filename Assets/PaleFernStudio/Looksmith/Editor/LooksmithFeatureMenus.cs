using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>GameObject > Looksmith > Wind (plain, undoable scene edits, like any GameObject menu).</summary>
    static class LooksmithFeatureMenus
    {
        [MenuItem("GameObject/Looksmith/Wind", false, 17)]
        static void CreateWind()
        {
            if (LooksmithFind.All<LooksmithWind>(FindObjectsInactive.Include).Length > 0)
            {
                EditorUtility.DisplayDialog("Looksmith", "This scene already has a Looksmith Wind.", "OK");
                return;
            }
            var go = new GameObject("Looksmith Wind");
            Undo.RegisterCreatedObjectUndo(go, "Looksmith: add wind");
            Undo.AddComponent<LooksmithWind>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
