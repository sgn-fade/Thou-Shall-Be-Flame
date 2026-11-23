using System;
using UnityEditor;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Play Mode rules for Looksmith's editor tools: picking a look works (for the session, like any change made while
    /// playing); everything that writes files (Apply, Restyle, Fix, Revert) waits until Play Mode ends.
    /// </summary>
    public static class LooksmithPlayMode
    {
        public const string Note = "Play Mode: looks you pick last until you stop playing. Setup, Restyle, fixes and Revert work outside Play Mode.";
        public const string WritesDisabled = "Not available in Play Mode: Looksmith only changes files outside Play Mode.";

        /// <summary>Test hook: replaces the editor's Play Mode state. Null uses the real one.</summary>
        public static Func<bool> overrideForTests;

        /// <summary>True while playing or about to play.</summary>
        public static bool active => overrideForTests?.Invoke() ?? EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
