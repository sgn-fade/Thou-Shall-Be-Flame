using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flame
{
    [Serializable]
    public class TemperatureEvent
    {
        [SerializeField] private float _requiredTemperature;
        [SerializeField] private bool _revertable;
        [SerializeField] private List<MaterialReactionEffect> _effects;

        public float RequiredTemperature => _requiredTemperature;

        public void PlayEffects()
        {
            ValidateEffects();
            foreach (var effect in _effects)
                effect.PlayEffect();
        }

        public void TryRevertEffects()
        {
            if (!_revertable) return;
            ValidateEffects();
            foreach (var effect in _effects)
                effect.ReverEffect();
        }

        public void UpdateEffectStrength(double temperature)
        {
            if (temperature < _requiredTemperature) return;
            ValidateEffects();
            foreach (var effect in _effects)
                effect.UpdateEffectStrength(temperature, _requiredTemperature);
        }

        private void ValidateEffects()
        {
            if (_effects == null || _effects.Count == 0)
                throw new InvalidOperationException("TemperatureEvent has no reaction effects configured.");

            for (var i = 0; i < _effects.Count; i++)
                if (_effects[i] == null)
                    throw new InvalidOperationException($"TemperatureEvent has a missing reaction effect at index {i}.");
        }
    }
}
