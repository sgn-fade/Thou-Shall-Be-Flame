using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Holds every hidden shader and texture Looksmith needs at runtime, so builds include them
    /// without Shader.Find or "Always Included" edits (P1). Lives in Resources/Looksmith/.
    /// </summary>
    public sealed class LooksmithRuntimeResources : ScriptableObject
    {
        public const string ResourcePath = "Looksmith/LooksmithRuntimeResources";

        [Tooltip("Debug views (Value Check, Lines Only, normals, depth...).")]
        public Shader debugViewShader;
        [Tooltip("Pro: occluded-object silhouettes (Looksmith See-Through). Empty in Lite.")]
        public Shader seeThroughShader;
        [Tooltip("Edge-detection lines (depth and normals; colour and object edges in Pro).")]
        public Shader linesShader;
        [Tooltip("Wind offsets of swaying surfaces, so paper and brush strokes laid on them ride the leaves.")]
        public Shader swayShader;
        [Tooltip("Pro: object-ID and line-layer buffer for object edges and layer masks. Empty in Lite.")]
        public Shader lineIdsShader;
        [Tooltip("Pro: inverted-hull object outlines. Empty in Lite.")]
        public Shader outlineHullShader;
        [Tooltip("Pro: jump-flood selection highlight. Empty in Lite.")]
        public Shader highlightShader;

        [Header("Art Styles")]
        [Tooltip("Posterize & Palette, and the palette lookup-table bake.")]
        public Shader posterizeShader;
        [Tooltip("The Look's Finish (grain, paper, vignette, grading LUT, chromatic fringe).")]
        public Shader finishShader;
        [Tooltip("Pro: rendering-layer mask buffer for per-layer styles. Empty in Lite.")]
        public Shader styleMaskShader;
        [Tooltip("Pro styles. Empty in Lite.")]
        public Shader painterlyShader;
        [Tooltip("Pro style. Empty in Lite.")]
        public Shader watercolourShader;
        [Tooltip("Pro style. Empty in Lite.")]
        public Shader hatchingShader;
        [Tooltip("Pro style. Empty in Lite.")]
        public Shader comicShader;
        [Tooltip("Pro style. Empty in Lite.")]
        public Shader pixelArtShader;
        [Tooltip("Pro style. Empty in Lite.")]
        public Shader retroShader;

        [Header("Water")]
        [Tooltip("Pro: the Interaction Field's ripple simulation. Empty in Lite.")]
        public Shader interactionFieldShader;

        [Header("Atmosphere")]
        [Tooltip("Pro: fog, mist, volumetric light and ambient occlusion (Looksmith Atmosphere). Empty in Lite.")]
        public Shader atmosphereShader;

        /// <summary>
        /// Rendering layers Looksmith reserves for Pro outlines and the selection highlight. Bits nobody sets by default;
        /// change them here if your project already uses these layers.
        /// </summary>
        [Tooltip("Rendering layer the Looksmith Outline component sets (Pro hull outlines).")]
        [Range(1, 31)] public int outlineRenderingLayer = 20;
        [Tooltip("Rendering layer the selection highlight sets (Pro).")]
        [Range(1, 31)] public int highlightRenderingLayer = 21;

        public uint OutlineLayerMask => 1u << outlineRenderingLayer;
        public uint HighlightLayerMask => 1u << highlightRenderingLayer;
        [Tooltip("Brush-stroke texture for the Look's Painted light.")]
        public Texture2D brushNoise;

        [Header("Yarn")]
        [Tooltip("Pro: the Yarn surface (knitted). Empty in Lite.")]
        public Shader yarnShader;
        [Tooltip("Pro: one modelled stitch of stockinette, baked (Art~/Blender/build_knit_tile.py): normal, height, occlusion. Empty in Lite.")]
        public Texture2D yarnShape;
        [Tooltip("Pro: the same stitch's yarn: which stitch and ply, the fibres' direction, round the ply. Empty in Lite.")]
        public Texture2D yarnIds;
        [Tooltip("The baked stitch: its row height, its depth and the yarn's radius, in stitch widths (KnitTile.json).")]
        public Vector4 yarnTileInfo = new Vector4(0.67f, 1.0093f, 0.21f, 0f);

        static LooksmithRuntimeResources s_Instance;

        public static LooksmithRuntimeResources Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = Resources.Load<LooksmithRuntimeResources>(ResourcePath);
                return s_Instance;
            }
        }
    }
}
