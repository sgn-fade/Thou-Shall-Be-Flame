using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Marks someone else's water (a pack's lake, a water asset) as a surface for Looksmith's screen-space effects. Water is
    /// transparent, so the depth texture holds the bed under it: without this mark, outlines, art-style marks (hatching,
    /// paint, halftone), paper and fog treat the lake bed as the surface, and the marks slide through the water as the
    /// camera moves. Looksmith Water needs no mark. Nothing about the water itself changes.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Looksmith/Looksmith Water Plane")]
    public sealed class LooksmithWaterPlane : MonoBehaviour, ILooksmithWaterPlane
    {
        [Tooltip("Take the surface height from the top of this object's renderer (a flat water mesh). Off: use Height.")]
        public bool heightFromRenderer = true;
        [Tooltip("Height of the water surface (m), when not taken from the renderer.")]
        public float height;
        [Tooltip("Footprint size (m) when the object has no renderer; centred on it.")]
        public Vector2 size = new Vector2(100f, 100f);

        public float planeHeight
        {
            get
            {
                if (heightFromRenderer && TryGetComponent(out Renderer r))
                    return r.bounds.max.y;
                return height;
            }
        }

        public Rect planeFootprint
        {
            get
            {
                if (TryGetComponent(out Renderer r))
                {
                    var b = r.bounds;
                    return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                }
                var p = transform.position;
                return new Rect(p.x - size.x * 0.5f, p.z - size.y * 0.5f, size.x, size.y);
            }
        }

        void OnEnable() => LooksmithWaterPlanes.Register(this);
        void OnDisable() => LooksmithWaterPlanes.Unregister(this);

        void Reset()
        {
            if (TryGetComponent(out Renderer r))
                height = r.bounds.max.y;
            else
                height = transform.position.y;
        }
    }
}
