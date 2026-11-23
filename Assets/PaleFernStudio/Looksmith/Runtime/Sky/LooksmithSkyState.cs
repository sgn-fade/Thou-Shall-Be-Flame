using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// A sky preset resolved to linear colours and directions, so two moods can be blended smoothly (transitions, the
    /// day cycle).
    /// </summary>
    public struct LooksmithSkyState
    {
        public Color zenith, horizon, ground, glow;
        public float horizonCurve, groundBlend, glowStrength, exposure;
        public float sunElevation, sunAzimuth, sunIntensity, sunSize, sunHalo, shadowStrength, lookKeyTint;
        public Color sunColor;
        public float moonElevation, moonAzimuth, moonIntensity, moonSize, moonPhase;
        public Color moonColor;
        public float starDensity, starBrightness, twinkle, milkyWay;
        public float cloudCover, cloudOpacity, cloudScale, cloudSpeed, cloudSoftness, cloudSteps;
        public Color cloudLit, cloudShade;
        public Color ambientSky, ambientEquator, ambientGround;
        public float ambientIntensity;
        public float fog, fogDensity;
        public Color fogColor;
        public float reflectionIntensity;

        public static LooksmithSkyState From(LooksmithSkyPreset p) => new LooksmithSkyState
        {
            zenith = p.zenith.linear, horizon = p.horizon.linear, ground = p.ground.linear, glow = p.glow.linear,
            horizonCurve = p.horizonCurve, groundBlend = p.groundBlend, glowStrength = p.glowStrength, exposure = p.exposure,
            sunElevation = p.sunElevation, sunAzimuth = p.sunAzimuth, sunIntensity = p.sunIntensity, sunSize = p.sunSize,
            sunHalo = p.sunHalo, shadowStrength = p.shadowStrength, lookKeyTint = p.lookKeyTint, sunColor = p.sunColor.linear,
            moonElevation = p.moonElevation, moonAzimuth = p.moonAzimuth, moonIntensity = p.moonIntensity, moonSize = p.moonSize,
            moonPhase = p.moonPhase, moonColor = p.moonColor.linear,
            starDensity = p.starDensity, starBrightness = p.starBrightness, twinkle = p.twinkle, milkyWay = p.milkyWay,
            cloudCover = p.cloudCover, cloudOpacity = p.cloudOpacity, cloudScale = p.cloudScale, cloudSpeed = p.cloudSpeed,
            cloudSoftness = p.cloudSoftness, cloudSteps = p.cloudSteps, cloudLit = p.cloudLit.linear, cloudShade = p.cloudShade.linear,
            ambientSky = p.ambientSky.linear, ambientEquator = p.ambientEquator.linear, ambientGround = p.ambientGround.linear,
            ambientIntensity = p.ambientIntensity,
            fog = p.fog ? 1f : 0f, fogDensity = p.fogDensity, fogColor = p.fogColor.linear,
            reflectionIntensity = p.reflectionIntensity,
        };

        /// <summary>Blends two states (angles take the short way round; fog fades by density).</summary>
        public static LooksmithSkyState Lerp(in LooksmithSkyState a, in LooksmithSkyState b, float t)
        {
            t = Mathf.Clamp01(t);
            Color C(Color x, Color y) => Color.Lerp(x, y, t);
            float F(float x, float y) => Mathf.Lerp(x, y, t);
            float A(float x, float y) => Mathf.LerpAngle(x, y, t);
            // Fog switching on or off fades its density instead of popping.
            float fogA = a.fog > 0.5f ? a.fogDensity : 0f, fogB = b.fog > 0.5f ? b.fogDensity : 0f;
            return new LooksmithSkyState
            {
                zenith = C(a.zenith, b.zenith), horizon = C(a.horizon, b.horizon), ground = C(a.ground, b.ground), glow = C(a.glow, b.glow),
                horizonCurve = F(a.horizonCurve, b.horizonCurve), groundBlend = F(a.groundBlend, b.groundBlend),
                glowStrength = F(a.glowStrength, b.glowStrength), exposure = F(a.exposure, b.exposure),
                sunElevation = F(a.sunElevation, b.sunElevation), sunAzimuth = A(a.sunAzimuth, b.sunAzimuth),
                sunIntensity = F(a.sunIntensity, b.sunIntensity), sunSize = F(a.sunSize, b.sunSize), sunHalo = F(a.sunHalo, b.sunHalo),
                shadowStrength = F(a.shadowStrength, b.shadowStrength), lookKeyTint = F(a.lookKeyTint, b.lookKeyTint), sunColor = C(a.sunColor, b.sunColor),
                moonElevation = F(a.moonElevation, b.moonElevation), moonAzimuth = A(a.moonAzimuth, b.moonAzimuth),
                moonIntensity = F(a.moonIntensity, b.moonIntensity), moonSize = F(a.moonSize, b.moonSize),
                moonPhase = F(a.moonPhase, b.moonPhase), moonColor = C(a.moonColor, b.moonColor),
                starDensity = F(a.starDensity, b.starDensity), starBrightness = F(a.starBrightness, b.starBrightness),
                twinkle = F(a.twinkle, b.twinkle), milkyWay = F(a.milkyWay, b.milkyWay),
                cloudCover = F(a.cloudCover, b.cloudCover), cloudOpacity = F(a.cloudOpacity, b.cloudOpacity),
                cloudScale = F(a.cloudScale, b.cloudScale), cloudSpeed = F(a.cloudSpeed, b.cloudSpeed),
                cloudSoftness = F(a.cloudSoftness, b.cloudSoftness), cloudSteps = t < 0.5f ? a.cloudSteps : b.cloudSteps,
                cloudLit = C(a.cloudLit, b.cloudLit), cloudShade = C(a.cloudShade, b.cloudShade),
                ambientSky = C(a.ambientSky, b.ambientSky), ambientEquator = C(a.ambientEquator, b.ambientEquator),
                ambientGround = C(a.ambientGround, b.ambientGround), ambientIntensity = F(a.ambientIntensity, b.ambientIntensity),
                fog = fogA > 0f || fogB > 0f ? 1f : 0f, fogDensity = F(fogA, fogB),
                fogColor = a.fog < 0.5f ? b.fogColor : b.fog < 0.5f ? a.fogColor : C(a.fogColor, b.fogColor),
                reflectionIntensity = F(a.reflectionIntensity, b.reflectionIntensity),
            };
        }

        /// <summary>Unit direction toward a point at this elevation and azimuth (degrees).</summary>
        public static Vector3 Direction(float elevation, float azimuth)
        {
            float el = elevation * Mathf.Deg2Rad, az = azimuth * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }

        public Vector3 sunDirection => Direction(sunElevation, sunAzimuth);
        public Vector3 moonDirection => Direction(moonElevation, moonAzimuth);
    }
}
