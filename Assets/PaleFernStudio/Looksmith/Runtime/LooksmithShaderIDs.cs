using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>Shader property IDs for the Looksmith globals (prefix <c>_Looksmith</c>).</summary>
    public static class LooksmithShaderIDs
    {
        public static readonly int Enabled = Shader.PropertyToID("_LooksmithEnabled");
        public static readonly int Bypass = Shader.PropertyToID("_LooksmithBypass");
        public static readonly int DebugMode = Shader.PropertyToID("_LooksmithDebugMode");
        public static readonly int DepthAvailable = Shader.PropertyToID("_LooksmithDepthAvailable");
        public static readonly int PrepassDepth = Shader.PropertyToID("_LooksmithPrepassDepth");
        public static readonly int Split = Shader.PropertyToID("_LooksmithSplit");
        public static readonly int SplitSource = Shader.PropertyToID("_LooksmithSplitSource");
        public static readonly int DebugSurface = Shader.PropertyToID("_LooksmithDebugSurface");

        // Looksmith Sky (LooksmithSky.hlsl)
        public static readonly int SkyZenith = Shader.PropertyToID("_LooksmithSkyZenith");
        public static readonly int SkyHorizon = Shader.PropertyToID("_LooksmithSkyHorizon");
        public static readonly int SkyGround = Shader.PropertyToID("_LooksmithSkyGround");
        public static readonly int SkyGlow = Shader.PropertyToID("_LooksmithSkyGlow");
        public static readonly int SkyParams = Shader.PropertyToID("_LooksmithSkyParams");
        public static readonly int SunDir = Shader.PropertyToID("_LooksmithSunDir");
        public static readonly int SunColor = Shader.PropertyToID("_LooksmithSunColor");
        public static readonly int MoonDir = Shader.PropertyToID("_LooksmithMoonDir");
        public static readonly int MoonColor = Shader.PropertyToID("_LooksmithMoonColor");
        public static readonly int StarParams = Shader.PropertyToID("_LooksmithStarParams");
        public static readonly int CloudLit = Shader.PropertyToID("_LooksmithCloudLit");
        public static readonly int CloudShade = Shader.PropertyToID("_LooksmithCloudShade");
        public static readonly int CloudParams = Shader.PropertyToID("_LooksmithCloudParams");

        // Look Core (palette roles are linear RGB)
        public static readonly int KeyColor = Shader.PropertyToID("_LooksmithKeyColor");
        public static readonly int ShadowColor = Shader.PropertyToID("_LooksmithShadowColor");
        public static readonly int SkyColor = Shader.PropertyToID("_LooksmithSkyColor");
        public static readonly int AccentColor = Shader.PropertyToID("_LooksmithAccentColor");
        public static readonly int FogColor = Shader.PropertyToID("_LooksmithFogColor");
        public static readonly int LightParams = Shader.PropertyToID("_LooksmithLightParams");
        public static readonly int AmbientParams = Shader.PropertyToID("_LooksmithAmbientParams");
        public static readonly int UnityFog = Shader.PropertyToID("_LooksmithUnityFog");
        public static readonly int UnityFogColor = Shader.PropertyToID("_LooksmithUnityFogColor");
        public static readonly int RimParams = Shader.PropertyToID("_LooksmithRimParams");
        public static readonly int RimColor = Shader.PropertyToID("_LooksmithRimColor");
        public static readonly int SpecParams = Shader.PropertyToID("_LooksmithSpecParams");
        public static readonly int SpecColor = Shader.PropertyToID("_LooksmithSpecColor");
        public static readonly int TerminatorParams = Shader.PropertyToID("_LooksmithTerminatorParams");
        public static readonly int AtmosParams = Shader.PropertyToID("_LooksmithAtmosParams");
        public static readonly int AtmosParams2 = Shader.PropertyToID("_LooksmithAtmosParams2");
        public static readonly int SurfaceParams = Shader.PropertyToID("_LooksmithSurfaceParams");
        public static readonly int VertexParams = Shader.PropertyToID("_LooksmithVertexParams");
        public static readonly int RampAtlas = Shader.PropertyToID("_LooksmithRampAtlas");
        public static readonly int BrushNoise = Shader.PropertyToID("_LooksmithBrushNoise");

        // Lines (Outlines module)
        public static readonly int LineParams = Shader.PropertyToID("_LooksmithLineParams");
        public static readonly int LineParams2 = Shader.PropertyToID("_LooksmithLineParams2");
        public static readonly int LineParams3 = Shader.PropertyToID("_LooksmithLineParams3");
        public static readonly int LineColor = Shader.PropertyToID("_LooksmithLineColor");
        public static readonly int LineIds = Shader.PropertyToID("_LooksmithLineIds");
        public static readonly int LineIdParams = Shader.PropertyToID("_LooksmithLineIdParams");
        public static readonly int OutlineParams = Shader.PropertyToID("_LooksmithOutlineParams");

        // Selection highlight (Pro)
        public static readonly int HighlightColor = Shader.PropertyToID("_LooksmithHighlightColor");
        public static readonly int HighlightParams = Shader.PropertyToID("_LooksmithHighlightParams");
        public static readonly int HighlightMask = Shader.PropertyToID("_LooksmithHighlightMask");
        public static readonly int HighlightSeeds = Shader.PropertyToID("_LooksmithHighlightSeeds");
        public static readonly int JumpStep = Shader.PropertyToID("_LooksmithJumpStep");

        // Art Styles: stack blending, palettes and shared inputs. Style shaders read _LooksmithParams0..7 (each shader documents its own).
        public static readonly int StyleMix = Shader.PropertyToID("_LooksmithStyleMix");
        public static readonly int MixDepth = Shader.PropertyToID("_LooksmithMixDepth");
        public static readonly int MixSky = Shader.PropertyToID("_LooksmithMixSky");
        public static readonly int StyleLayers = Shader.PropertyToID("_LooksmithStyleLayers");
        public static readonly int StyleMask = Shader.PropertyToID("_LooksmithStyleMask");
        public static readonly int StyleMaskLayers = Shader.PropertyToID("_LooksmithStyleMaskLayers");
        public static readonly int Palette = Shader.PropertyToID("_LooksmithPalette");
        public static readonly int PaletteParams = Shader.PropertyToID("_LooksmithPaletteParams");
        public static readonly int PaletteLut = Shader.PropertyToID("_LooksmithPaletteLut");
        public static readonly int Paper = Shader.PropertyToID("_LooksmithPaper");
        public static readonly int GradingLut = Shader.PropertyToID("_LooksmithGradingLut");
        public static readonly int History = Shader.PropertyToID("_LooksmithHistory");
        public static readonly int StyleTexA = Shader.PropertyToID("_LooksmithStyleTexA");
        public static readonly int StyleTexB = Shader.PropertyToID("_LooksmithStyleTexB");
        public static readonly int StyleTexC = Shader.PropertyToID("_LooksmithStyleTexC");
        public static readonly int StyleTexD = Shader.PropertyToID("_LooksmithStyleTexD");
        public static readonly int[] Params =
        {
            Shader.PropertyToID("_LooksmithParams0"), Shader.PropertyToID("_LooksmithParams1"), Shader.PropertyToID("_LooksmithParams2"), Shader.PropertyToID("_LooksmithParams3"),
            Shader.PropertyToID("_LooksmithParams4"), Shader.PropertyToID("_LooksmithParams5"), Shader.PropertyToID("_LooksmithParams6"), Shader.PropertyToID("_LooksmithParams7"),
        };
        public static readonly int PixelOffset = Shader.PropertyToID("_LooksmithPixelOffset");

        // Water (Pro) and caustics (read by every Looksmith surface)
        public static readonly int WaterTime = Shader.PropertyToID("_LooksmithWaterTime");
        public static readonly int WaterInteraction = Shader.PropertyToID("_LooksmithWaterInteraction");
        public static readonly int WaterNormals = Shader.PropertyToID("_LooksmithWaterNormals");
        public static readonly int WaterFoam = Shader.PropertyToID("_LooksmithWaterFoam");
        public static readonly int InteractionField = Shader.PropertyToID("_LooksmithInteractionField");
        public static readonly int FieldStamps = Shader.PropertyToID("_LooksmithFieldStamps");
        public static readonly int FieldStampCount = Shader.PropertyToID("_LooksmithFieldStampCount");
        public static readonly int PlanarReflection = Shader.PropertyToID("_LooksmithPlanarReflection");
        public static readonly int PlanarReflectionParams = Shader.PropertyToID("_LooksmithPlanarReflectionParams");
        public static readonly int CausticParams = Shader.PropertyToID("_LooksmithCausticParams");
        public static readonly int CausticParams2 = Shader.PropertyToID("_LooksmithCausticParams2");
        public static readonly int CausticBounds = Shader.PropertyToID("_LooksmithCausticBounds");
        public static readonly int WaterPlane = Shader.PropertyToID("_LooksmithWaterPlane");
        public static readonly int OccluderDepth = Shader.PropertyToID("_LooksmithOccluderDepth");
        public static readonly int OccluderParams = Shader.PropertyToID("_LooksmithOccluderParams");
        public static readonly int SwayOffset = Shader.PropertyToID("_LooksmithSwayOffset");
        public static readonly int SwayParams = Shader.PropertyToID("_LooksmithSwayParams");
        public static readonly int WaterPlaneBounds = Shader.PropertyToID("_LooksmithWaterPlaneBounds");
        public static readonly int CausticTexture = Shader.PropertyToID("_LooksmithCausticTexture");

        // Grass Field (Pro)
        public static readonly int GrassTiles = Shader.PropertyToID("_GrassTiles");
        public static readonly int GrassTileStart = Shader.PropertyToID("_GrassTileStart");
        public static readonly int GrassKind = Shader.PropertyToID("_GrassKind");
        public static readonly int GrassPerTile = Shader.PropertyToID("_GrassPerTile");
        public static readonly int GrassTileSize = Shader.PropertyToID("_GrassTileSize");
        public static readonly int GrassTerrainOrigin = Shader.PropertyToID("_GrassTerrainOrigin");
        public static readonly int GrassTerrainSize = Shader.PropertyToID("_GrassTerrainSize");
        public static readonly int GrassLod = Shader.PropertyToID("_GrassLod");
        public static readonly int GrassHeights = Shader.PropertyToID("_GrassHeights");
        public static readonly int GrassDensity = Shader.PropertyToID("_GrassDensity");
        public static readonly int GrassBladeHeight = Shader.PropertyToID("_BladeHeight");
        public static readonly int GrassVergeHeight = Shader.PropertyToID("_VergeHeight");
        public static readonly int GrassDryHeight = Shader.PropertyToID("_DryHeight");
    }
}
