using System.Collections.Generic;
using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>A water surface the screen-space effects should treat as a surface: a height over a footprint.</summary>
    public interface ILooksmithWaterPlane
    {
        bool isActiveAndEnabled { get; }
        /// <summary>Height of the surface (m).</summary>
        float planeHeight { get; }
        /// <summary>World-space footprint (xz).</summary>
        Rect planeFootprint { get; }
    }

    /// <summary>
    /// The water surfaces Looksmith's screen-space effects treat as surfaces: lines, art-style marks, paper and fog. Water is
    /// transparent, so the depth texture holds the bed under it: without this, marks were printed on the lake bed and slid
    /// through the water as the camera moved. Looksmith Water registers itself; a Looksmith Water Plane marks anyone else's.
    /// Per camera, the one under the camera (else the nearest) goes to the shaders.
    /// </summary>
    public static class LooksmithWaterPlanes
    {
        static readonly List<ILooksmithWaterPlane> s_Planes = new List<ILooksmithWaterPlane>();

        public static IReadOnlyList<ILooksmithWaterPlane> all => s_Planes;

        public static void Register(ILooksmithWaterPlane plane)
        {
            if (plane != null && !s_Planes.Contains(plane))
                s_Planes.Add(plane);
        }

        public static void Unregister(ILooksmithWaterPlane plane) => s_Planes.Remove(plane);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Planes.Clear();

        /// <summary>The active plane under a position, else the nearest one (null when there's none).</summary>
        public static ILooksmithWaterPlane Nearest(Vector3 position)
        {
            ILooksmithWaterPlane best = null;
            float bestDistance = float.MaxValue;
            for (int i = s_Planes.Count - 1; i >= 0; i--)
            {
                var p = s_Planes[i];
                if (p == null || (p is Object o && o == null))
                {
                    s_Planes.RemoveAt(i);
                    continue;
                }
                if (!p.isActiveAndEnabled)
                    continue;
                var f = p.planeFootprint;
                float dx = Mathf.Max(f.xMin - position.x, 0f, position.x - f.xMax);
                float dz = Mathf.Max(f.yMin - position.z, 0f, position.z - f.yMax);
                float d = dx * dx + dz * dz;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Pushes the plane nearest a camera to the shaders (x height, y 1 while there is one; bounds xz).</summary>
        public static void Push(Camera camera)
        {
            var plane = camera != null ? Nearest(camera.transform.position) : null;
            if (plane == null)
            {
                Shader.SetGlobalVector(LooksmithShaderIDs.WaterPlane, Vector4.zero);
                return;
            }
            var f = plane.planeFootprint;
            Shader.SetGlobalVector(LooksmithShaderIDs.WaterPlane, new Vector4(plane.planeHeight, 1f, 0f, 0f));
            Shader.SetGlobalVector(LooksmithShaderIDs.WaterPlaneBounds, new Vector4(f.xMin, f.yMin, f.xMax, f.yMax));
        }
    }
}
