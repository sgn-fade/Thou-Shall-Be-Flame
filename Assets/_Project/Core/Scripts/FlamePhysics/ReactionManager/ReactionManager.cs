using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Flame
{
    public class ReactionManager : MonoBehaviour
    {
        [SerializeField] private List<TemperatureEvent> _events;
        [SerializeField] private UnityEvent _destroyEvent;

        private Flameable _flameable;


        private void Awake()
        {
            _flameable = GetComponent<Flameable>();
            if (_flameable == null)
                throw new MissingComponentException($"{nameof(ReactionManager)} on '{name}' requires a {nameof(Flameable)} component.");
            if (_events == null || _events.Count == 0)
                throw new InvalidOperationException($"{nameof(ReactionManager)} on '{name}' has no temperature events configured.");

            for (var i = 0; i < _events.Count; i++)
                if (_events[i] == null)
                    throw new InvalidOperationException($"{nameof(ReactionManager)} on '{name}' has a missing temperature event at index {i}.");

            _flameable.TemperatureChanged += FlameableOnTemperatureChanged;
            _flameable.Burned += FlameableOnBurned;
        }

        private void FlameableOnBurned()
        {
            if (_destroyEvent == null) return;

            _destroyEvent?.Invoke();
        }

        private void OnDestroy()
        {
            if (_flameable != null)
            {
                _flameable.TemperatureChanged -= FlameableOnTemperatureChanged;
                _flameable.Burned -= FlameableOnBurned;
            }
        }

        private void FlameableOnTemperatureChanged(double oldValue, double newValue)
        {
            foreach (var temperatureEvent in _events)
            {
                if (newValue >= temperatureEvent.RequiredTemperature)
                    temperatureEvent.UpdateEffectStrength(newValue);

                if (oldValue < temperatureEvent.RequiredTemperature && newValue >= temperatureEvent.RequiredTemperature)
                    temperatureEvent.PlayEffects();
                else if (oldValue >= temperatureEvent.RequiredTemperature && newValue < temperatureEvent.RequiredTemperature)
                    temperatureEvent.TryRevertEffects();
            }
        }

    }
}
