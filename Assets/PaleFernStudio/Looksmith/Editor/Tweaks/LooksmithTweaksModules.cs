using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// The Tweaks modules every edition has: the Look (how the world is drawn), Sky &amp; Time and Wind. Each leads with the
    /// few knobs that matter, named for what they do; Advanced shows every setting. The Pro modules (Atmosphere, Water,
    /// Grass) register from the Pro editor.
    /// </summary>
    public static class LooksmithTweaksModules
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            LooksmithTweaks.Register(new TweakModule
            {
                name = "Look", order = 0, addMenu = "GameObject/Looksmith/Look Volume",
                summary = "How everything is drawn: the light, the colours, the outlines and the art style. To switch to another look, use the Looks tab.",
                present = () => LookProfile() != null,
                essentials = LookKnobs,
                suggestions = LookSuggestions,
                objects = () => LookProfile() is VolumeProfile p ? p.components.Where(c => c != null).Cast<Object>().ToList() : new List<Object>(),
            });
            LooksmithTweaks.Register(new TweakModule
            {
                name = "Sky & Time", order = 10, addMenu = "GameObject/Looksmith/Sky",
                summary = "The sun, the moon, the clouds and the time of day.",
                present = () => Sky() != null,
                essentials = SkyKnobs,
                objects = () => Sky() is LooksmithSky s ? new List<Object> { s, s.preset }.Where(o => o != null).ToList() : new List<Object>(),
            });
            LooksmithTweaks.Register(new TweakModule
            {
                name = "Wind", order = 60, addMenu = "GameObject/Looksmith/Wind",
                summary = "The breeze that sways grass, leaves and trees.",
                present = () => Object.FindAnyObjectByType<LooksmithWind>() != null,
                essentials = WindKnobs,
                objects = () => LooksmithFind.All<LooksmithWind>().Cast<Object>().ToList(),
            });

            LooksmithTweaks.RegisterDefaults(typeof(LooksmithSkyPreset), SkyPresetDefaults);
            LooksmithTweaks.RegisterDefaults(typeof(VolumeComponent), LookComponentDefaults);
            // a sky caches its preset: re-read it when the preset or the sky changes
            LooksmithTweaks.RegisterAfterChange(typeof(LooksmithSkyPreset), preset =>
            {
                foreach (var sky in LooksmithFind.All<LooksmithSky>())
                    if (sky.preset == preset)
                        sky.TransitionTo(sky.preset, 0f);
            });
            LooksmithTweaks.RegisterAfterChange(typeof(LooksmithSky), o =>
            {
                var sky = (LooksmithSky)o;
                if (sky.preset != null)
                    sky.TransitionTo(sky.preset, 0f);
            });
        }

        // ------------------------------------------------------------------ Look

        /// <summary>The profile the scene's Look Volume shows (in Play Mode, the demo's copy standing in for it).</summary>
        public static VolumeProfile LookProfile()
        {
            foreach (var v in LooksmithFind.All<LooksmithLookVolume>())
                if (v.isActiveAndEnabled && v.TryGetComponent(out Volume volume) && volume.isGlobal && volume.sharedProfile != null)
                    return volume.sharedProfile;
            return null;
        }

        /// <summary>More of the Look's essentials, from its Pro parts (the Yarn's stitch size and fuzz). Set by the Pro editor.</summary>
        public static System.Func<VolumeProfile, IEnumerable<TweakKnob>> ProLookKnobs;

        static List<TweakKnob> LookKnobs()
        {
            var knobs = new List<TweakKnob>();
            var profile = LookProfile();
            if (profile == null)
                return knobs;
            if (profile.TryGet(out LooksmithLook look))
            {
                TweakKnob K(string param, string label, string hint, float min, float max) =>
                    new TweakKnob { target = look, property = param + ".m_Value", label = label, hint = hint, min = min, max = max };
                knobs.Add(K("softness", "Light softness", "Low gives hard, cel-shaded bands. High gives soft, painterly light.", 0f, 1f));
                knobs.Add(K("contrast", "Light contrast", "How strong the difference between light and shadow is.", 0f, 1f));
                knobs.Add(K("warmth", "Warmth", "Low makes the light cool, high makes it warm. The shadows lean the other way.", 0f, 1f));
                knobs.Add(K("ambient", "Shadow brightness", "How bright things are in shadow.", 0f, 2f));
                knobs.Add(K("rim", "Rim light", "A thin light along the edges that helps shapes stand out.", 0f, 1f));
                knobs.Add(K("saturation", "Colour strength", "How colourful everything is.", 0f, 2f));
                knobs.Add(K("haze", "Distance haze", "How much faraway things fade into the air.", 0f, 1f));
                knobs.Add(K("lineWeight", "Line weight", "How thick the ink outlines are. Zero turns them off.", 0f, 4f));
                knobs.Add(K("grain", "Film grain", "A light film grain over the picture.", 0f, 1f));
            }
            if (profile.TryGet(out ColorAdjustments adjust))
                knobs.Add(new TweakKnob { target = adjust, property = "postExposure.m_Value", label = "Exposure", hint = "Overall brightness. Each step of 1 doubles or halves it.", min = -2f, max = 2f });
            if (ProLookKnobs != null)
                knobs.AddRange(ProLookKnobs(profile));
            return knobs;
        }

        /// <summary>The Look Advisor's suggestions for what the camera sees now, each carried out as drafts.</summary>
        static List<TweakSuggestion> LookSuggestions()
        {
            var list = new List<TweakSuggestion>();
            var profile = LookProfile();
            var camera = LooksmithLookFit.ReferenceCamera();
            if (profile == null || camera == null)
                return list;
            var reading = LooksmithLookFit.Read(camera);
            foreach (var advice in LooksmithLookAdvisor.Advise(profile, reading))
            {
                // switches on the camera or the scene go through Hub > Home (with a backup): Tweaks carries out the Look's own
                if (advice.turnOffUnityFog || advice.turnOnPostProcessing || advice.changes.Count == 0)
                    continue;
                var a = advice;
                list.Add(new TweakSuggestion
                {
                    title = a.title, why = a.why, recommended = a.level == LooksmithAdviceLevel.Recommended,
                    apply = () => ApplyAdvice(profile, a),
                });
            }
            return list;
        }

        static void ApplyAdvice(VolumeProfile profile, LooksmithAdvice advice)
        {
            foreach (var c in advice.changes)
            {
                var p = LooksmithLookAdvisor.FindParameter(profile, c.component, c.parameter, out var owner);
                if (p == null)
                    continue;
                if (!owner.active)
                    LooksmithTweaks.Set(owner, "active", true, "Look", ObjectNames.NicifyVariableName(owner.GetType().Name.Replace("Looksmith", "")) + " (on)");
                object value;
                switch (p)
                {
                    case VolumeParameter<int> _: value = Mathf.RoundToInt(c.value); break;
                    case VolumeParameter<bool> _: value = c.value > 0.5f; break;
                    case VolumeParameter<Vector3> _ when c.isColor: value = (Vector4)c.color; break;
                    case VolumeParameter<float> _: value = c.value; break;
                    default: continue;
                }
                LooksmithTweaks.Set(owner, c.parameter + ".m_Value", value, "Look", ObjectNames.NicifyVariableName(c.parameter));
            }
        }

        /// <summary>
        /// A look component's values as the look it was made from ships. Null when that look isn't known: a blank component
        /// has every setting switched off, which is never what the look is meant to be (its default is then how it's saved).
        /// </summary>
        static Object LookComponentDefaults(Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            var profile = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path) as VolumeProfile;
            var lookName = ShippedName(profile);
            if (lookName == null)
                return null;
            var temp = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                LooksmithLooks.Populate(temp, lookName);
                var match = temp.components.FirstOrDefault(c => c != null && c.GetType() == o.GetType());
                return match != null ? Object.Instantiate(match) : null;
            }
            finally
            {
                foreach (var c in temp.components)
                    if (c != null)
                        Object.DestroyImmediate(c);
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>The shipped look a profile is, or was copied from ("Storybook Look (yours)").</summary>
        static string ShippedName(VolumeProfile profile)
        {
            if (profile == null)
                return null;
            var shipped = LooksmithLookGallery.ShippedLookName(profile);
            if (shipped != null)
                return shipped;
            var name = profile.name.Replace(" (yours)", "");
            if (name.EndsWith(" Look"))
                name = name.Substring(0, name.Length - " Look".Length);
            return LooksmithLooks.All.Any(d => d.name == name) || LooksmithLooks.FindVariant(name) != null ? name : null;
        }

        // ------------------------------------------------------------------ Sky & Time

        static LooksmithSky Sky() => LooksmithSky.active != null ? LooksmithSky.active : Object.FindAnyObjectByType<LooksmithSky>();

        static List<TweakKnob> SkyKnobs()
        {
            var knobs = new List<TweakKnob>();
            var sky = Sky();
            if (sky == null)
                return knobs;
            var presets = LooksmithSkyPresets.All.Select(d => LooksmithSkyPresets.Load(d.name)).Where(p => p != null).ToList();
            if (sky.preset != null && !presets.Contains(sky.preset))
                presets.Insert(0, sky.preset);
            knobs.Add(new TweakKnob
            {
                target = sky, property = "preset", label = "Time of day", hint = "Pick a sky: dawn, noon, golden hour, night and more.",
                choices = presets.Select(p => new KeyValuePair<string, object>(p.name.Replace(" Sky", ""), new ObjectRef(p))).ToList(),
                note = DayCyclePlaying() ? "The day cycle is running, so the sky keeps moving on its own." : null,
            });
            var p0 = sky.preset;
            if (p0 == null)
                return knobs;
            TweakKnob K(string field, string label, string hint, float min, float max) =>
                new TweakKnob { target = p0, property = field, label = label, hint = hint, min = min, max = max };
            knobs.Add(K("sunElevation", "Sun height", "Low gives long shadows and warm light. High puts the sun overhead.", -20f, 90f));
            knobs.Add(K("sunAzimuth", "Sun direction", "Which side of the scene the sun is on.", 0f, 360f));
            knobs.Add(K("sunIntensity", "Sunlight", "How bright the sunlight is, or the moonlight at night.", 0f, 3f));
            knobs.Add(K("cloudCover", "Clouds", "From a clear sky to fully overcast. The cloud shadows on the ground follow it.", 0f, 1f));
            knobs.Add(K("cloudSpeed", "Cloud speed", "How fast the clouds drift.", 0f, 0.2f));
            knobs.Add(K("ambientIntensity", "Ambient light", "How much the sky lights up the shadows.", 0f, 3f));
            knobs.Add(K("exposure", "Sky brightness", "How bright the sky itself looks.", 0.2f, 2f));
            knobs.Add(K("moonPhase", "Moon phase", "From a new moon to a full moon. You only see it while the moon is up.", 0f, 1f));
            knobs.Add(K("starBrightness", "Stars", "How bright the stars are at night.", 0f, 2f));
            return knobs;
        }

        // the Day Cycle is Pro: found by name, so Lite builds without it
        static readonly System.Type s_DayCycle = System.Type.GetType("PaleFernStudio.Looksmith.LooksmithDayCycle, PaleFernStudio.Looksmith.Runtime.Pro");

        static bool DayCyclePlaying() =>
            s_DayCycle != null && LooksmithFind.All(s_DayCycle).OfType<Behaviour>().Any(b => b.isActiveAndEnabled);

        static Object SkyPresetDefaults(Object o)
        {
            var name = o.name.Replace(" (yours)", "");
            if (name.EndsWith(" Sky"))
                name = name.Substring(0, name.Length - " Sky".Length);
            var preset = ScriptableObject.CreateInstance<LooksmithSkyPreset>();
            var def = LooksmithSkyPresets.All.FirstOrDefault(d => d.name == name);
            if (def != null)
                LooksmithSkyPresets.Apply(preset, def);
            return preset;
        }

        // ------------------------------------------------------------------ Wind

        static List<TweakKnob> WindKnobs()
        {
            var knobs = new List<TweakKnob>();
            var wind = Object.FindAnyObjectByType<LooksmithWind>();
            if (wind == null)
                return knobs;
            TweakKnob K(string field, string label, string hint, float min, float max) =>
                new TweakKnob { target = wind, property = field, label = label, hint = hint, min = min, max = max };
            knobs.Add(K("strength", "Wind strength", "From still air to a strong wind.", 0f, 3f));
            knobs.Add(K("direction", "Wind direction", "Which way the wind blows, in degrees.", 0f, 360f));
            knobs.Add(K("speed", "Sway speed", "How fast things sway.", 0.1f, 4f));
            knobs.Add(K("gustiness", "Gusts", "Low is a steady breeze. High brings gusts that roll through the grass in waves.", 0f, 1f));
            return knobs;
        }
    }
}
