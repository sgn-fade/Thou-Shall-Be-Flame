using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flame
{
    public class ReactionManager : MonoBehaviour
    {
        [SerializeField] private List<MaterialReactionEffect> _effects;


        private Flameable _flameable;


        private void Awake()
        {
            _flameable = GetComponent<Flameable>();
            _flameable.TemperatureChanged += FlameableOnTemperatureChanged;
        }

        private void FlameableOnTemperatureChanged(double value)
        {

        }
    }
}