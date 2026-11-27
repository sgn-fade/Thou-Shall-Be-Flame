using System;
using UnityEngine;

namespace Flame
{
    public class ParticleEmitter : MaterialReactionEffect
    {
        [SerializeField] private ParticleSystem _particleSystem;
        [SerializeField] private float _minParticlesPerSecond;
        [SerializeField] private float _maxParticlesPerSecond = 100f;
        [SerializeField] private bool _updateOverTime = true;
        [SerializeField, Min(0.01f)] private float _referenceMass = 1f;
        [SerializeField, Min(0f)] private float _massScaleExponent = 0.5f;
        [SerializeField, Min(0f)] private float _maximumMassMultiplier = 4f;
        private Rigidbody _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        public override void PlayEffect()
        {
            _particleSystem.Play();
        }

        public override void ReverEffect()
        {
            _particleSystem.Stop();
        }

        public override void UpdateEffectStrength(double temperature, float minTemperature)
        {
            if (!_updateOverTime) return;


            if (_particleSystem == null)
                throw new MissingReferenceException($"{nameof(ParticleEmitter)} on '{name}' has no ParticleSystem assigned.");

            var temperatureRange = MaxTemperature - minTemperature;
            var strength = temperatureRange <= 0f
                ? (temperature >= MaxTemperature ? 1f : 0f)
                : Mathf.Clamp01((float)(temperature - minTemperature) / temperatureRange);

            var emission = _particleSystem.emission;
            var mass = _rigidbody != null ? Mathf.Max(_rigidbody.mass, 0.01f) : _referenceMass;
            var massMultiplier = Mathf.Clamp(
                Mathf.Pow(mass / Mathf.Max(_referenceMass, 0.01f), _massScaleExponent),
                0f,
                _maximumMassMultiplier);
            emission.rateOverTime = Mathf.Lerp(_minParticlesPerSecond, _maxParticlesPerSecond, strength) *
                                    massMultiplier;
        }
    }
}
