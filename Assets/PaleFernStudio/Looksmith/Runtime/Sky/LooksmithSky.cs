using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The scene's sky: applies a Sky Preset (or a smooth transition between two) to the Looksmith Sky skybox, the sun or
    /// moon light, ambient light, fog and a reflection cubemap made from the sky, so everything agrees with the sky.
    /// Looks keep working at any time of day: with a Looksmith Sky in the scene, a Look's haze follows the sky's horizon.
    /// What the scene had before is put back when the component is removed or disabled.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Looksmith/Looksmith Sky")]
    public sealed class LooksmithSky : MonoBehaviour
    {
        [Tooltip("The sky mood to show.")]
        public LooksmithSkyPreset preset;
        [Tooltip("The directional light the sun (by day) or the moon (by night) drives.")]
        public Light sun;
        [Tooltip("The Looksmith Sky skybox material (shared; the component never edits it).")]
        public Material skyMaterial;
        [Tooltip("Drive the scene's ambient light from the preset.")]
        public bool applyAmbient = true;
        [Tooltip("Drive Unity fog from the preset.")]
        public bool applyFog = true;
        [Tooltip("Drive reflections with a cubemap made from the sky.")]
        public bool applyReflections = true;

        // What the scene had before the sky took over, put back on disable.
        [SerializeField, HideInInspector] bool m_Captured;
        [SerializeField, HideInInspector] Material m_PrevSkybox;
        [SerializeField, HideInInspector] AmbientMode m_PrevAmbientMode;
        [SerializeField, HideInInspector] Color m_PrevAmbientSky, m_PrevAmbientEquator, m_PrevAmbientGround, m_PrevFogColor;
        [SerializeField, HideInInspector] float m_PrevAmbientIntensity, m_PrevFogDensity, m_PrevReflectionIntensity;
        [SerializeField, HideInInspector] bool m_PrevFog;
        [SerializeField, HideInInspector] FogMode m_PrevFogMode;
        [SerializeField, HideInInspector] DefaultReflectionMode m_PrevReflectionMode;

        LooksmithSkyState m_State, m_From, m_To;
        double m_TransitionStart;
        float m_TransitionLength;
        bool m_Transitioning, m_HasState, m_OverrideState;
        LooksmithSkyPreset m_AppliedPreset;
        Cubemap m_Reflection;
        int m_ReflectionHash;

        static double s_ClockOverride = -1;

        /// <summary>The Looksmith Sky driving the scene (the last one enabled), or null.</summary>
        public static LooksmithSky active { get; private set; }

        /// <summary>The sky as it is right now (after transitions).</summary>
        public LooksmithSkyState state => m_State;

        /// <summary>True while a transition is running.</summary>
        public bool transitioning => m_Transitioning;

        /// <summary>Seconds on the sky clock (clouds, twinkle). Freeze it for captures with <see cref="SetClockOverride"/>.</summary>
        public static double clock
        {
            get
            {
                if (s_ClockOverride >= 0)
                    return s_ClockOverride;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return UnityEditor.EditorApplication.timeSinceStartup % 3600.0;
#endif
                return Time.timeAsDouble % 3600.0;
            }
        }

        /// <summary>Freezes the sky clock (captures, tests). A negative value releases it.</summary>
        public static void SetClockOverride(double seconds) => s_ClockOverride = seconds;

        /// <summary>Raised after the sky changes the scene's lighting (the water and tools listen).</summary>
        public static event Action Applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active = null;
            s_ClockOverride = -1;
        }

        void OnEnable()
        {
            active = this;
            Capture();
            m_HasState = false;
            Apply();
        }

        void OnDisable()
        {
            if (active == this)
                active = null;
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyHorizon, Vector4.zero);
            m_PushedHash = 0;
            Restore();
            if (m_Reflection != null)
                CoreUtils.Destroy(m_Reflection);
            m_Reflection = null;
            m_ReflectionHash = 0;
        }

        void OnValidate()
        {
            m_HasState = false;
            m_PushedHash = 0;
        }

        void Update()
        {
            if (preset == null && !m_OverrideState)
                return;
            Apply();
        }

        /// <summary>Blends from the current sky to a preset over a number of seconds (0 = at once).</summary>
        public void TransitionTo(LooksmithSkyPreset target, float seconds)
        {
            if (target == null)
                return;
            m_OverrideState = false;
            if (seconds <= 0f || !m_HasState)
            {
                preset = target;
                m_Transitioning = false;
                m_HasState = false;
                Apply();
                return;
            }
            m_From = m_State;
            m_To = LooksmithSkyState.From(target);
            preset = target;
            m_AppliedPreset = target;
            m_TransitionStart = clock;
            m_TransitionLength = seconds;
            m_Transitioning = true;
        }

        /// <summary>Shows an explicit sky state (the day cycle drives the sky this way). Call every frame it changes.</summary>
        public void SetState(in LooksmithSkyState s)
        {
            m_OverrideState = true;
            m_Transitioning = false;
            m_State = s;
            m_HasState = true;
            Push();
        }

        /// <summary>This sky sets the scene's Unity fog (for non-Looksmith materials; Looksmith surfaces take the Look's haze).</summary>
        public bool drivesFog => isActiveAndEnabled && applyFog && (preset != null || m_OverrideState);

        /// <summary>Applies the preset (or the running transition) now.</summary>
        public void Apply()
        {
            if (m_OverrideState)
            {
                Push();
                return;
            }
            if (preset == null)
                return;
            if (m_Transitioning)
            {
                double t = (clock - m_TransitionStart) / Mathf.Max(m_TransitionLength, 1e-3f);
                if (t < 0)
                    t = 1;   // the clock wrapped
                float s = Mathf.SmoothStep(0f, 1f, (float)Math.Min(t, 1.0));
                m_State = LooksmithSkyState.Lerp(m_From, m_To, s);
                if (t >= 1)
                    m_Transitioning = false;
            }
            else if (!m_HasState || m_AppliedPreset != preset)
            {
                m_State = LooksmithSkyState.From(preset);
                m_AppliedPreset = preset;
            }
            m_HasState = true;
            Push();
        }

        int m_PushedHash;

        void Push()
        {
            var s = m_State;
            // The clock ticks every frame; everything else only when the sky changed (edit-mode writes to the light or
            // RenderSettings every frame would keep the scene dirty).
            int hash = LooksmithSkyMath.Hash(s) ^ (LooksmithObjectIds.Hash(sun) * 31) ^ LooksmithObjectIds.Hash(skyMaterial);
            if (hash == m_PushedHash && Shader.GetGlobalVector(LooksmithShaderIDs.SkyHorizon).w > 0.5f)
            {
                var p = Shader.GetGlobalVector(LooksmithShaderIDs.SkyParams);
                p.w = (float)clock;
                Shader.SetGlobalVector(LooksmithShaderIDs.SkyParams, p);
                return;
            }
            m_PushedHash = hash;
            var sunDir = s.sunDirection;
            var moonDir = s.moonDirection;
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyZenith, s.zenith);
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyHorizon, new Vector4(s.horizon.r, s.horizon.g, s.horizon.b, 1f));
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyGround, new Vector4(s.ground.r, s.ground.g, s.ground.b, s.lookKeyTint));
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyGlow, new Vector4(s.glow.r, s.glow.g, s.glow.b, s.glowStrength));
            Shader.SetGlobalVector(LooksmithShaderIDs.SkyParams, new Vector4(s.horizonCurve, s.exposure, s.groundBlend, (float)clock));
            Shader.SetGlobalVector(LooksmithShaderIDs.SunDir, new Vector4(sunDir.x, sunDir.y, sunDir.z, s.sunSize * Mathf.Deg2Rad));
            Shader.SetGlobalVector(LooksmithShaderIDs.SunColor, new Vector4(s.sunColor.r, s.sunColor.g, s.sunColor.b, s.sunHalo));
            Shader.SetGlobalVector(LooksmithShaderIDs.MoonDir, new Vector4(moonDir.x, moonDir.y, moonDir.z, s.moonSize * Mathf.Deg2Rad));
            float moonVisible = s.moonElevation > -5f ? 1f : 0f;
            Shader.SetGlobalVector(LooksmithShaderIDs.MoonColor, new Vector4(s.moonColor.r * moonVisible, s.moonColor.g * moonVisible, s.moonColor.b * moonVisible, s.moonPhase));
            Shader.SetGlobalVector(LooksmithShaderIDs.StarParams, new Vector4(s.starDensity, s.starBrightness, s.twinkle, s.milkyWay));
            Shader.SetGlobalVector(LooksmithShaderIDs.CloudLit, new Vector4(s.cloudLit.r, s.cloudLit.g, s.cloudLit.b, s.cloudCover));
            Shader.SetGlobalVector(LooksmithShaderIDs.CloudShade, new Vector4(s.cloudShade.r, s.cloudShade.g, s.cloudShade.b, s.cloudOpacity));
            Shader.SetGlobalVector(LooksmithShaderIDs.CloudParams, new Vector4(s.cloudScale, s.cloudSpeed, s.cloudSoftness, s.cloudSteps));

            if (skyMaterial != null && RenderSettings.skybox != skyMaterial)
                RenderSettings.skybox = skyMaterial;

            if (sun != null)
            {
                // One light: the sun by day, the moon by night, fading through zero at the horizon so the switch never shows.
                bool day = s.sunElevation > -1f;
                var dir = day ? sunDir : moonDir;
                float intensity = day
                    ? s.sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 4f, s.sunElevation))
                    : s.moonIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, -6f, s.sunElevation)) * Mathf.Clamp01((s.moonElevation + 2f) / 8f);
                sun.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                sun.color = (day ? s.sunColor : s.moonColor).gamma;
                sun.intensity = intensity;
                sun.shadowStrength = s.shadowStrength;
            }

            if (applyAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = (s.ambientSky * s.ambientIntensity).gamma;
                RenderSettings.ambientEquatorColor = (s.ambientEquator * s.ambientIntensity).gamma;
                RenderSettings.ambientGroundColor = (s.ambientGround * s.ambientIntensity).gamma;
            }
            if (applyFog)
            {
                RenderSettings.fog = s.fog > 0.5f && s.fogDensity > 1e-5f;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = s.fogColor.gamma;
                RenderSettings.fogDensity = s.fogDensity;
            }
            if (applyReflections)
                UpdateReflection(s);
            Applied?.Invoke();
        }

        void UpdateReflection(in LooksmithSkyState s)
        {
            int hash = LooksmithSkyMath.Hash(s);
            if (m_Reflection == null)
            {
                m_Reflection = new Cubemap(LooksmithSkyMath.ReflectionSize, TextureFormat.RGBAHalf, false)
                    { name = "Looksmith Sky Reflection", hideFlags = HideFlags.HideAndDontSave };
                m_ReflectionHash = 0;
            }
            if (hash != m_ReflectionHash)
            {
                LooksmithSkyMath.Fill(m_Reflection, s);
                m_ReflectionHash = hash;
            }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            if (RenderSettings.customReflectionTexture != m_Reflection)
                RenderSettings.customReflectionTexture = m_Reflection;
            RenderSettings.reflectionIntensity = s.reflectionIntensity;
        }

        void Capture()
        {
            if (m_Captured)
                return;
            m_PrevSkybox = RenderSettings.skybox;
            m_PrevAmbientMode = RenderSettings.ambientMode;
            m_PrevAmbientSky = RenderSettings.ambientSkyColor;
            m_PrevAmbientEquator = RenderSettings.ambientEquatorColor;
            m_PrevAmbientGround = RenderSettings.ambientGroundColor;
            m_PrevAmbientIntensity = RenderSettings.ambientIntensity;
            m_PrevFog = RenderSettings.fog;
            m_PrevFogMode = RenderSettings.fogMode;
            m_PrevFogColor = RenderSettings.fogColor;
            m_PrevFogDensity = RenderSettings.fogDensity;
            m_PrevReflectionMode = RenderSettings.defaultReflectionMode;
            m_PrevReflectionIntensity = RenderSettings.reflectionIntensity;
            m_Captured = true;
        }

        void Restore()
        {
            if (!m_Captured)
                return;
            RenderSettings.skybox = m_PrevSkybox;
            if (applyAmbient)
            {
                RenderSettings.ambientMode = m_PrevAmbientMode;
                RenderSettings.ambientSkyColor = m_PrevAmbientSky;
                RenderSettings.ambientEquatorColor = m_PrevAmbientEquator;
                RenderSettings.ambientGroundColor = m_PrevAmbientGround;
                RenderSettings.ambientIntensity = m_PrevAmbientIntensity;
            }
            if (applyFog)
            {
                RenderSettings.fog = m_PrevFog;
                RenderSettings.fogMode = m_PrevFogMode;
                RenderSettings.fogColor = m_PrevFogColor;
                RenderSettings.fogDensity = m_PrevFogDensity;
            }
            if (applyReflections)
            {
                RenderSettings.defaultReflectionMode = m_PrevReflectionMode;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.reflectionIntensity = m_PrevReflectionIntensity;
            }
        }
    }
}
