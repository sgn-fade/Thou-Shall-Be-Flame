using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>
    /// Grass, flowers and ferns for the demo: thousands of small meshes drawn with GPU instancing, in chunks culled per
    /// camera. Drawn from beginCameraRendering, so every camera sees them (game, scene, off-screen captures) and they go
    /// through the same passes as any other Looksmith Lit object (lines, outlines, looks).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Looksmith/Demo/Demo Foliage")]
    public sealed class DemoFoliage : MonoBehaviour
    {
        [Serializable]
        public class Kind
        {
            public string name;
            public Mesh mesh;
            public Material material;
            [Tooltip("Beyond this distance (m) the kind isn't drawn.")]
            public float drawDistance = 70f;
        }

        [Serializable]
        public struct Chunk
        {
            public int kind;
            public Bounds bounds;
            public int start, count;
        }

        public Kind[] kinds = Array.Empty<Kind>();
        [Tooltip("Density multiplier (0..1) applied at draw time; lower it on slow machines.")]
        [Range(0f, 1f)] public float density = 1f;
        [Tooltip("Grass casts shadows (costly; off by default).")]
        public bool castShadows;
        [Tooltip("The instances, packed (written by the demo builder); loaded when the component wakes up.")]
        public TextAsset data;

        // Instances: xyz position, w yaw (radians); tilt is (x, z) lean from the ground normal; scale per instance.
        // Kept at runtime only: the data asset stores them (SetInstances fills them for procedural use).
        [NonSerialized] Vector4[] m_Positions = Array.Empty<Vector4>();
        [NonSerialized] Vector2[] m_Tilts = Array.Empty<Vector2>();
        [NonSerialized] float[] m_Scales = Array.Empty<float>();
        [NonSerialized] Chunk[] m_Chunks = Array.Empty<Chunk>();

        Matrix4x4[][] m_Matrices;
        readonly Plane[] m_Planes = new Plane[6];

        public int instanceCount => m_Positions.Length;

        /// <summary>Replaces the instances (sorted into chunks of chunkSize metres).</summary>
        public void SetInstances(IList<(int kind, Vector3 position, float yaw, Vector2 tilt, float scale)> items, float chunkSize = 16f)
        {
            var buckets = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var key = (it.kind, Mathf.FloorToInt(it.position.x / chunkSize), Mathf.FloorToInt(it.position.z / chunkSize));
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new List<int>();
                list.Add(i);
            }
            // shuffle each bucket (deterministically): drawing the first n of a chunk then thins it evenly
            var rng = new System.Random(1234);
            foreach (var list in buckets.Values)
                for (int k = list.Count - 1; k > 0; k--)
                {
                    int j = rng.Next(k + 1);
                    (list[k], list[j]) = (list[j], list[k]);
                }
            var pos = new List<Vector4>(items.Count);
            var tilt = new List<Vector2>(items.Count);
            var scale = new List<float>(items.Count);
            var chunks = new List<Chunk>();
            foreach (var kv in buckets)
            {
                // the GPU takes at most 1023 instances per draw: split big buckets
                for (int s = 0; s < kv.Value.Count; s += 1023)
                {
                    int n = Mathf.Min(1023, kv.Value.Count - s);
                    var b = new Bounds(items[kv.Value[s]].position, Vector3.zero);
                    int start = pos.Count;
                    for (int k = 0; k < n; k++)
                    {
                        var it = items[kv.Value[s + k]];
                        pos.Add(new Vector4(it.position.x, it.position.y, it.position.z, it.yaw));
                        tilt.Add(it.tilt);
                        scale.Add(it.scale);
                        b.Encapsulate(it.position);
                    }
                    b.Expand(new Vector3(2f, 3f, 2f));
                    chunks.Add(new Chunk { kind = kv.Key.Item1, bounds = b, start = start, count = n });
                }
            }
            m_Positions = pos.ToArray();
            m_Tilts = tilt.ToArray();
            m_Scales = scale.ToArray();
            m_Chunks = chunks.ToArray();
            m_Matrices = null;
        }

        /// <summary>The instances as compact bytes (what <see cref="data"/> holds).</summary>
        public byte[] Pack()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(m_Chunks.Length);
            foreach (var c in m_Chunks)
            {
                w.Write(c.kind);
                w.Write(c.start);
                w.Write(c.count);
                w.Write(c.bounds.center.x); w.Write(c.bounds.center.y); w.Write(c.bounds.center.z);
                w.Write(c.bounds.size.x); w.Write(c.bounds.size.y); w.Write(c.bounds.size.z);
            }
            w.Write(m_Positions.Length);
            for (int i = 0; i < m_Positions.Length; i++)
            {
                var p = m_Positions[i];
                w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(p.w);
                w.Write(m_Tilts[i].x); w.Write(m_Tilts[i].y);
                w.Write(m_Scales[i]);
            }
            return ms.ToArray();
        }

        void Unpack(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            m_Chunks = new Chunk[r.ReadInt32()];
            for (int i = 0; i < m_Chunks.Length; i++)
            {
                int kind = r.ReadInt32(), start = r.ReadInt32(), count = r.ReadInt32();
                var centre = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var size = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m_Chunks[i] = new Chunk { kind = kind, start = start, count = count, bounds = new Bounds(centre, size) };
            }
            int n = r.ReadInt32();
            m_Positions = new Vector4[n];
            m_Tilts = new Vector2[n];
            m_Scales = new float[n];
            for (int i = 0; i < n; i++)
            {
                m_Positions[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m_Tilts[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                m_Scales[i] = r.ReadSingle();
            }
        }

        /// <summary>Drops the in-scene copy of the instances (they come from <see cref="data"/> instead).</summary>
        public void ClearSerialized()
        {
            m_Positions = Array.Empty<Vector4>();
            m_Tilts = Array.Empty<Vector2>();
            m_Scales = Array.Empty<float>();
            m_Chunks = Array.Empty<Chunk>();
            m_Matrices = null;
        }

        void OnEnable()
        {
            m_Matrices = null;
            if (data != null && m_Chunks.Length == 0)
                Unpack(data.bytes);
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        void OnValidate() => m_Matrices = null;

        void BuildMatrices()
        {
            m_Matrices = new Matrix4x4[m_Chunks.Length][];
            for (int c = 0; c < m_Chunks.Length; c++)
            {
                var ch = m_Chunks[c];
                var arr = new Matrix4x4[ch.count];
                for (int k = 0; k < ch.count; k++)
                {
                    var p = m_Positions[ch.start + k];
                    var t = m_Tilts[ch.start + k];
                    var rot = Quaternion.Euler(t.x, 0f, t.y) * Quaternion.Euler(0f, p.w * Mathf.Rad2Deg, 0f);
                    arr[k] = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), rot, Vector3.one * m_Scales[ch.start + k]);
                }
                m_Matrices[c] = arr;
            }
        }

        void Draw(ScriptableRenderContext context, Camera cam)
        {
            if (kinds.Length == 0 || m_Chunks.Length == 0 || density <= 0f)
                return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return;
            if ((cam.cullingMask & (1 << gameObject.layer)) == 0)
                return;
            if (m_Matrices == null || m_Matrices.Length != m_Chunks.Length)
                BuildMatrices();
            GeometryUtility.CalculateFrustumPlanes(cam, m_Planes);
            var camPos = cam.transform.position;
            for (int c = 0; c < m_Chunks.Length; c++)
            {
                var ch = m_Chunks[c];
                if (ch.kind < 0 || ch.kind >= kinds.Length)
                    continue;
                var kind = kinds[ch.kind];
                if (kind.mesh == null || kind.material == null || !kind.material.enableInstancing)
                    continue;
                float d = Mathf.Sqrt(ch.bounds.SqrDistance(camPos));
                if (d > kind.drawDistance || !GeometryUtility.TestPlanesAABB(m_Planes, ch.bounds))
                    continue;
                // thin out toward the draw distance so the edge doesn't pop as a line
                float fade = Mathf.Clamp01((kind.drawDistance - d) / (kind.drawDistance * 0.35f));
                int n = Mathf.CeilToInt(ch.count * density * Mathf.Lerp(0.35f, 1f, fade));
                if (n <= 0)
                    continue;
                var rp = new RenderParams(kind.material)
                {
                    camera = cam,
                    layer = gameObject.layer,
                    worldBounds = ch.bounds,
                    shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                Graphics.RenderMeshInstanced(rp, kind.mesh, 0, m_Matrices[c], n);
            }
        }
    }
}
