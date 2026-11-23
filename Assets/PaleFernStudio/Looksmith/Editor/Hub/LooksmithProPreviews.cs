using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The pictures Lite shows of what Pro adds: each Pro look's gallery thumbnail, its half of a before-and-after (one
    /// view of the demo village, plain URP and the look) and the Pro tab's card pictures. They ship with both editions
    /// (Pro never shows them) and are found by name, so they work wherever the Looksmith folder sits.
    /// </summary>
    public static class LooksmithProPreviews
    {
        /// <summary>The before half of every before-and-after: the same view with Looksmith bypassed.</summary>
        public const string Plain = "Plain URP";

        const string ThumbPrefix = "LooksmithThumb ", ComparePrefix = "LooksmithCompare ", CardPrefix = "LooksmithCard ";
        static Dictionary<string, string> s_Paths;   // a path cache only: a miss or a moved file looks again

        public static Texture2D Thumbnail(string look) => Load(ThumbPrefix + look);

        public static Texture2D Compare(string lookOrPlain) => Load(ComparePrefix + lookOrPlain);

        public static Texture2D Card(string name) => Load(CardPrefix + name);

        static Texture2D Load(string assetName)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (s_Paths == null || attempt == 1)
                    s_Paths = Index();
                if (s_Paths.TryGetValue(assetName, out var path))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                        return tex;
                }
            }
            return null;
        }

        static Dictionary<string, string> Index()
        {
            var map = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("Looksmith t:Texture2D"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith(ThumbPrefix) || name.StartsWith(ComparePrefix) || name.StartsWith(CardPrefix))
                    map[name] = path;
            }
            return map;
        }
    }
}
