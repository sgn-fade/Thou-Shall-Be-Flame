using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>GameObject > Looksmith > Mark as Water: a Looksmith Water Plane on the selected water (a plain, undoable scene edit).</summary>
    static class LooksmithWaterPlaneMenu
    {
        [MenuItem("GameObject/Looksmith/Mark as Water", true)]
        static bool CanMark() => Selection.gameObjects.Any(g => g.scene.IsValid() && g.GetComponent<LooksmithWaterPlane>() == null);

        [MenuItem("GameObject/Looksmith/Mark as Water", false, 16)]
        static void Mark()
        {
            foreach (var go in Selection.gameObjects.Where(g => g.scene.IsValid() && g.GetComponent<LooksmithWaterPlane>() == null))
            {
                Undo.AddComponent<LooksmithWaterPlane>(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }
    }
}
