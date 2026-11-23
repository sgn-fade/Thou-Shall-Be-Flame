using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Look gallery thumbnails: each Look Profile rendered on a small diorama through <see cref="PreviewRenderUtility"/>,
    /// with its lines, Art Styles and URP grading. Cached in memory and in <c>Library/LooksmithThumbnails</c> (a local cache,
    /// never a project file); a profile renders again only when it changes. One thumbnail renders per editor update, so
    /// the gallery opens at once and fills in.
    /// </summary>
    public static class LooksmithLookThumbnails
    {
        public const int Width = 320, Height = 200;
        public const string CacheFolder = "Library/LooksmithThumbnails";
        const int Version = 2;
        const string LitShader = "Looksmith/Looksmith Lit";

        static readonly Dictionary<string, Texture2D> s_Memory = new Dictionary<string, Texture2D>();
        static readonly List<VolumeProfile> s_Queue = new List<VolumeProfile>();
        static bool s_Hooked;

        /// <summary>Raised after a thumbnail finishes rendering (the gallery swaps it in).</summary>
        public static event Action<VolumeProfile> Rendered;

        /// <summary>Number of thumbnails waiting to render.</summary>
        public static int pending => s_Queue.Count;

        /// <summary>The thumbnail for a profile asset, or null while it renders (it is queued).</summary>
        public static Texture2D Get(VolumeProfile profile)
        {
            if (profile == null)
                return null;
            var key = Key(profile);
            if (s_Memory.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var file = CachePath(key);
            if (File.Exists(file))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = profile.name };
                if (tex.LoadImage(File.ReadAllBytes(file)))
                {
                    s_Memory[key] = tex;
                    return tex;
                }
                Object.DestroyImmediate(tex);
            }
            if (!s_Queue.Contains(profile))
                s_Queue.Add(profile);
            if (!s_Hooked)
            {
                EditorApplication.update += Tick;
                s_Hooked = true;
            }
            return null;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            s_Queue.RemoveAll(p => p == null);
            if (s_Queue.Count == 0)
            {
                EditorApplication.update -= Tick;
                s_Hooked = false;
                return;
            }
            var profile = s_Queue[0];
            s_Queue.RemoveAt(0);
            RenderAndCache(profile);
        }

        /// <summary>Renders a profile's thumbnail now and caches it (tests and tools; the gallery queues instead).</summary>
        public static Texture2D RenderAndCache(VolumeProfile profile)
        {
            var key = Key(profile);
            Texture2D tex;
            try
            {
                tex = Render(profile, Width, Height);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Looksmith: could not render the thumbnail for '{profile.name}': {e.Message}");
                return null;
            }
            if (s_Memory.TryGetValue(key, out var old) && old != null && old != tex)
                Object.DestroyImmediate(old);
            s_Memory[key] = tex;
            try
            {
                Directory.CreateDirectory(CacheFolder);
                foreach (var stale in Directory.GetFiles(CacheFolder, FilePrefix(profile) + "*.png"))
                    File.Delete(stale);   // a regenerable cache in Library/, not user data
                File.WriteAllBytes(CachePath(key), tex.EncodeToPNG());
            }
            catch (IOException)
            {
                // The in-memory copy still serves this session.
            }
            Rendered?.Invoke(profile);
            return tex;
        }

        /// <summary>Forgets every cached thumbnail (memory only; the files are replaced when a profile changes).</summary>
        public static void ClearMemory()
        {
            foreach (var t in s_Memory.Values)
                if (t != null)
                    Object.DestroyImmediate(t);
            s_Memory.Clear();
            s_Queue.Clear();
        }

        static string FilePrefix(VolumeProfile profile) =>
            string.Concat(profile.name.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + "_";

        /// <summary>Changes whenever the profile, the Looksmith Lit shader or the thumbnail recipe changes.</summary>
        static string Key(VolumeProfile profile)
        {
            var path = AssetDatabase.GetAssetPath(profile);
            var hash = new Hash128();
            hash.Append(Version);
            hash.Append(string.IsNullOrEmpty(path) ? LooksmithObjectIds.Get(profile) : AssetDatabase.GetAssetDependencyHash(path).ToString());
            var lit = Shader.Find(LitShader);
            if (lit != null)
                hash.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(lit)).ToString());
            return FilePrefix(profile) + hash;
        }

        static string CachePath(string key) => Path.Combine(CacheFolder, key + ".png");

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// Renders a profile on the diorama. The preview camera only sees the profile (its Volume sits on a layer no other
        /// Volume uses), so the open scenes' looks, Bypass, the A/B split and debug views never leak in.
        /// </summary>
        public static Texture2D Render(VolumeProfile profile, int width, int height)
        {
            var pru = new PreviewRenderUtility();
            var created = new List<Object>();
            bool asyncWas = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;   // a variant compiling in the background would draw nothing
            try
            {
                int layer = FreeVolumeLayer();
                BuildDiorama(pru, created);
                var cam = pru.camera;
                pru.BeginStaticPreview(new Rect(0, 0, width * 2, height * 2));
                LooksmithRuntime.previewCamera = cam;
                // Volumes register with the Volume framework, which URP initialises with the pipeline: right after a domain
                // reload (or in batchmode) nothing may have rendered yet, so render once before the Volume exists.
                if (!VolumeManager.instance.isInitialized)
                    cam.Render();

                var volumeGo = EditorUtility.CreateGameObjectWithHideFlags("Looksmith Thumbnail Look", HideFlags.HideAndDontSave, typeof(Volume));
                volumeGo.layer = layer;
                var volume = volumeGo.GetComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000f;
                volume.sharedProfile = profile;
                pru.AddSingleGO(volumeGo);

                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 60f;
                cam.transform.position = new Vector3(0.7f, 3.1f, -6.7f);
                cam.transform.LookAt(new Vector3(0.2f, 0.72f, 0.2f));
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SkyColor(profile);
                cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.volumeLayerMask = 1 << layer;
                data.volumeTrigger = cam.transform;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                pru.lights[0].type = LightType.Directional;
                pru.lights[0].intensity = 1.25f;
                pru.lights[0].color = Color.white;
                pru.lights[0].shadows = LightShadows.Soft;
                pru.lights[0].transform.rotation = Quaternion.Euler(42f, -38f, 0f);
                pru.lights[1].intensity = 0f;
                pru.ambientColor = Color.Lerp(SkyColor(profile), Color.white, 0.3f) * 0.55f;

                cam.Render();   // first frame: the camera's look is read; the second plans its passes from it
                cam.Render();
                var tex = pru.EndStaticPreview();
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.name = profile.name;
                return tex;
            }
            finally
            {
                LooksmithRuntime.previewCamera = null;
                ShaderUtil.allowAsyncCompilation = asyncWas;
                // The preview camera pushed its own globals; the open scenes' cameras push theirs again, but Bypass is a
                // global that styled cameras never re-set while it's on.
                Shader.SetGlobalFloat(LooksmithShaderIDs.Bypass, LooksmithRuntime.Bypass ? 1f : 0f);
                if (LooksmithRuntime.Bypass)
                    Shader.SetGlobalFloat(LooksmithShaderIDs.Enabled, 0f);
                pru.Cleanup();
                foreach (var o in created)
                    if (o != null)
                        Object.DestroyImmediate(o);
            }
        }

        /// <summary>A layer with no Volume on it in the open scenes, so only the thumbnail's own Volume reaches its camera.</summary>
        static int FreeVolumeLayer()
        {
            int fallback = 31;
            for (int i = 31; i >= 8; i--)
            {
                if (VolumeManager.instance.GetVolumes(1 << i).Length > 0)
                    continue;
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    return i;
                fallback = i;
            }
            return fallback;
        }

        static Color SkyColor(VolumeProfile profile)
        {
            if (profile.TryGet<LooksmithLook>(out var look))
                return LooksmithColor.OklchToColor(look.skyColor.value);
            return new Color(0.62f, 0.78f, 0.88f);
        }

        // The diorama: a grassy disc with a cottage, a tree, a figure, a rock and a ball, in plain painted colours.
        static void BuildDiorama(PreviewRenderUtility pru, List<Object> created)
        {
            var shader = Shader.Find(LitShader);
            if (shader == null)
                throw new InvalidOperationException("Looksmith Lit shader not found");
            Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name);
            var cube = Builtin("Cube.fbx");
            var sphere = Builtin("Sphere.fbx");
            var cylinder = Builtin("Cylinder.fbx");
            var capsule = Builtin("Capsule.fbx");

            void Add(string name, Mesh mesh, string hex, Vector3 position, Vector3 scale, Vector3 euler = default, float smoothness = 0.2f)
            {
                ColorUtility.TryParseHtmlString(hex, out var color);
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "Looksmith Thumbnail " + name };
                material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", smoothness);
                created.Add(material);
                var go = EditorUtility.CreateGameObjectWithHideFlags("Looksmith Thumbnail " + name, HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
                go.transform.localScale = scale;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                pru.AddSingleGO(go);
            }

            Add("Ground", cylinder, "#7DB35A", new Vector3(0f, -0.05f, 0f), new Vector3(6.4f, 0.05f, 6.4f));
            Add("Walls", cube, "#EDE3CF", new Vector3(-0.95f, 0.45f, 0.7f), new Vector3(1.15f, 0.9f, 1.0f), new Vector3(0f, 18f, 0f));
            Add("Roof", cube, "#C8553D", new Vector3(-0.95f, 0.9f, 0.7f), new Vector3(0.88f, 0.88f, 1.12f), new Vector3(0f, 18f, 45f));
            var house = Quaternion.Euler(0f, 18f, 0f);
            Add("Door", cube, "#6E4A33", new Vector3(-0.95f, 0.45f, 0.7f) + house * new Vector3(0.18f, -0.2f, -0.51f), new Vector3(0.26f, 0.5f, 0.04f), new Vector3(0f, 18f, 0f));
            Add("Trunk", cylinder, "#7A5236", new Vector3(1.05f, 0.4f, 0.95f), new Vector3(0.17f, 0.4f, 0.17f));
            Add("Canopy", sphere, "#4F9A45", new Vector3(1.05f, 1.2f, 0.95f), new Vector3(1.05f, 1.0f, 1.05f));
            Add("Canopy Small", sphere, "#6DB04C", new Vector3(1.45f, 0.95f, 0.55f), new Vector3(0.65f, 0.6f, 0.65f));
            Add("Figure", capsule, "#E59A3B", new Vector3(0.15f, 0.42f, -0.45f), new Vector3(0.32f, 0.42f, 0.32f));
            Add("Head", sphere, "#F1C7A5", new Vector3(0.15f, 0.98f, -0.45f), new Vector3(0.26f, 0.26f, 0.26f));
            Add("Rock", sphere, "#9A968F", new Vector3(-0.35f, 0.12f, -1.2f), new Vector3(0.62f, 0.36f, 0.5f), new Vector3(0f, 30f, 8f));
            Add("Ball", sphere, "#4A7BC8", new Vector3(1.25f, 0.22f, -0.75f), new Vector3(0.44f, 0.44f, 0.44f), default, 0.7f);
        }
    }
}
