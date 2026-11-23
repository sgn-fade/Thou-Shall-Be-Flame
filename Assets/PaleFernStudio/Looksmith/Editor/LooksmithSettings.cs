using System;
using System.IO;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    public enum LooksmithQualityTier { Low, Medium, High }

    /// <summary>
    /// Project-wide Looksmith settings (ProjectSettings/LooksmithSettings.json). The file only exists once the user
    /// changes something; until then defaults apply (P1: importing writes nothing).
    /// </summary>
    [Serializable]
    public sealed class LooksmithProjectSettings
    {
        public const string FilePath = "ProjectSettings/LooksmithSettings.json";

        public bool autoOpenHub = true;
        public int backupRetention = 20;
        public bool disabledInProject;
        public LooksmithQualityTier desktopTier = LooksmithQualityTier.High;
        public LooksmithQualityTier mobileTier = LooksmithQualityTier.Low;
        public LooksmithQualityTier xrTier = LooksmithQualityTier.Low;

        static LooksmithProjectSettings s_Instance;

        public static LooksmithProjectSettings instance => s_Instance ??= Load();

        static LooksmithProjectSettings Load()
        {
            var s = new LooksmithProjectSettings();
            try
            {
                if (File.Exists(FilePath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Looksmith: could not read " + FilePath + ": " + e.Message);
            }
            return s;
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        /// <summary>Re-reads the file on next access.</summary>
        public static void Reload() => s_Instance = null;
    }

    /// <summary>Per-user, per-project state (UserSettings/Looksmith.json): first-run marker, Bypass.</summary>
    [Serializable]
    public sealed class LooksmithUserSettings
    {
        public const string FilePath = "UserSettings/Looksmith.json";

        public bool hubShown;
        public bool bypass;
        public string lastScanUtc = "";
        /// <summary>Inspectors of Looksmith components and assets show their fields (off: a summary and Open in Tweaks).</summary>
        public bool showAllFields;
        /// <summary>Tweaks saves into Looksmith's shipped assets instead of the project's own copies (for Looksmith's publisher).</summary>
        public bool editShippedInPlace;
        /// <summary>Lite: the Hub's Pro tab (and the Looks tab's line pointing to it) is hidden.</summary>
        public bool hideProTab;

        static LooksmithUserSettings s_Instance;

        public static LooksmithUserSettings instance => s_Instance ??= Load();

        static LooksmithUserSettings Load()
        {
            var s = new LooksmithUserSettings();
            try
            {
                if (File.Exists(FilePath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Looksmith: could not read " + FilePath + ": " + e.Message);
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        /// <summary>Re-reads the file on next access.</summary>
        public static void Reload() => s_Instance = null;

        /// <summary>Switches Bypass (Looksmith off, P7) for this user and remembers it.</summary>
        public static void SetBypass(bool on)
        {
            LooksmithRuntime.Bypass = on;
            instance.bypass = on;
            instance.Save();
            UnityEditor.SceneView.RepaintAll();
        }
    }
}
