using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The shipped sky moods (one definition each, written into Sky Preset assets) and the Looksmith Sky creation menu.
    /// </summary>
    public static class LooksmithSkyPresets
    {
        public const string Folder = "Assets/PaleFernStudio/Looksmith/Sky";
        public const string PresetFolder = Folder + "/Presets";
        public const string MaterialPath = Folder + "/Looksmith Sky.mat";

        public sealed class Def
        {
            public string name, description;
            public string zenith, horizon, ground, glow = "#FFFFFF";
            public float curve = 0.45f, groundBlend = 0.12f, glowStrength, exposure = 1f;
            public float sunEl = 40f, sunAz = 150f, sunI = 1.2f, sunSize = 1.2f, sunHalo = 0.6f, shadow = 1f, keyTint = 1f;
            public string sun = "#FFF6E8";
            public float moonEl = -30f, moonAz = 200f, moonI = 0.3f, moonSize = 2.2f, phase = 0.8f;
            public string moon = "#C0D0FF";
            public float stars, starDensity = 0.6f, twinkle = 0.5f, milky = 0.4f;
            public float cover = 0.3f, opacity = 0.9f, cloudScale = 0.3f, cloudSpeed = 0.008f, softness = 0.08f;
            public int steps = 3;
            public string cloudLit = "#FFFFFF", cloudShade = "#B6C2D4";
            public string ambSky, ambEq, ambGround;
            public float ambI = 1f;
            public bool fog;
            public string fogColor = "#B8BEC4";
            public float fogDensity = 0.01f, reflection = 1f;
        }

        public static readonly Def[] All =
        {
            new Def
            {
                name = "Morning", description = "Fresh, clear and bright: the sun low in the east, a few soft clouds.",
                zenith = "#5C98D6", horizon = "#CFE3EE", ground = "#8E9C8C", glow = "#FFD9A8", glowStrength = 0.4f,
                sunEl = 18f, sunAz = 100f, sun = "#FFE2B8", sunI = 1.7f, sunHalo = 0.8f,
                cover = 0.32f, cloudLit = "#FFFFFF", cloudShade = "#B9C6D8",
                ambSky = "#86B0DA", ambEq = "#A8B8B0", ambGround = "#5E5A48", ambI = 0.8f,
            },
            new Def
            {
                name = "Noon", description = "High sun, deep blue sky, crisp shadows.",
                zenith = "#468BD2", horizon = "#BEDCEF", ground = "#90A08A", glow = "#FFFFFF", glowStrength = 0.08f,
                sunEl = 58f, sunAz = 165f, sun = "#FFF4E2", sunI = 1.9f, sunHalo = 0.45f,
                cover = 0.28f, cloudShade = "#B3C2D5",
                ambSky = "#7FAEDC", ambEq = "#A6BDC6", ambGround = "#6A644C", ambI = 0.82f,
            },
            new Def
            {
                name = "Golden Hour", description = "Warm, long light an hour before sunset.",
                zenith = "#3F6DB4", horizon = "#F6C88E", ground = "#9A8064", glow = "#FF9E50", glowStrength = 0.8f,
                sunEl = 21f, sunAz = 238f, sun = "#FFBE7A", sunI = 2.1f, sunHalo = 1.1f,
                cover = 0.38f, cloudLit = "#FFDAA8", cloudShade = "#9890B0",
                ambSky = "#6E8EC6", ambEq = "#B8946E", ambGround = "#4A3A30", ambI = 0.72f,
            },
            new Def
            {
                name = "Sunset", description = "The sun on the horizon, orange glow, violet shadows, the first star.",
                zenith = "#3B4C8A", horizon = "#FF9A5A", ground = "#6C4C52", glow = "#FF7838", glowStrength = 0.9f, curve = 0.55f,
                sunEl = 2f, sunAz = 270f, sun = "#FF9450", sunI = 0.9f, sunHalo = 1.5f, sunSize = 1.6f, keyTint = 0.6f,
                moonEl = 12f, moonAz = 110f, moonI = 0.1f, phase = 0.55f,
                stars = 0.12f, cover = 0.48f, cloudLit = "#FFB07A", cloudShade = "#6A5A86",
                ambSky = "#6B6FA8", ambEq = "#E08A64", ambGround = "#4A3840",
            },
            new Def
            {
                name = "Dusk", description = "Blue hour: the sun just gone, the moon up, the first stars.",
                zenith = "#1C2858", horizon = "#7A6A9A", ground = "#2C283E", glow = "#C46868", glowStrength = 0.45f,
                sunEl = -6f, sunAz = 280f, sun = "#FF9A70", sunI = 0f, keyTint = 0.2f,
                moonEl = 22f, moonAz = 120f, moon = "#C8D4FF", moonI = 0.22f, phase = 0.2f,
                stars = 0.45f, cover = 0.3f, cloudLit = "#8A7AA8", cloudShade = "#38385A",
                ambSky = "#3C4C7C", ambEq = "#5A4E70", ambGround = "#221C2C",
            },
            new Def
            {
                name = "Cozy Night", description = "A clear, friendly night: a crescent moon, many stars, deep blue.",
                zenith = "#0D1636", horizon = "#2A3A66", ground = "#151A28", glow = "#3A4A7A", glowStrength = 0.2f,
                sunEl = -30f, sunAz = 300f, sunI = 0f, keyTint = 0.15f,
                moonEl = 42f, moonAz = 200f, moon = "#AFC4FF", moonI = 0.3f, phase = 0.2f, moonSize = 2.4f,
                stars = 1f, starDensity = 0.75f, milky = 0.6f, cover = 0.14f, cloudLit = "#4A5A8A", cloudShade = "#1A2240",
                ambSky = "#2A3A6A", ambEq = "#25304E", ambGround = "#13151F", ambI = 1.1f, reflection = 0.8f,
            },
            new Def
            {
                name = "Scary Night", description = "A sickly thin moon behind heavy cloud, cold fog, almost no light.",
                zenith = "#05080B", horizon = "#1B2521", ground = "#0A0E0C", glow = "#2A3A30", glowStrength = 0.25f,
                sunEl = -35f, sunAz = 300f, sunI = 0f, exposure = 0.9f, keyTint = 0.1f,
                moonEl = 24f, moonAz = 230f, moon = "#B6CEBE", moonI = 0.13f, phase = 0.16f, moonSize = 2.6f,
                stars = 0.25f, starDensity = 0.4f, twinkle = 0.8f, milky = 0f,
                cover = 0.62f, opacity = 0.92f, cloudLit = "#2A3430", cloudShade = "#0B0F0F", softness = 0.15f, steps = 0,
                ambSky = "#18221E", ambEq = "#131917", ambGround = "#08090A", ambI = 0.8f,
                fog = true, fogColor = "#1E2A25", fogDensity = 0.018f, reflection = 0.5f,
            },
            new Def
            {
                name = "Overcast", description = "Soft grey daylight, no hard shadows, light mist.",
                zenith = "#8C97A3", horizon = "#C4C9CE", ground = "#7E8278", glowStrength = 0f,
                sunEl = 35f, sunAz = 150f, sun = "#E6E8EA", sunI = 0.6f, sunHalo = 0f, shadow = 0.4f, sunSize = 0.2f,
                cover = 1f, opacity = 1f, cloudLit = "#D6D9DC", cloudShade = "#8C929B", softness = 0.2f,
                ambSky = "#A8B0B8", ambEq = "#A4A8A8", ambGround = "#6E6E64", ambI = 1.25f,
                fog = true, fogColor = "#B8BEC2", fogDensity = 0.008f, reflection = 0.7f,
            },
            new Def
            {
                name = "Foggy Dawn", description = "Early light through mist: pastel sky, soft shadows.",
                zenith = "#8EA1BB", horizon = "#E8D8CC", ground = "#A8A49A", glow = "#FFD2B0", glowStrength = 0.5f, curve = 0.7f,
                sunEl = 6f, sunAz = 95f, sun = "#FFDCC0", sunI = 0.8f, sunHalo = 1f, shadow = 0.6f,
                cover = 0.2f, cloudLit = "#FFEEDD", cloudShade = "#C2C2CC",
                ambSky = "#A8B4C8", ambEq = "#D8CABE", ambGround = "#7A766C",
                fog = true, fogColor = "#D8D2CC", fogDensity = 0.02f,
            },
        };

        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static string PresetPath(string name) => $"{PresetFolder}/{name} Sky.asset";

        public static LooksmithSkyPreset Load(string name) => AssetDatabase.LoadAssetAtPath<LooksmithSkyPreset>(PresetPath(name));

        /// <summary>Writes a definition into a preset.</summary>
        public static void Apply(LooksmithSkyPreset p, Def d)
        {
            p.zenith = C(d.zenith); p.horizon = C(d.horizon); p.ground = C(d.ground); p.glow = C(d.glow);
            p.horizonCurve = d.curve; p.groundBlend = d.groundBlend; p.glowStrength = d.glowStrength; p.exposure = d.exposure;
            p.sunElevation = d.sunEl; p.sunAzimuth = d.sunAz; p.sunColor = C(d.sun); p.sunIntensity = d.sunI; p.sunSize = d.sunSize;
            p.sunHalo = d.sunHalo; p.shadowStrength = d.shadow; p.lookKeyTint = d.keyTint;
            p.moonElevation = d.moonEl; p.moonAzimuth = d.moonAz; p.moonColor = C(d.moon); p.moonIntensity = d.moonI;
            p.moonSize = d.moonSize; p.moonPhase = d.phase;
            p.starBrightness = d.stars; p.starDensity = d.starDensity; p.twinkle = d.twinkle; p.milkyWay = d.milky;
            p.cloudCover = d.cover; p.cloudOpacity = d.opacity; p.cloudScale = d.cloudScale; p.cloudSpeed = d.cloudSpeed;
            p.cloudSoftness = d.softness; p.cloudSteps = d.steps; p.cloudLit = C(d.cloudLit); p.cloudShade = C(d.cloudShade);
            p.ambientSky = C(d.ambSky); p.ambientEquator = C(d.ambEq); p.ambientGround = C(d.ambGround); p.ambientIntensity = d.ambI;
            p.fog = d.fog; p.fogColor = C(d.fogColor); p.fogDensity = d.fogDensity; p.reflectionIntensity = d.reflection;
        }

        /// <summary>Creates or refreshes the shipped presets and the sky material.</summary>
        public static List<string> WriteShipped()
        {
            Directory.CreateDirectory(PresetFolder);
            AssetDatabase.Refresh();
            var written = new List<string>();
            foreach (var d in All)
            {
                var path = PresetPath(d.name);
                var p = AssetDatabase.LoadAssetAtPath<LooksmithSkyPreset>(path);
                if (p == null)
                {
                    p = ScriptableObject.CreateInstance<LooksmithSkyPreset>();
                    AssetDatabase.CreateAsset(p, path);
                }
                Apply(p, d);
                EditorUtility.SetDirty(p);
                AssetDatabase.SaveAssetIfDirty(p);
                written.Add(path);
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PaleFernStudio/Looksmith/Shaders/Sky/LooksmithSky.shader");
                AssetDatabase.CreateAsset(new Material(shader), MaterialPath);
                written.Add(MaterialPath);
            }
            return written;
        }

        [MenuItem("GameObject/Looksmith/Sky", false, 11)]
        static void CreateSky()
        {
            if (LooksmithFind.All<LooksmithSky>(FindObjectsInactive.Include).Length > 0)
            {
                EditorUtility.DisplayDialog("Looksmith", "This scene already has a Looksmith Sky.", "OK");
                return;
            }
            var go = new GameObject("Looksmith Sky");
            Undo.RegisterCreatedObjectUndo(go, "Looksmith: add sky");
            var sky = Undo.AddComponent<LooksmithSky>(go);
            sky.skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            sky.preset = Load("Noon") ?? AssetDatabase.FindAssets("t:LooksmithSkyPreset").Select(g => AssetDatabase.LoadAssetAtPath<LooksmithSkyPreset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            sky.sun = RenderSettings.sun != null ? RenderSettings.sun
                : LooksmithFind.All<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            if (sky.sun != null)
                Undo.RecordObject(sky.sun.transform, "Looksmith: add sky");
            Selection.activeGameObject = go;
        }
    }
}
