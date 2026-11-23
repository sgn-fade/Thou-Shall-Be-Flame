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
            emission.rateOverTime = Mathf.Lerp(_minParticlesPerSecond, _maxParticlesPerSecond, strength);
        }
    }
}
