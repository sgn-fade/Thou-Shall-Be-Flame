using UnityEngine;
namespace Flame
{
[CreateAssetMenu(fileName = "New Flameable Material", menuName = "FlameableMaterial")]
    public class FlameableMaterial : ScriptableObject
    {
        [SerializeField] private bool _canBurn = true;

        [Tooltip("Temperature at which this material can start burning (K).")]
        [SerializeField] private float _ignitionTemperatureKelvin = 573.15f;

        [SerializeField] private double _maxTemperatureKelvin;
        [SerializeField] private bool _secondToTotalDestroyingWhileBurning;


        [Tooltip("Describes how efficiently a material emits thermal radiation. " +
                 "Values range from 0 (no emission) to 1 (ideal blackbody)."), Range(0, 1)]
        [SerializeField] private float _emissivity = 0.5f;

        [Tooltip("Energy required to heat 1 kg of material by 1 Kelvin (J/kg·K).")]
        [SerializeField] private float _specificHeatCapacity = 1700f;

        [Header("Burning")]
        [Tooltip("Seconds from ignition until the object is destroyed.")]
        [Min(0f)]
        [SerializeField] private float _burnDurationSeconds = 5f;



        public bool CanBurn => _canBurn;

        public float IgnitionTemperatureKelvin => _ignitionTemperatureKelvin;


        public float Emissivity => _emissivity;

        public float SpecificHeatCapacity => _specificHeatCapacity;

        public float BurnDurationSeconds => _burnDurationSeconds;

        public double MaxTemperature => _maxTemperatureKelvin;

        public bool SecondToTotalDestroyingWhileBurning => _secondToTotalDestroyingWhileBurning;
    }
}
