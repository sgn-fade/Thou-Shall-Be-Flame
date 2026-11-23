using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>What the Look Advisor reads: the scene's light, fog and sky, how big the frame is shown, and the frame.</summary>
    public struct LooksmithSceneReading
    {
        /// <summary>A directional light lights the scene (URP's main light: RenderSettings.sun or the brightest one).</summary>
        public bool hasSun;
        /// <summary>The sun's height above the horizon (degrees).</summary>
        public float sunElevation;
        /// <summary>The sun's strength: linear luminance of its colour times intensity.</summary>
        public float sunLevel;
        /// <summary>The ambient light from above (linear) and its luminance.</summary>
        public Color ambient;
        public float ambientLevel;
        /// <summary>The scene's horizon colour (linear): a Looksmith Sky's horizon, else the ambient seen sideways.</summary>
        public Color horizon;
        /// <summary>Unity's own fog (Lighting > Environment) is on.</summary>
        public bool unityFog;
        /// <summary>A Looksmith Sky drives the haze colour.</summary>
        public bool looksmithSky;
        /// <summary>The height the frame is shown at (pixels).</summary>
        public int outputHeight;
        /// <summary>The camera renders without post-processing (URP's per-camera switch, off by default): URP grading doesn't show.</summary>
        public bool postProcessingOff;
        /// <summary>
        /// Share of the scene's visible renderers drawn with other shaders than Looksmith's (0..1): a pack's own trees or
        /// water Restyle left alone. They take Unity's fog but not the Look's haze.
        /// </summary>
        public float otherShaders;

        /// <summary>The frame was measured (<see cref="LooksmithLookAdvisor.ReadFrame"/>).</summary>
        public bool hasFrame;
        /// <summary>Mean tone (0..1, perceptual), the share of crushed blacks and of clipped whites, mean OKLCH chroma.</summary>
        public float meanTone, crushed, clipped, meanChroma;

        /// <summary>Night or dusk: no sun, or a sun barely above the horizon.</summary>
        public bool dim => !hasSun || sunElevation < 4f || sunLevel < 0.15f;
        /// <summary>A low sun (golden hour, dusk, dawn).</summary>
        public bool lowSun => hasSun && sunElevation < 15f;
    }

    public enum LooksmithAdviceLevel
    {
        /// <summary>Worth a look; the look may mean it this way.</summary>
        Tip,
        /// <summary>The look is fighting the scene: the change makes it read as intended.</summary>
        Recommended,
    }

    /// <summary>One change to a profile: a parameter of one of its components (the Look, an Art Style, URP grading).</summary>
    [Serializable]
    public struct LooksmithAdviceChange
    {
        /// <summary>The component's type name ("LooksmithLook", "LooksmithComic", "ColorAdjustments").</summary>
        public string component;
        /// <summary>The parameter's field name ("ambient", "adaptToLight", "postExposure").</summary>
        public string parameter;
        /// <summary>The new value for a float, int (rounded) or bool (above 0.5) parameter.</summary>
        public float value;
        /// <summary>The new value for an OKLCH colour parameter (when <see cref="isColor"/>).</summary>
        public Vector3 color;
        public bool isColor;
        /// <summary>What changes, for people: "Ambient 0.30 → 0.45".</summary>
        public string label;
    }

    /// <summary>A recommendation: what, why, and the changes that carry it out.</summary>
    public sealed class LooksmithAdvice
    {
        public string id;
        public string title;
        public string why;
        public LooksmithAdviceLevel level;
        public readonly List<LooksmithAdviceChange> changes = new List<LooksmithAdviceChange>();
        /// <summary>Unity's fog should go off (a scene change, made on its own).</summary>
        public bool turnOffUnityFog;
        /// <summary>The camera's Post Processing should go on (a change to the camera, made on its own).</summary>
        public bool turnOnPostProcessing;

        public override string ToString() => title;
    }

    /// <summary>
    /// The Look Advisor: reads the scene a Look is used in (its light, fog and sky, the frame as rendered) and recommends
    /// tweaks so the look reads as intended there. A look is a starting point: the same profile can't suit a sunny
    /// meadow, a dark interior and a foggy night alike. Rules are shared by the editor (applied as a Changeset) and
    /// players (applied to a runtime copy); Pro adds rules for its Art Styles.
    /// </summary>
    public static class LooksmithLookAdvisor
    {
        /// <summary>A rule: reads the profile and the scene, adds any advice.</summary>
        public delegate void Rule(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading scene, List<LooksmithAdvice> advice);

        static readonly Rule[] s_Core = { PostProcessingOff, TooDark, BlownOut, DoubleFog, HazeFromScene, TintedAmbient, ThinLines, OverSaturated };
        static readonly List<Rule> s_Extra = new List<Rule>();

        /// <summary>Adds a rule (Pro registers its Art Style rules here).</summary>
        public static void Register(Rule rule)
        {
            if (rule != null && !s_Extra.Contains(rule))
                s_Extra.Add(rule);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Extra.Clear();

        /// <summary>The advice for a Look profile in a scene, most important first.</summary>
        public static List<LooksmithAdvice> Advise(VolumeProfile profile, in LooksmithSceneReading scene)
        {
            var advice = new List<LooksmithAdvice>();
            if (profile == null || !profile.TryGet(out LooksmithLook look) || !look.active)
                return advice;
            foreach (var rule in s_Core)
                rule(profile, look, scene, advice);
            foreach (var rule in s_Extra)
                rule(profile, look, scene, advice);
            advice.Sort((a, b) => b.level.CompareTo(a.level));
            return advice;
        }

        // ------------------------------------------------------------------ reading the scene

        static readonly Vector3[] s_Dirs = { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        static readonly Color[] s_Colors = new Color[5];

        /// <summary>Reads the scene's light, fog and sky as a camera sees them (no frame: see <see cref="ReadFrame"/>).</summary>
        public static LooksmithSceneReading ReadScene(Camera camera)
        {
            var r = new LooksmithSceneReading();
            var sun = MainLight();
            if (sun != null)
            {
                r.hasSun = true;
                r.sunElevation = -Mathf.Asin(Mathf.Clamp(sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                var c = sun.color.linear * sun.intensity;
                r.sunLevel = Luminance(c);
            }
            RenderSettings.ambientProbe.Evaluate(s_Dirs, s_Colors);
            r.ambient = s_Colors[0];
            r.ambientLevel = Mathf.Max(0f, Luminance(s_Colors[0]));
            var side = (s_Colors[1] + s_Colors[2] + s_Colors[3] + s_Colors[4]) * 0.25f;
            r.horizon = side;
            var sky = LooksmithSky.active;
            if (sky != null && sky.isActiveAndEnabled)
            {
                r.looksmithSky = true;
                r.horizon = sky.state.horizon;     // already linear
                // the sky's one light is the moon at night: its height says nothing about the sun
                r.hasSun = sky.state.sunElevation > -2f;
                r.sunElevation = sky.state.sunElevation;
            }
            r.unityFog = RenderSettings.fog;
            r.otherShaders = OtherShaders();
            r.outputHeight = camera != null ? camera.pixelHeight : Screen.height;
            // a camera without URP's camera data (a Built-in pack's) renders with its defaults: post-processing off
            r.postProcessingOff = camera != null && !(camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderPostProcessing);
            return r;
        }

        /// <summary>Share of the active renderers (terrains included) with a material that isn't a Looksmith shader.</summary>
        public static float OtherShaders()
        {
            int all = 0, other = 0;
            foreach (var r in LooksmithFind.All<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer)
                    continue;
                all++;
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && !m.shader.name.StartsWith("Looksmith/"))
                    {
                        other++;
                        break;
                    }
            }
            foreach (var t in Terrain.activeTerrains)
            {
                all++;
                if (t.materialTemplate == null || t.materialTemplate.shader == null || !t.materialTemplate.shader.name.StartsWith("Looksmith/"))
                    other++;
            }
            return all > 0 ? other / (float)all : 0f;
        }

        /// <summary>The main light, as URP picks it: RenderSettings.sun, else the brightest active directional light.</summary>
        public static Light MainLight()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled)
                return RenderSettings.sun;
            Light best = null;
            float bestLevel = 0f;
            foreach (var l in LooksmithFind.All<Light>())
            {
                if (l.type != LightType.Directional || !l.isActiveAndEnabled)
                    continue;
                float level = l.intensity * Luminance(l.color.linear);
                if (best == null || level > bestLevel)
                {
                    best = l;
                    bestLevel = level;
                }
            }
            return best;
        }

        /// <summary>
        /// Measures a rendered frame (sRGB-encoded, as read back from an sRGB target): mean tone, crushed and clipped
        /// shares, mean chroma.
        /// </summary>
        public static void ReadFrame(ref LooksmithSceneReading r, Texture2D frame)
        {
            if (frame == null)
                return;
            var px = frame.GetPixels32();
            if (px.Length == 0)
                return;
            double tone = 0, chroma = 0;
            int crushed = 0, clipped = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                float t = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
                tone += t;
                if (t < 0.06f)
                    crushed++;
                if (t > 0.92f && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 250)
                    clipped++;
                var lin = new Color(p.r / 255f, p.g / 255f, p.b / 255f).linear;
                chroma += LooksmithColor.LinearToOklch(new Vector3(lin.r, lin.g, lin.b)).y;
            }
            r.hasFrame = true;
            r.meanTone = (float)(tone / px.Length);
            r.meanChroma = (float)(chroma / px.Length);
            r.crushed = crushed / (float)px.Length;
            r.clipped = clipped / (float)px.Length;
        }

        /// <summary>
        /// Renders what a camera sees into a small texture (a copy of the camera, so the game view isn't touched). Two
        /// frames: the first reads the camera's Look, the second draws with it. The caller destroys the texture.
        /// </summary>
        public static Texture2D CaptureFrame(Camera camera, int width = 192, int height = 108)
        {
            if (camera == null)
                return null;
            var go = new GameObject("Looksmith Advisor Camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Looksmith Advisor" };
            try
            {
                var cam = go.AddComponent<Camera>();
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                cam.CopyFrom(camera);
                if (camera.TryGetComponent(out UniversalAdditionalCameraData src))
                {
                    data.renderPostProcessing = src.renderPostProcessing;
                    data.renderShadows = src.renderShadows;
                    data.dithering = src.dithering;
                    data.volumeLayerMask = src.volumeLayerMask;
                    data.volumeTrigger = src.volumeTrigger != null ? src.volumeTrigger : camera.transform;
                }
                cam.cameraType = CameraType.Game;
                cam.enabled = false;
                cam.aspect = width / (float)height;
                cam.targetTexture = rt;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                for (int i = 0; i < 2; i++)
                {
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    else
                        cam.Render();
                }
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = "Looksmith Advisor Frame" };
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false, false);
                RenderTexture.active = previous;
                return tex;
            }
            finally
            {
                rt.Release();
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(rt);
                    UnityEngine.Object.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(rt);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ------------------------------------------------------------------ applying (runtime copies)

        /// <summary>
        /// Carries out advice on a profile directly (a runtime copy: the demo, a player's settings menu). The editor
        /// applies the same changes through a Changeset instead, with a backup. The camera is the one the scene was read
        /// from (for advice about the camera itself).
        /// </summary>
        public static int Apply(VolumeProfile profile, LooksmithAdvice advice, Camera camera = null)
        {
            int applied = 0;
            if (profile == null || advice == null)
                return 0;
            foreach (var c in advice.changes)
                if (Set(profile, c))
                    applied++;
            if (advice.turnOffUnityFog)
                RenderSettings.fog = false;
            if (advice.turnOnPostProcessing && camera != null)
            {
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
                    data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                applied++;
            }
            return applied;
        }

        /// <summary>
        /// Sets one change on a profile (overriding the parameter, and switching its component on: bloom a look left
        /// off). False if the component or parameter is missing.
        /// </summary>
        public static bool Set(VolumeProfile profile, in LooksmithAdviceChange c)
        {
            var p = FindParameter(profile, c.component, c.parameter, out var owner);
            if (p == null)
                return false;
            owner.active = true;
            p.overrideState = true;
            switch (p)
            {
                case VolumeParameter<float> f: f.value = c.value; return true;
                case VolumeParameter<int> n: n.value = Mathf.RoundToInt(c.value); return true;
                case VolumeParameter<bool> b: b.value = c.value > 0.5f; return true;
                case VolumeParameter<Vector3> v when c.isColor: v.value = c.color; return true;
            }
            return false;
        }

        /// <summary>A parameter of a profile's component, by the component's type name and the field's name.</summary>
        public static VolumeParameter FindParameter(VolumeProfile profile, string component, string parameter, out VolumeComponent owner)
        {
            owner = null;
            if (profile == null)
                return null;
            foreach (var comp in profile.components)
            {
                if (comp == null || comp.GetType().Name != component)
                    continue;
                owner = comp;
                var field = comp.GetType().GetField(parameter, BindingFlags.Public | BindingFlags.Instance);
                return field?.GetValue(comp) as VolumeParameter;
            }
            return null;
        }

        // ------------------------------------------------------------------ helpers for rules

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>A change to a float parameter, labelled "Name a → b".</summary>
        public static LooksmithAdviceChange Change(string component, string parameter, string name, float from, float to, string format = "0.00") =>
            new LooksmithAdviceChange { component = component, parameter = parameter, value = to, label = $"{name} {from.ToString(format)} → {to.ToString(format)}" };

        /// <summary>A change to a bool parameter.</summary>
        public static LooksmithAdviceChange Switch(string component, string parameter, string name, bool on) =>
            new LooksmithAdviceChange { component = component, parameter = parameter, value = on ? 1f : 0f, label = $"{name}: {(on ? "on" : "off")}" };

        /// <summary>
        /// The look is dark on purpose (horror, dark fantasy): grain, a heavy vignette or a dark fog colour say so. Its
        /// darkness advice stays a tip.
        /// </summary>
        public static bool DarkByDesign(LooksmithLook look) =>
            look.grain.value > 0.35f || look.vignette.value > 0.45f || look.fogColor.value.x < 0.45f;

        static bool HazeOn(LooksmithLook look) => look.haze.value > 0.05f && look.hazeMax.value > 0.05f;

        // ------------------------------------------------------------------ the core rules

        // The look grades through URP post-processing (tonemapping, exposure, bloom, vignette) and the camera has Post
        // Processing off: none of that shows, so the scene doesn't look like the look's preview. One switch on the camera.
        static void PostProcessingOff(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (!s.postProcessingOff || !UsesUrpPost(profile))
                return;
            advice.Add(new LooksmithAdvice
            {
                id = "post-processing-off",
                level = LooksmithAdviceLevel.Recommended,
                title = "The camera has post-processing off",
                why = "This look grades through URP post-processing (tonemapping, exposure, bloom) and the camera skips it, so the scene doesn't look like the look's preview. Looksmith's lighting and art styles run either way; turning the camera's Post Processing on shows the grading too.",
                turnOnPostProcessing = true,
            });
        }

        /// <summary>The profile has URP post-processing in use (an active URP volume component with an overridden setting).</summary>
        public static bool UsesUrpPost(VolumeProfile profile)
        {
            foreach (var c in profile.components)
            {
                if (c == null || !c.active || c.GetType().Namespace != typeof(Tonemapping).Namespace)
                    continue;
                foreach (var p in c.parameters)
                    if (p.overrideState)
                        return true;
            }
            return false;
        }

        // Most of the frame is crushed to black: the shadows have nowhere to go. More ambient lifts them evenly (the bands
        // and the lit side keep their shape); with URP grading in the profile, a little exposure too.
        static void TooDark(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (!s.hasFrame || s.meanTone > 0.2f || s.crushed < 0.2f)
                return;
            bool byDesign = DarkByDesign(look);
            float amb = look.ambient.value;
            var a = new LooksmithAdvice
            {
                id = "too-dark",
                level = byDesign ? LooksmithAdviceLevel.Tip : LooksmithAdviceLevel.Recommended,
                title = byDesign ? "Dark by design, but a lot of the frame is pure black" : "Too dark for this look here",
                why = $"{s.crushed:P0} of the frame is crushed to black{(s.dim ? " (it's night or dusk in this scene)" : "")}. More ambient lifts the shadows evenly, so shapes read without washing out the light.",
            };
            a.changes.Add(Change("LooksmithLook", "ambient", "Ambient", amb, Mathf.Min(amb + (byDesign ? 0.08f : 0.15f), 1.2f)));
            // Exposure does most of the lifting (ambient only scales what light the scene has, and a night has little):
            // enough stops to bring the frame's mean toward a readable tone, at most a stop and a half.
            float target = byDesign ? 0.14f : 0.2f;
            float stops = Mathf.Clamp(Mathf.Log(Mathf.Pow(target / Mathf.Max(s.meanTone, 0.01f), 2.2f), 2f), 0.3f, 1.5f);
            if (FindParameter(profile, "ColorAdjustments", "postExposure", out _) is VolumeParameter<float> exposure)
                a.changes.Add(Change("ColorAdjustments", "postExposure", "Post Exposure", exposure.value, exposure.value + stops));
            else
                a.changes.Add(Change("LooksmithLook", "keyLevel", "Key Level", look.keyLevel.value, Mathf.Min(look.keyLevel.value * Mathf.Pow(2f, stops * 0.5f), 1.6f)));
            advice.Add(a);
        }

        // Highlights clip: lower the key light (the lit side keeps its colour instead of turning white).
        static void BlownOut(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (!s.hasFrame || s.clipped < 0.12f)
                return;
            var a = new LooksmithAdvice
            {
                id = "blown-out",
                level = LooksmithAdviceLevel.Recommended,
                title = "Highlights clip to white",
                why = $"{s.clipped:P0} of the frame is pure white: lit surfaces lose their colour. A lower Key Level keeps the light's colour on them.",
            };
            float key = look.keyLevel.value;
            a.changes.Add(Change("LooksmithLook", "keyLevel", "Key Level", key, Mathf.Max(key - 0.12f, 0.5f)));
            if (FindParameter(profile, "ColorAdjustments", "postExposure", out _) is VolumeParameter<float> exposure && exposure.value > -1f)
                a.changes.Add(Change("ColorAdjustments", "postExposure", "Post Exposure", exposure.value, exposure.value - 0.3f));
            advice.Add(a);
        }

        // Unity's fog and the Look's haze stack: distance goes milky twice over. One of them should do it. Looksmith surfaces
        // take Unity's fog like any material, but the Look's haze lives in Looksmith shaders only: where Restyle left a pack's
        // own trees or water alone, keeping Unity's fog is the one fog that reaches everything.
        static void DoubleFog(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (!s.unityFog || !HazeOn(look))
                return;
            bool mixed = s.otherShaders > 0.1f;
            var replace = new LooksmithAdvice
            {
                id = mixed ? "double-fog-replace" : "double-fog",
                level = mixed ? LooksmithAdviceLevel.Tip : LooksmithAdviceLevel.Recommended,
                title = mixed ? "Or let the Look's haze replace your fog" : "Two fogs: Unity's and the Look's haze",
                why = mixed
                    ? $"The Look's haze follows its palette and the sky, but it only reaches Looksmith materials: {s.otherShaders:P0} of this scene keeps its own shaders and would have no fog at all. Worth it once the whole scene is restyled."
                    : "Unity's fog (Lighting > Environment) is on and the Look adds its haze on top, so the distance fades twice. The Look's haze follows its palette and the sky; turning Unity's fog off lets it do the job.",
                turnOffUnityFog = true,
            };
            var keep = new LooksmithAdvice
            {
                id = mixed ? "double-fog" : "double-fog-keep-yours",
                level = mixed ? LooksmithAdviceLevel.Recommended : LooksmithAdviceLevel.Tip,
                title = mixed ? "Two fogs: keep your Unity fog, turn the Look's haze off" : "Or keep your fog and turn the Look's haze off",
                why = mixed
                    ? $"Unity's fog is on and the Look adds its haze on top, so Looksmith surfaces fade twice. {s.otherShaders:P0} of this scene keeps its own shaders, which take Unity's fog but not the haze: your fog is the one that reaches everything."
                    : "If your game relies on Unity's fog (its colour, its gameplay distance), the Look's haze can step aside. Looksmith surfaces take Unity's fog like every other material.",
            };
            keep.changes.Add(Change("LooksmithLook", "haze", "Haze", look.haze.value, 0f));
            advice.Add(mixed ? keep : replace);
            advice.Add(mixed ? replace : keep);
        }

        // Without a Looksmith Sky the haze uses the palette's Fog colour as is: a blue-grey fog under a warm desert sky, or a
        // warm one in a cold night, reads as wrong. Take the scene's horizon tint (keeping the fog's own lightness).
        static void HazeFromScene(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (s.looksmithSky || !HazeOn(look) || Luminance(s.horizon) < 0.002f)
                return;
            var fog = look.fogColor.value;
            var horizon = LooksmithColor.LinearToOklch(new Vector3(s.horizon.r, s.horizon.g, s.horizon.b));
            var a1 = new Vector2(Mathf.Cos(fog.z * Mathf.Deg2Rad), Mathf.Sin(fog.z * Mathf.Deg2Rad)) * fog.y;
            var a2 = new Vector2(Mathf.Cos(horizon.z * Mathf.Deg2Rad), Mathf.Sin(horizon.z * Mathf.Deg2Rad)) * horizon.y;
            if ((a1 - a2).magnitude < 0.05f)
                return;
            var to = new Vector3(fog.x, Mathf.Min(horizon.y, 0.12f), horizon.z);
            var a = new LooksmithAdvice
            {
                id = "haze-from-scene",
                level = LooksmithAdviceLevel.Tip,
                title = "Haze colour from your sky",
                why = "There's no Looksmith Sky to colour the haze, so it uses the palette's Fog colour, which doesn't match this scene's horizon. Taking the horizon's tint (at the fog's own lightness) makes distance sink into your sky.",
            };
            a.changes.Add(new LooksmithAdviceChange { component = "LooksmithLook", parameter = "fogColor", isColor = true, color = to, label = "Fog colour from the horizon" });
            advice.Add(a);
        }

        // A strongly coloured ambient (a blue sky light) plus the palette's ambient tint on top: rooms and shade lit only
        // by ambient take the colour wholesale.
        static void TintedAmbient(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            // (by day: a night's blue moonlight is meant to tint everything)
            if (s.dim || s.ambientLevel < 0.01f || look.ambientFromPalette.value < 0.45f)
                return;
            var lch = LooksmithColor.LinearToOklch(new Vector3(s.ambient.r, s.ambient.g, s.ambient.b));
            if (lch.y < 0.06f)
                return;
            var a = new LooksmithAdvice
            {
                id = "tinted-ambient",
                level = LooksmithAdviceLevel.Tip,
                title = "Shade and interiors take a strong tint",
                why = "This scene's ambient light is strongly coloured, and the Look tints ambient toward its palette on top: anything lit only by ambient (rooms, deep shade) takes the colour wholesale.",
            };
            a.changes.Add(Change("LooksmithLook", "ambientFromPalette", "Ambient From Palette", look.ambientFromPalette.value, 0.3f));
            advice.Add(a);
        }

        // Lines are sized for 1080p and scale with the frame: at a small output they fall under a pixel and break up.
        static void ThinLines(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            // (a look drawn with hairlines on purpose keeps them: only lines that are whole at 1080p and break up at a
            // smaller output count)
            float w = look.lineWeight.value;
            if (w < 1f || s.outputHeight <= 0 || s.outputHeight >= 1080)
                return;
            float px = w * s.outputHeight / 1080f;
            if (px >= 1.0f)
                return;
            float to = Mathf.Min(1.3f * 1080f / s.outputHeight, 4f);
            var a = new LooksmithAdvice
            {
                id = "thin-lines",
                level = LooksmithAdviceLevel.Tip,
                title = "Lines are thinner than a pixel at this size",
                why = $"Line Weight is set for 1080p; at {s.outputHeight}p the lines are {px:0.0} px wide and break up. Heavier lines keep them whole.",
            };
            a.changes.Add(Change("LooksmithLook", "lineWeight", "Line Weight", w, to));
            advice.Add(a);
        }

        // A saturated scene and a look that boosts saturation: colours clip and band.
        static void OverSaturated(VolumeProfile profile, LooksmithLook look, in LooksmithSceneReading s, List<LooksmithAdvice> advice)
        {
            if (!s.hasFrame || s.meanChroma < 0.15f || look.saturation.value <= 1.05f)
                return;
            var a = new LooksmithAdvice
            {
                id = "over-saturated",
                level = LooksmithAdviceLevel.Tip,
                title = "Colours are already strong here",
                why = "This scene's colours are saturated and the Look boosts them further: they clip and band. Taking the boost out keeps them rich without the glare.",
            };
            a.changes.Add(Change("LooksmithLook", "saturation", "Saturation", look.saturation.value, 1f));
            advice.Add(a);
        }
    }
}
