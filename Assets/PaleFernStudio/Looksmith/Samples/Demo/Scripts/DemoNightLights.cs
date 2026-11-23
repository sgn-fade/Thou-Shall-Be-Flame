using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>
    /// Lights the village as the sun goes down: windows and lanterns glow (their emission maps), lantern and campfire
    /// lights come on, the fire flickers. Follows the Looksmith Sky's sun, so it works with presets and the day cycle alike.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Looksmith/Demo/Demo Night Lights")]
    public sealed class DemoNightLights : MonoBehaviour
    {
        [Serializable]
        public struct LightEntry
        {
            public Light light;
            public float intensity;
            [Tooltip("Flickers like a flame.")]
            public bool flicker;
        }

        public Renderer[] glowing = Array.Empty<Renderer>();
        public LightEntry[] lights = Array.Empty<LightEntry>();
        [Tooltip("Particle systems that only come out at night (fireflies): their emission follows the dark.")]
        public ParticleSystem[] nightParticles = Array.Empty<ParticleSystem>();
        [Tooltip("Particle systems that only show by day (falling leaves, butterflies).")]
        public ParticleSystem[] dayParticles = Array.Empty<ParticleSystem>();
        [Tooltip("Full emission rate of each night system (per second).")]
        public float[] nightRates = Array.Empty<float>();
        [Tooltip("Full emission rate of each day system (per second).")]
        public float[] dayRates = Array.Empty<float>();
        [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.62f, 0.3f) * 2.4f;
        [Tooltip("Sun elevation (degrees) where the lights are fully on / start to come on.")]
        public Vector2 elevationRange = new Vector2(-3f, 9f);
        [Tooltip("Glow kept by day (windows still catch a little warmth).")]
        [Range(0f, 1f)] public float dayGlow = 0f;

        MaterialPropertyBlock m_Block;
        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");
        float m_Last = -1f;

        public float night { get; private set; }

        static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null)
                return;
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        void OnEnable() => RenderPipelineManager.beginContextRendering += OnRender;
        void OnDisable() => RenderPipelineManager.beginContextRendering -= OnRender;

        void OnRender(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras) => Apply();

        public void Apply()
        {
            float elevation = 30f;
            var sky = LooksmithSky.active;
            if (sky != null)
                elevation = sky.state.sunElevation;
            else if (RenderSettings.sun != null)
                elevation = -Mathf.Asin(Mathf.Clamp(RenderSettings.sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            night = Mathf.Max(dayGlow, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(elevationRange.y, elevationRange.x, elevation)));

            if (!Mathf.Approximately(night, m_Last))
            {
                m_Last = night;
                m_Block ??= new MaterialPropertyBlock();
                var c = glowColor * night;
                foreach (var r in glowing)
                {
                    if (r == null)
                        continue;
                    r.GetPropertyBlock(m_Block);
                    m_Block.SetColor(k_Emission, c);
                    r.SetPropertyBlock(m_Block);
                }
            }
            for (int i = 0; i < nightParticles.Length; i++)
                SetRate(nightParticles[i], (i < nightRates.Length ? nightRates[i] : 5f) * night);
            for (int i = 0; i < dayParticles.Length; i++)
                SetRate(dayParticles[i], (i < dayRates.Length ? dayRates[i] : 1f) * (1f - night));
            float t = Application.isPlaying ? Time.time : (float)(LooksmithSky.clock % 1000.0);
            for (int i = 0; i < lights.Length; i++)
            {
                var e = lights[i];
                if (e.light == null)
                    continue;
                float f = e.flicker ? 0.82f + 0.18f * Mathf.PerlinNoise(t * 3.1f, i * 7.3f) + 0.06f * Mathf.Sin(t * 23f + i) : 1f;
                e.light.intensity = e.intensity * night * f;
                e.light.enabled = night > 0.01f;
            }
        }
    }
}
