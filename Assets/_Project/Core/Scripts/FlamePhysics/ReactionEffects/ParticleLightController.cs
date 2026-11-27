using UnityEngine;

namespace Flame
{
    /// <summary>Flickers a point light while the attached particle system is playing.</summary>
    [DisallowMultipleComponent]
    public sealed class ParticleLightController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _particles;
        [SerializeField] private Light _light;
        [SerializeField, Min(0f)] private float _baseIntensity = 1.8f;
        [SerializeField, Range(0f, 1f)] private float _flickerAmount = 0.22f;
        [SerializeField, Min(0f)] private float _flickerSpeed = 15f;

        private void Awake()
        {
            if (_particles == null) _particles = GetComponent<ParticleSystem>();
            if (_light == null) _light = GetComponent<Light>();
            if (_light != null) _light.enabled = false;
        }

        private void Update()
        {
            if (_particles == null || _light == null) return;

            var shouldBeOn = _particles.isPlaying;
            if (_light.enabled != shouldBeOn) _light.enabled = shouldBeOn;
            if (!shouldBeOn) return;

            var noise = Mathf.PerlinNoise(Time.time * _flickerSpeed, 0.37f);
            _light.intensity = _baseIntensity * (1f + (noise * 2f - 1f) * _flickerAmount);
        }
    }
}
