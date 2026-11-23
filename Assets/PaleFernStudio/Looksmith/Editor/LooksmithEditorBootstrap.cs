using System;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Editor start-up: restores Bypass and opens the Hub once per user and project (P1: the only thing that
    /// runs automatically). Never opens a window in batchmode. Set the environment variable
    /// LOOKSMITH_FIRSTRUN_SIMULATE=1 to log the decision in batchmode instead (used by the import integration check).
    /// </summary>
    [InitializeOnLoad]
    public static class LooksmithEditorBootstrap
    {
        public const string SimulateEnv = "LOOKSMITH_FIRSTRUN_SIMULATE";
        public const string LogOpen = "LOOKSMITH_FIRSTRUN: opening Hub (first run)";
        public const string LogSimulated = "LOOKSMITH_FIRSTRUN: would open Hub (first run, batchmode simulation)";
        public const string LogAlreadyShown = "LOOKSMITH_FIRSTRUN: Hub already shown for this user and project";

        static LooksmithEditorBootstrap()
        {
            // Batchmode simulation decides right away: a -quit run may end before delayCall fires.
            if (Application.isBatchMode && Environment.GetEnvironmentVariable(SimulateEnv) == "1")
            {
                OnFirstFrame();
                return;
            }
            EditorApplication.delayCall -= OnFirstFrame;
            EditorApplication.delayCall += OnFirstFrame;
        }

        static void OnFirstFrame()
        {
            if (LooksmithProjectSettings.instance.disabledInProject)
                return;
            LooksmithRuntime.Bypass = LooksmithUserSettings.instance.bypass;
            Decide(Application.isBatchMode, Environment.GetEnvironmentVariable(SimulateEnv) == "1", openWindow: () => LooksmithHubWindow.Open());
        }

        /// <summary>
        /// The first-run decision, separated from the window for tests. Returns what happened.
        /// </summary>
        public static string Decide(bool batchMode, bool simulate, Action openWindow)
        {
            var user = LooksmithUserSettings.instance;
            if (!LooksmithProjectSettings.instance.autoOpenHub)
                return null;
            if (batchMode && !simulate)
                return null;
            if (user.hubShown)
            {
                if (simulate)
                    Debug.Log(LogAlreadyShown);
                return LogAlreadyShown;
            }
            user.hubShown = true;
            user.Save();
            if (batchMode)
            {
                Debug.Log(LogSimulated);
                return LogSimulated;
            }
            openWindow?.Invoke();
            return LogOpen;
        }
    }
}
