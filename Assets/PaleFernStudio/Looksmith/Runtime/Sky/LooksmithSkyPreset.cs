using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// A sky mood: the sky's colours, the sun or moon, stars, clouds, ambient light, fog and reflections. The Looksmith Sky
    /// component applies one (or blends two) so the sky and the scene's lighting always agree. Colours are sRGB, as
    /// a colour picker shows them.
    /// </summary>
    [CreateAssetMenu(fileName = "New Sky Preset", menuName = "Looksmith/Sky Preset", order = 121)]
    public sealed class LooksmithSkyPreset : ScriptableObject
    {
        [Header("Sky")]
        [Tooltip("Colour straight overhead.")]
        public Color zenith = new Color(0.29f, 0.56f, 0.83f);
        [Tooltip("Colour at the horizon.")]
        public Color horizon = new Color(0.75f, 0.87f, 0.94f);
        [Tooltip("Colour below the horizon (seen from hills, over water, past the edge of the world). Designed, never black.")]
        public Color ground = new Color(0.56f, 0.63f, 0.54f);
        [Tooltip("How quickly the horizon colour gives way to the zenith: low = a thin horizon band, high = a broad one.")]
        [Range(0.15f, 1.5f)] public float horizonCurve = 0.45f;
        [Tooltip("How far below the horizon the ground colour takes over.")]
        [Range(0.02f, 0.5f)] public float groundBlend = 0.12f;
        [Tooltip("Glow along the horizon on the sun's side (sunrise and sunset colour).")]
        public Color glow = new Color(1f, 0.85f, 0.65f);
        [Tooltip("Strength of the sun-side glow.")]
        [Range(0f, 1f)] public float glowStrength = 0.2f;
        [Tooltip("Brightness of the whole sky.")]
        [Range(0.2f, 2f)] public float exposure = 1f;

        [Header("Sun")]
        [Tooltip("Sun height above the horizon in degrees (negative = below it: the moon lights the scene).")]
        [Range(-90f, 90f)] public float sunElevation = 40f;
        [Tooltip("Sun direction around the horizon in degrees (0 = +Z, 90 = +X).")]
        [Range(0f, 360f)] public float sunAzimuth = 150f;
        [Tooltip("Colour of the sunlight (and the sun disc).")]
        public Color sunColor = new Color(1f, 0.96f, 0.9f);
        [Tooltip("Intensity of the sunlight.")]
        [Range(0f, 3f)] public float sunIntensity = 1.2f;
        [Tooltip("Size of the sun disc (degrees).")]
        [Range(0.2f, 4f)] public float sunSize = 1.2f;
        [Tooltip("Soft glow around the sun.")]
        [Range(0f, 2f)] public float sunHalo = 0.6f;
        [Tooltip("How dark the shadows of the sun or moon are.")]
        [Range(0f, 1f)] public float shadowStrength = 1f;
        [Tooltip("How much a Look's Key colour may tint this light: 1 by day, low at night so moonlight stays moonlight.")]
        [Range(0f, 1f)] public float lookKeyTint = 1f;

        [Header("Moon")]
        [Tooltip("Moon height above the horizon in degrees.")]
        [Range(-90f, 90f)] public float moonElevation = -30f;
        [Tooltip("Moon direction around the horizon in degrees.")]
        [Range(0f, 360f)] public float moonAzimuth = 200f;
        [Tooltip("Colour of the moon and of moonlight.")]
        public Color moonColor = new Color(0.75f, 0.82f, 1f);
        [Tooltip("Intensity of moonlight (used while the sun is below the horizon).")]
        [Range(0f, 1.5f)] public float moonIntensity = 0.3f;
        [Tooltip("Size of the moon disc (degrees).")]
        [Range(0.5f, 6f)] public float moonSize = 2f;
        [Tooltip("Moon phase: 0 = new moon, 0.5 = half, 1 = full.")]
        [Range(0f, 1f)] public float moonPhase = 0.8f;

        [Header("Stars")]
        [Tooltip("How many stars.")]
        [Range(0f, 1f)] public float starDensity = 0.6f;
        [Tooltip("Star brightness (0 = no stars, as by day).")]
        [Range(0f, 2f)] public float starBrightness;
        [Tooltip("How much the stars twinkle.")]
        [Range(0f, 1f)] public float twinkle = 0.5f;
        [Tooltip("A faint band of the galaxy across the night sky.")]
        [Range(0f, 1f)] public float milkyWay = 0.4f;

        [Header("Clouds")]
        [Tooltip("How much of the sky the clouds cover.")]
        [Range(0f, 1f)] public float cloudCover = 0.3f;
        [Tooltip("Cloud colour on the side facing the sun.")]
        public Color cloudLit = Color.white;
        [Tooltip("Cloud colour on the shaded side.")]
        public Color cloudShade = new Color(0.7f, 0.76f, 0.84f);
        [Tooltip("How solid the clouds are.")]
        [Range(0f, 1f)] public float cloudOpacity = 0.9f;
        [Tooltip("Cloud size: smaller values make bigger clouds.")]
        [Range(0.05f, 1f)] public float cloudScale = 0.3f;
        [Tooltip("How fast the clouds drift.")]
        [Range(0f, 0.2f)] public float cloudSpeed = 0.01f;
        [Tooltip("Softness of the cloud edges.")]
        [Range(0.01f, 0.4f)] public float cloudSoftness = 0.08f;
        [Tooltip("Stepped, banded shading on the clouds (0 = smooth).")]
        [Range(0, 6)] public int cloudSteps = 3;

        [Header("Ambient light")]
        [Tooltip("Ambient light from above.")]
        public Color ambientSky = new Color(0.55f, 0.72f, 0.88f);
        [Tooltip("Ambient light from the sides.")]
        public Color ambientEquator = new Color(0.7f, 0.78f, 0.8f);
        [Tooltip("Ambient light from below (bounced off the ground).")]
        public Color ambientGround = new Color(0.45f, 0.43f, 0.34f);
        [Tooltip("Overall ambient strength.")]
        [Range(0f, 3f)] public float ambientIntensity = 1f;

        [Header("Fog")]
        [Tooltip("Unity fog for this mood (mist, night fog). The Look's haze works on top of it.")]
        public bool fog;
        [Tooltip("Fog colour.")]
        public Color fogColor = new Color(0.7f, 0.75f, 0.8f);
        [Tooltip("Fog density (exponential squared).")]
        [Range(0f, 0.1f)] public float fogDensity = 0.01f;

        [Header("Reflections")]
        [Tooltip("Strength of the sky's reflection on shiny surfaces and water.")]
        [Range(0f, 2f)] public float reflectionIntensity = 1f;
    }
}
