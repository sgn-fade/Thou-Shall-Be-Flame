using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Sets one serialized field on an asset (URP asset, renderer data...). Covers most project fixes.</summary>
    [Serializable]
    public sealed class SetSerializedValueChange : LooksmithChange
    {
        public string assetPath;
        public string propertyPath;
        public string valueKind;   // "bool" | "int" | "float"
        public string value;
        public string label;
        public string explanation;
        public ChangeGroup changeGroup = ChangeGroup.Project;

        public override ChangeGroup group => changeGroup;
        public override string title => label;
        public override string detail => explanation;
        public override IEnumerable<string> Writes() { yield return assetPath; }

        public static SetSerializedValueChange Bool(string path, string property, bool v, string label, string why = "") =>
            new SetSerializedValueChange { assetPath = path, propertyPath = property, valueKind = "bool", value = v ? "1" : "0", label = label, explanation = why };

        public static SetSerializedValueChange Int(string path, string property, int v, string label, string why = "") =>
            new SetSerializedValueChange { assetPath = path, propertyPath = property, valueKind = "int", value = v.ToString(), label = label, explanation = why };

        public override void Apply(ChangeContext context)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj == null)
                throw new InvalidOperationException("Asset not found: " + assetPath);
            var so = new SerializedObject(obj);
            var p = so.FindProperty(propertyPath) ?? throw new InvalidOperationException($"{propertyPath} not found on {assetPath}");
            switch (valueKind)
            {
                case "bool": p.boolValue = value == "1"; break;
                case "int": p.intValue = int.Parse(value); break;
                case "float": p.floatValue = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                default: throw new InvalidOperationException("Unknown value kind " + valueKind);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            AssetDatabase.SaveAssetIfDirty(obj);
        }
    }

    /// <summary>
    /// Gamma to Linear (PlayerSettings). Unity reimports textures afterwards. URP also sets GraphicsSettings'
    /// "lights use linear intensity" to match the colour space the next time the pipeline is created, so this change
    /// owns that side effect too: it sets it during the run (inside the backup) and puts it back on revert.
    /// </summary>
    [Serializable]
    public sealed class ColorSpaceChange : LooksmithChange
    {
        public const string SettingsFile = "ProjectSettings/ProjectSettings.asset";
        public const string GraphicsFile = "ProjectSettings/GraphicsSettings.asset";
        public ColorSpace target = ColorSpace.Linear;
        public ColorSpace previous = ColorSpace.Gamma;
        public bool previousLinearIntensity;

        public override ChangeGroup group => ChangeGroup.Project;
        public override string title => $"Switch colour space to {target}";
        public override string detail => "Stylized ramps and palettes are authored in linear light. Unity reimports textures afterwards (can take a while).";
        public override IEnumerable<string> Writes()
        {
            yield return SettingsFile;
            yield return GraphicsFile;
        }

        public override void Apply(ChangeContext context)
        {
            previous = PlayerSettings.colorSpace;
            previousLinearIntensity = GraphicsSettings.lightsUseLinearIntensity;
            PlayerSettings.colorSpace = target;   // saved when the Changeset finishes
            GraphicsSettings.lightsUseLinearIntensity = target == ColorSpace.Linear;   // what URP would do on its next render
            EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
        }

        public override void AfterRevert()
        {
            bool changed = false;
            if (PlayerSettings.colorSpace != previous)
            {
                PlayerSettings.colorSpace = previous;
                changed = true;
            }
            if (GraphicsSettings.lightsUseLinearIntensity != previousLinearIntensity)
            {
                GraphicsSettings.lightsUseLinearIntensity = previousLinearIntensity;
                changed = true;
            }
            if (changed)
                AssetDatabase.SaveAssets();
        }
    }

    /// <summary>Creates a Look Profile asset (a Volume Profile carrying the Looksmith Look) and any missing folders.</summary>
    [Serializable]
    public sealed class CreateLookProfileChange : LooksmithChange
    {
        public string profilePath;
        public string lookName = LooksmithLooks.FirstLook;

        public override ChangeGroup group => ChangeGroup.Assets;
        public override string title => $"Create Look Profile {Path.GetFileName(profilePath)}";
        public override string detail => profilePath;

        public override IEnumerable<string> Writes()
        {
            foreach (var folder in MissingFolders(Path.GetDirectoryName(profilePath).Replace('\\', '/')))
            {
                yield return folder;
                yield return folder + ".meta";
            }
            yield return profilePath;
        }

        static IEnumerable<string> MissingFolders(string folder)
        {
            var missing = new List<string>();
            while (!string.IsNullOrEmpty(folder) && folder != "Assets" && !AssetDatabase.IsValidFolder(folder))
            {
                missing.Insert(0, folder);
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
            return missing;
        }

        public override void Apply(ChangeContext context)
        {
            EnsureFolder(Path.GetDirectoryName(profilePath).Replace('\\', '/'));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            LooksmithLookPopulator.Populate?.Invoke(profile, lookName);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        internal static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>Hook the Look Core fills in (keeps the safety layer independent of the look data).</summary>
    public static class LooksmithLookPopulator
    {
        public static Action<VolumeProfile, string> Populate;
    }

    /// <summary>
    /// Adds the "Looksmith" scene root holding the global Look Volume, just above the scene's own global volumes: a look's
    /// grading then wins over theirs for the settings it sets (it looks like its preview), their other effects still
    /// blend in, and their local volumes with a higher priority keep their areas. Their volumes are never touched (P6).
    /// </summary>
    [Serializable]
    public sealed class AddLooksmithRootChange : LooksmithChange
    {
        public string scenePath;
        public string profilePath;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => "Add the Looksmith root (Look Volume) to " + Path.GetFileNameWithoutExtension(scenePath);
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var root = new GameObject("Looksmith");
            SceneManager.MoveGameObjectToScene(root, scene);
            var volumeGo = new GameObject("Look Volume");
            volumeGo.transform.SetParent(root.transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = AbovePriority(scene);
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            volumeGo.AddComponent<LooksmithLookVolume>();
            context.RegisterCreatedObject(root, "Looksmith root");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>One above the highest global volume already in the scene (0 when there's none).</summary>
        public static float AbovePriority(Scene scene)
        {
            float top = -1f;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var v in go.GetComponentsInChildren<Volume>(true))
                    if (v.isGlobal && v.GetComponent<LooksmithLookVolume>() == null)
                        top = Mathf.Max(top, v.priority);
            return top < 0f ? 0f : Mathf.Floor(top) + 1f;
        }
    }

    /// <summary>Adds a directional light when the scene has none.</summary>
    [Serializable]
    public sealed class AddSunChange : LooksmithChange
    {
        public string scenePath;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => "Add a Looksmith Sun (directional light)";
        public override string detail => "Looksmith's ramps and shadow tint need a main light.";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var go = new GameObject("Looksmith Sun");
            SceneManager.MoveGameObjectToScene(go, scene);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.95f, 0.88f);
            go.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            context.RegisterCreatedObject(go, "Looksmith Sun");
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>
    /// Stops static batching on the scene's objects whose Looksmith materials sway in the wind (their other static flags stay).
    /// Static batching bakes a mesh into world space, so the wind would read every vertex as the top of the plant and the
    /// whole object would slide over the ground instead of swaying.
    /// </summary>
    [Serializable]
    public sealed class ClearSwayBatchingChange : LooksmithChange
    {
        public string scenePath;
        public int count;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => $"Stop static batching on {count} swaying object(s)";
        public override string detail => "Batched plants slide over the ground instead of swaying. Their other static flags are kept.";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        public static List<GameObject> Find(Scene scene) =>
            LooksmithFind.All<Renderer>(FindObjectsInactive.Include)
                .Where(r => r.gameObject.scene == scene && r.sharedMaterials.Any(LooksmithScan.Sways) &&
                            GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))
                .Select(r => r.gameObject).Distinct().ToList();

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid())
                scene = SceneManager.GetActiveScene();
            foreach (var go in Find(scene))
            {
                Undo.RecordObject(go, "Looksmith: stop static batching on a swaying object");
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.BatchingStatic);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>Turns Unity's built-in fog on or off for a scene.</summary>
    [Serializable]
    public sealed class SetSceneFogChange : LooksmithChange
    {
        public string scenePath;
        public bool fog;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => (fog ? "Enable" : "Disable") + " Unity fog in " + Path.GetFileNameWithoutExtension(scenePath);
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            RenderSettings.fog = fog;
            SceneManager.SetActiveScene(active);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>
    /// Turns a scene camera's Post Processing on (URP's per-camera switch), so a Look's URP grading shows. The camera
    /// gets URP's camera data if it had none (a Built-in camera brought into URP).
    /// </summary>
    [Serializable]
    public sealed class SetCameraPostProcessingChange : LooksmithChange
    {
        public string scenePath;
        public string cameraGlobalId;
        public string cameraName;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => $"Turn Post Processing on for {cameraName}";
        public override string detail => "Looksmith's looks grade through URP post-processing (tonemapping, exposure, bloom); a camera with it off never shows that.";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        /// <summary>The change for a camera, or null when it already renders post-processing (or isn't in a saved scene).</summary>
        public static SetCameraPostProcessingChange For(Camera camera)
        {
            if (camera == null || string.IsNullOrEmpty(camera.gameObject.scene.path))
                return null;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderPostProcessing)
                return null;
            return new SetCameraPostProcessingChange
            {
                scenePath = camera.gameObject.scene.path, cameraName = camera.name,
                cameraGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(camera).ToString(),
            };
        }

        public override void Apply(ChangeContext context)
        {
            if (!GlobalObjectId.TryParse(cameraGlobalId, out var id) || !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Camera camera))
                throw new InvalidOperationException("Camera not found: " + cameraName);
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
                data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
            Undo.RecordObject(data, "Looksmith: camera post-processing on");
            data.renderPostProcessing = true;
            if (PrefabUtility.IsPartOfPrefabInstance(data))
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        }
    }

    /// <summary>Turns a scene camera's dithering off: URP's fine noise turns into speckles under the paint and ink styles.</summary>
    [Serializable]
    public sealed class SetCameraDitheringChange : LooksmithChange
    {
        public string scenePath;
        public string cameraGlobalId;
        public string cameraName;

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => $"Turn dithering off for {cameraName}";
        public override string detail => "URP's dithering adds fine noise; the paint, ink and comic styles read it as texture.";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        /// <summary>The change for a camera that dithers (null when it doesn't, or isn't in a saved scene).</summary>
        public static SetCameraDitheringChange For(Camera camera)
        {
            if (camera == null || string.IsNullOrEmpty(camera.gameObject.scene.path) || !camera.TryGetComponent(out UniversalAdditionalCameraData data) || !data.dithering)
                return null;
            return new SetCameraDitheringChange
            {
                scenePath = camera.gameObject.scene.path, cameraName = camera.name,
                cameraGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(camera).ToString(),
            };
        }

        public override void Apply(ChangeContext context)
        {
            if (!GlobalObjectId.TryParse(cameraGlobalId, out var id) || !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Camera camera)
                || !camera.TryGetComponent(out UniversalAdditionalCameraData data))
                throw new InvalidOperationException("Camera not found: " + cameraName);
            Undo.RecordObject(data, "Looksmith: camera dithering off");
            data.dithering = false;
            if (PrefabUtility.IsPartOfPrefabInstance(data))
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        }
    }

    /// <summary>
    /// Marks other people's water with a Looksmith Water Plane, so Looksmith's lines, marks, paper and fog treat it as a surface
    /// instead of seeing the bed under it. The water itself is untouched.
    /// </summary>
    [Serializable]
    public sealed class MarkWaterChange : LooksmithChange
    {
        public string scenePath;
        public List<string> objectGlobalIds = new List<string>();
        public List<string> names = new List<string>();

        public override ChangeGroup group => ChangeGroup.Scene;
        public override string title => $"Mark {names.Count} water surface(s) for Looksmith: {string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", …" : "")}";
        public override string detail => "Adds a Looksmith Water Plane to each; nothing about the water changes.";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        /// <summary>Renderers in a scene that look like water (a water shader or material) and aren't marked or Looksmith's own.</summary>
        public static List<Renderer> Unmarked(Scene scene)
        {
            var found = new List<Renderer>();
            if (!scene.IsValid() || !scene.isLoaded)
                return found;   // the Hub can rebuild while a scene unloads
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r.GetComponentInParent<LooksmithWaterPlane>() != null)
                        continue;
                    // a water shader, or a transparent material on something named like water
                    bool named = System.Text.RegularExpressions.Regex.IsMatch(r.name, "(?i)water|ocean|river|lake|pond");
                    bool water = r.sharedMaterials.Any(m => m != null && m.shader != null && !m.shader.name.StartsWith("Looksmith/")
                        && (LooksmithRestyle.IsWater(m.shader)
                            || (m.renderQueue >= 2900 && (named || System.Text.RegularExpressions.Regex.IsMatch(m.name, "(?i)water|ocean|river|lake|pond")))));
                    if (water && IsSurfaceShaped(r))
                        found.Add(r);
                }
            return found;
        }

        /// <summary>
        /// A wide, flat surface, as a lake, river or sea is: a square metre or more across, and low for its width. Water in
        /// a bottle (the Restaurant's 131 bottles hold "M_Water") isn't a surface the effects need, and a waterfall isn't a
        /// plane.
        /// </summary>
        static bool IsSurfaceShaped(Renderer r)
        {
            var size = r.bounds.size;
            if (size == Vector3.zero && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                size = Vector3.Scale(filter.sharedMesh.bounds.size, r.transform.lossyScale);   // an inactive one has no bounds
            return size.x * size.z >= 1f && size.y <= 0.35f * Mathf.Max(size.x, size.z);
        }

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            foreach (var id in objectGlobalIds)
            {
                if (!GlobalObjectId.TryParse(id, out var gid) || !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is GameObject go) || go.GetComponent<LooksmithWaterPlane>() != null)
                    continue;
                Undo.AddComponent<LooksmithWaterPlane>(go);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>Removes duplicate Looksmith renderer features (keeps the first). Never touches other features (P6).</summary>
    [Serializable]
    public sealed class RemoveDuplicateLooksmithFeatureChange : LooksmithChange
    {
        public string rendererPath;

        public override ChangeGroup group => ChangeGroup.Project;
        public override string title => "Remove the duplicate Looksmith feature from " + Path.GetFileNameWithoutExtension(rendererPath);
        public override IEnumerable<string> Writes() { yield return rendererPath; }

        public override void Apply(ChangeContext context)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            bool seen = false;
            var remove = new List<UnityEngine.Object>();
            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                var f = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (!(f is LooksmithRendererFeature))
                    continue;
                // keep the first one in list order: walk backwards, so remove all but the last visited
                bool isFirst = Enumerable.Range(0, i).All(j => !(features.GetArrayElementAtIndex(j).objectReferenceValue is LooksmithRendererFeature));
                if (isFirst)
                {
                    seen = true;
                    continue;
                }
                remove.Add(f);
                features.DeleteArrayElementAtIndex(i);
                if (map != null && i < map.arraySize)
                    map.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var f in remove)
                AssetDatabase.RemoveObjectFromAsset(f);
            if (seen || remove.Count > 0)
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
        }
    }
}
