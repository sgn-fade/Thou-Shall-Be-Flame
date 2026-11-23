using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>How a family of source shaders becomes a Looksmith shader (or back, for the inverse rules).</summary>
    public enum RestyleRecipe
    {
        Lit = 0,
        Unlit = 1,
        Particles = 2,
        Terrain = 3,
        ToUrpLit = 10,
        ToUrpUnlit = 11,
        ToUrpParticles = 12,
        ToUrpTerrain = 13,
    }

    /// <summary>One source property copied onto one target property ("Map manually…" and rule extras).</summary>
    [Serializable]
    public sealed class PropertyPair
    {
        public string source;
        public string target;

        public PropertyPair() { }
        public PropertyPair(string source, string target)
        {
            this.source = source;
            this.target = target;
        }
    }

    /// <summary>
    /// Everything a conversion needs, in a form a journaled change can carry (public fields, JsonUtility).
    /// Built from a rule asset, or from the low-poly heuristic for shaders no rule knows.
    /// </summary>
    [Serializable]
    public sealed class RestyleSpec
    {
        public string ruleName;
        public RestyleRecipe recipe;
        public string targetShaderGuid;
        public string targetShaderName;
        public float vertexColorAmount;
        public List<PropertyPair> pairs = new List<PropertyPair>();
        public bool twoSided;   // both faces drawn (the source culls nothing)

        public Shader ResolveTarget()
        {
            var path = string.IsNullOrEmpty(targetShaderGuid) ? null : AssetDatabase.GUIDToAssetPath(targetShaderGuid);
            var shader = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
            return shader != null ? shader : Shader.Find(targetShaderName);   // built-in shaders have no asset path
        }

        public static RestyleSpec For(Shader target, RestyleRecipe recipe, string ruleName, float vertexColorAmount = 0f,
            IEnumerable<PropertyPair> pairs = null, bool twoSided = false)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long _);
            return new RestyleSpec
            {
                ruleName = ruleName,
                recipe = recipe,
                targetShaderGuid = guid,
                targetShaderName = target.name,
                vertexColorAmount = vertexColorAmount,
                pairs = pairs?.Select(p => new PropertyPair(p.source, p.target)).ToList() ?? new List<PropertyPair>(),
                twoSided = twoSided,
            };
        }
    }

    /// <summary>
    /// A Restyle mapping rule: which shaders it converts, into what, and with which recipe. Looksmith ships rules for URP,
    /// Built-in, Autodesk Interactive and legacy shaders; "Map manually…" saves user rules next to your project
    /// (Assets/Looksmith Generated/Restyle Rules), which win over the shipped ones.
    /// </summary>
    public sealed class LooksmithRestyleRule : ScriptableObject
    {
        [Tooltip("Exact shader names this rule converts.")]
        public string[] sourceShaders = Array.Empty<string>();
        [Tooltip("The shader materials get (a Looksmith graph; a URP shader for the inverse rules).")]
        public Shader target;
        public RestyleRecipe recipe;
        [Tooltip("Vertex colour multiplies the base colour (low-poly packs that paint with vertex colours).")]
        [Range(0f, 1f)] public float vertexColorAmount;
        [Tooltip("Extra source → target property copies, applied after the recipe.")]
        public List<PropertyPair> pairs = new List<PropertyPair>();
        [Tooltip("Inverse rules turn Looksmith materials back into URP ones (Restyle > Convert to URP Lit).")]
        public bool inverse;
        [Tooltip("Higher wins when two rules match the same shader. Your rules are 100, shipped rules 0.")]
        public int priority;
        [Tooltip("Also map the base colour map, colour and normal map by the names the inspector shows (for Shader Graphs whose properties keep generated names like Texture2D_2A1A8040).")]
        public bool pairsFromInspectorNames;
        [Tooltip("The source draws both faces (leaves, cloth, cards): the converted material does too.")]
        public bool twoSided;

        public bool Matches(Shader shader) => shader != null && target != null && sourceShaders.Contains(shader.name);

        public RestyleSpec ToSpec() => ToSpec(null);

        /// <summary>
        /// The conversion for one source shader (inspector-name pairs are read from it when the rule asks; null when they
        /// find no base colour map, so a different shader that happens to share the name isn't converted blind).
        /// </summary>
        public RestyleSpec ToSpec(Shader source)
        {
            var all = new List<PropertyPair>(pairs);
            if (pairsFromInspectorNames && source != null)
            {
                var named = LooksmithRestyle.PairsFromDisplayNames(source);
                if (!named.Any(p => p.target == "_BaseMap"))
                    return null;
                all.InsertRange(0, named);
            }
            return RestyleSpec.For(target, recipe, name, vertexColorAmount, all, twoSided);
        }

        /// <summary>Every rule in the project, highest priority first.</summary>
        public static List<LooksmithRestyleRule> All() =>
            AssetDatabase.FindAssets("t:" + nameof(LooksmithRestyleRule))
                .Select(g => AssetDatabase.LoadAssetAtPath<LooksmithRestyleRule>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null && r.target != null)
                .OrderByDescending(r => r.priority).ThenBy(r => r.name, StringComparer.Ordinal)
                .ToList();
    }
}
