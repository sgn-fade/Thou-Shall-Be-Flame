using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Which edition is installed. Lite is Pro with the Pro files left out, so Pro is detected by its runtime assembly
    /// being present. Lite's own panels show only what Lite has; the Hub's Pro tab is the one place that shows Pro, and
    /// its buttons open the store only when clicked (never on their own, never on import).
    /// </summary>
    public static class LooksmithEdition
    {
        public const string ProAssembly = "PaleFernStudio.Looksmith.Runtime.Pro";

        /// <summary>
        /// The publisher's page on the Asset Store (Pale Fern Studio): it lists Looksmith Pro once Pro is live, and is
        /// there before that, so the button never leads to a page that doesn't exist.
        /// </summary>
        public const string StoreUrl = "https://assetstore.unity.com/publishers/143800";

        static bool? s_ProInstalled;

        /// <summary>True when the Pro files are installed.</summary>
        public static bool proInstalled => s_ProInstalled ??=
            TypeCache.GetTypesDerivedFrom<Component>().Any(t => t.Assembly.GetName().Name == ProAssembly);

        /// <summary>Dev and test switch: shows the Lite UI in a Pro install. Not saved anywhere.</summary>
        public static bool simulateLite { get; set; }

        /// <summary>True when Pro features are available.</summary>
        public static bool isPro => proInstalled && !simulateLite;

        /// <summary>Opens the Pro store page. Only ever called from a button the user clicked.</summary>
        public static void OpenStore() => LooksmithLinks.Open(StoreUrl);

        /// <summary>Settings only Pro's passes read: Lite's inspectors leave them out, as they would do nothing there.</summary>
        static readonly (Type owner, string field)[] k_ProSettings =
        {
            (typeof(LooksmithLook), "lineColorSensitivity"), (typeof(LooksmithLook), "lineObjectEdges"),
            (typeof(LooksmithLook), "lineLayerMask"), (typeof(LooksmithLook), "outlineWidth"),
            (typeof(LooksmithStyle), "layers"),   // an Art Style's rendering-layer mask is drawn by a Pro pass
        };

        /// <summary>Whether this edition's inspectors show a setting of a Looksmith component.</summary>
        public static bool Shows(Type owner, string field) =>
            isPro || !k_ProSettings.Any(s => s.field == field && s.owner.IsAssignableFrom(owner));
    }

    /// <summary>
    /// Every link Looksmith opens goes through here: the store (a web address), or the manual for a LooksmithDocs section,
    /// which is the PDF in the package (nothing else is online).
    /// </summary>
    public static class LooksmithLinks
    {
        /// <summary>Opens a web address in the browser, or a file in its app. Tests replace it to see what opens, and when.</summary>
        public static Action<string> opener = target =>
        {
            if (IsWeb(target))
                Application.OpenURL(target);
            else
                EditorUtility.OpenWithDefaultApp(target);
        };

        static bool IsWeb(string target) => target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        public static void Open(string target)
        {
            if (string.IsNullOrEmpty(target))
                return;
            if (IsWeb(target))
            {
                opener?.Invoke(target);
                return;
            }
            var manual = ManualPath();
            if (manual != null)
                opener?.Invoke(manual);
            else
                Debug.LogWarning("Looksmith: the manual (Documentation/Looksmith Manual.pdf) isn't in the project. Import it again from the package.");
        }

        /// <summary>The manual in the package, wherever the Looksmith folder was moved (null if it was deleted).</summary>
        public static string ManualPath()
        {
            var path = AssetDatabase.FindAssets("Looksmith Manual").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            return path == null ? null : Path.GetFullPath(path);
        }

        /// <summary>What a link opens, for its tooltip.</summary>
        public static string Describe(string target) =>
            string.IsNullOrEmpty(target) ? "" : IsWeb(target) ? target : "Opens " + LooksmithDocs.Describe(target) + " (the PDF in the package).";
    }
}
