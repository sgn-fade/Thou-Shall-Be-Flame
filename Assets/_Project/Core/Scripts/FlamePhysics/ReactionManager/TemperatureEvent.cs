using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flame
{
    [Serializable]
    public class TemperatureEvent
    {
        [SerializeField] private float _temperature;
        [SerializeField] private bool _revertable;
        [SerializeField] private List<MaterialReactionEffect> _effects;
    }
}
