using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>One reason a look doesn't come out right, in plain words, with what to do about it.</summary>
    public sealed class LookDoctorItem
    {
        public string id, title, why;
        /// <summary>The button's label (null: nothing to click, the explanation is the help).</summary>
        public string action;
        public Action run;
    }

    /// <summary>
    /// "It doesn't look right": the usual reasons a look misses in a scene, checked on the spot (the camera's view rendered,
    /// the scene and project read), each in plain words with its fix. Fixes go through the usual previews and backups;
    /// nothing here writes on its own, and the user's own volumes and URP asset are only ever pointed at, never edited.
    /// </summary>
    public static class LooksmithLookDoctor
    {
        public static List<LookDoctorItem> Diagnose(Scene scene, Camera camera = null)
        {
            var items = new List<LookDoctorItem>();
            camera ??= LooksmithLookFit.ReferenceCamera();

            if (LooksmithRuntime.Bypass)
                items.Add(new LookDoctorItem
                {
                    id = "bypass", title = "Looksmith is switched off (Bypass)",
                    why = "Bypass renders Looksmith materials as standard PBR and skips every Looksmith pass: no look at all.",
                    action = "Switch Looksmith on", run = () => LooksmithRuntime.Bypass = false,
                });

            var volume = LooksmithLookGallery.SceneLookVolume(scene);
            if (volume == null || volume.sharedProfile == null)
            {
                items.Add(new LookDoctorItem
                {
                    id = "no-look", title = "This scene has no Looksmith look yet",
                    why = "Add Looksmith to the scene (Hub > Home): a scan, then one previewed, backed-up Apply that adds the Look and restyles the materials.",
                    action = "Add Looksmith…", run = () => LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Home),
                });
                return items;
            }
            if (!volume.isActiveAndEnabled || volume.weight < 0.99f)
                items.Add(new LookDoctorItem
                {
                    id = "look-weak", title = "The Look Volume is off or only partly on",
                    why = $"{volume.name} is {(volume.isActiveAndEnabled ? $"at weight {volume.weight:0.##}" : "disabled")}: the look is only partly applied.",
                    action = "Show it", run = () => { Selection.activeObject = volume; EditorGUIUtility.PingObject(volume); },
                });
            if (camera == null)
            {
                items.Add(new LookDoctorItem { id = "no-camera", title = "No camera to judge from", why = "Add a camera (tagged MainCamera) so the checks can see what the player sees." });
                return items;
            }

            // most of the view on other shaders: the look's lighting can't reach it
            var plan = LooksmithRestyle.Plan(RestyleScope.Scene);
            var coverage = LooksmithCoverage.Measure(camera, plan);
            if (coverage != null && coverage.sky < 0.999f && coverage.other > 0.3f)
                items.Add(new LookDoctorItem
                {
                    id = "unrestyled",
                    title = coverage.looksmithAfter - coverage.looksmith > 0.1f ? $"{Pct(coverage.other)} of the view isn't restyled yet" : $"{Pct(coverage.other)} of the view keeps its own shaders",
                    why = coverage.Summary() + ". " + coverage.Meaning(),
                    action = "Restyle…", run = () => LooksmithHubWindow.ShowTab(LooksmithHubWindow.Tab.Restyle),
                });

            // the Look Advisor: dark, blown out, two fogs, post-processing off, thin lines, strong tints…
            var profile = volume.sharedProfile;
            var reading = LooksmithLookFit.Read(camera);
            foreach (var a in LooksmithLookAdvisor.Advise(profile, reading))
            {
                var advice = a;
                items.Add(new LookDoctorItem
                {
                    id = "advice-" + a.id, title = (a.level == LooksmithAdviceLevel.Recommended ? "" : "Tip: ") + a.title, why = a.why,
                    action = "Apply…", run = () => LooksmithLookFit.Apply(profile, new[] { advice }),
                });
            }

            // a dark scene and a look that has a Night variant
            var shipped = LooksmithLookGallery.ShippedLookName(profile);
            var variant = shipped != null ? LooksmithLooks.FindVariant(shipped) : null;
            var baseLook = variant != null ? variant.look : shipped;
            var night = baseLook != null ? LooksmithLooks.FindVariant($"{baseLook} ({LooksmithLooks.Night})") : null;
            if (night != null && variant?.kind != LooksmithLooks.Night && reading.hasFrame && (reading.meanTone < 0.2f || reading.crushed > 0.3f))
                items.Add(new LookDoctorItem
                {
                    id = "night-variant", title = $"It's dark here: try {night.name}",
                    why = $"{Pct(reading.crushed)} of the frame is crushed to black. {night.description}",
                    action = "Use the Night variant", run = () => LooksmithLookGallery.ApplyLook(volume, night.name),
                });

            // low render scale: lines and marks drawn small and stretched
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.renderScale < 0.95f
                && (profile.TryGet(out LooksmithLook look) && look.lineWeight.value > 0f || profile.components.Any(IsStyle)))
                items.Add(new LookDoctorItem
                {
                    id = "render-scale", title = $"Render scale is {urp.renderScale:0.##}",
                    why = $"Lines and art-style marks are drawn at {Pct(urp.renderScale)} of the screen and stretched up, so they come out soft or shimmer. A render scale of 1 keeps them crisp. Looksmith never edits your URP asset: set it on {urp.name} if you'd like.",
                    action = "Show the URP asset", run = () => { Selection.activeObject = urp; EditorGUIUtility.PingObject(urp); },
                });

            // grain and dithering under the paint styles
            if (profile.components.Any(IsStyle))
            {
                if (camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.dithering && SetCameraDitheringChange.For(camera) is SetCameraDitheringChange dither)
                    items.Add(new LookDoctorItem
                    {
                        id = "dithering", title = "The camera dithers under the art styles",
                        why = "URP's dithering adds fine noise to the image; the paint, ink and comic styles read it as texture and turn it into speckles.",
                        action = "Turn dithering off…", run = () => LooksmithHubWindow.ApplyWithPrompts(new Changeset(dither.title, new LooksmithChange[] { dither }), true),
                    });
                var grain = LooksmithFind.All<Volume>()
                    .Where(v => v.gameObject.scene == scene && v != volume && v.isActiveAndEnabled && v.sharedProfile != null
                                && v.sharedProfile.TryGet(out FilmGrain g) && g.active && g.intensity.overrideState && g.intensity.value > 0.05f)
                    .ToList();
                foreach (var v in grain)
                {
                    var owner = v;
                    items.Add(new LookDoctorItem
                    {
                        id = "film-grain", title = $"Your film grain ({v.name}) runs under the art styles",
                        why = "Grain added before the styles turns into speckled strokes. The look has its own grain, after the styles. Looksmith never edits your volumes: turn yours down there if you'd like.",
                        action = "Show the volume", run = () => { Selection.activeObject = owner; EditorGUIUtility.PingObject(owner); },
                    });
                }
            }

            // someone else's water, unmarked
            var water = MarkWaterChange.Unmarked(scene);
            if (water.Count > 0 && !string.IsNullOrEmpty(scene.path))
            {
                var finding = LooksmithScan.Run().FirstOrDefault(f => f.id == "water-unmarked");
                if (finding != null)
                    items.Add(new LookDoctorItem
                    {
                        id = "water-unmarked", title = finding.title, why = finding.detail,
                        action = "Mark the water…", run = () => LooksmithHubWindow.Fix(finding),
                    });
            }
            return items;
        }

        static bool IsStyle(VolumeComponent c) => c is LooksmithStyle s && s.active && s.intensity.value > 0.01f && !(s is LooksmithPosterize);

        static string Pct(float x) => Mathf.RoundToInt(x * 100f) + "%";
    }
}
