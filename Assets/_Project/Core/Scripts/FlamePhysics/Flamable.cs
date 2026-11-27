using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Flame
{
public class Flameable : MonoBehaviour
    {
        private const float HeatReceivedTimeoutSeconds = 0.6f;
        [SerializeField] private FlameableMaterial _material;
        [SerializeField] private float _emitRadius;
        [SerializeField] private float _surface;



        private readonly double sigma = 5.67 * Math.Pow(10, -8);
        private readonly double environmentTemperatureKelvin = 273.15;


        private bool _isBurning;
        private double _receivedTemperatureDelta;
        private double _lostTemperatureDelta;
        private double _currentTemperatureKelvin;
        private float _lastHeatReceivedTime = float.NegativeInfinity;


        public event Action<double, double> TemperatureChanged;
        public event Action Burned;
        public bool IsReceivingHeat => Time.time - _lastHeatReceivedTime <= HeatReceivedTimeoutSeconds;


        private double CurrentTemperatureKelvin
        {
            get  => _currentTemperatureKelvin;
            set
            {
                TemperatureChanged?.Invoke(_currentTemperatureKelvin, value);
                _currentTemperatureKelvin = value;
            }
        }


        private Rigidbody _rigidBody;
        private readonly List<Flameable> _flameables = new();



        public void Burn()
        {
            if (!_material.CanBurn) return;

            _isBurning = true;
            CurrentTemperatureKelvin = _material.MaxTemperature;
            StartCoroutine(DestroyCoroutine());
        }

        private void Awake()
        {
            CurrentTemperatureKelvin = environmentTemperatureKelvin;
            _rigidBody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            StartCoroutine(EmitHeat());
        }

        private IEnumerator EmitHeat()
        {
            while (true)
            {
                var sourceTemperature = CurrentTemperatureKelvin;
                var emissionFactor = _material.Emissivity * _surface * sigma * 0.5;
                var emittedEnergy = Math.Max(
                    0d,
                    Math.Pow(sourceTemperature, 4) - Math.Pow(environmentTemperatureKelvin, 4)
                ) * emissionFactor;

                if (!_isBurning)
                    LoseHeat(emittedEnergy);

                foreach (var flameable in _flameables)
                {
                    if (flameable == null || flameable == this)
                        continue;

                    float distance = Vector3.Distance(
                        transform.position,
                        flameable.transform.position
                    );

                    float heatFactor = Mathf.Clamp01(1f - distance / _emitRadius);

                    double temperatureDifference = Math.Pow(sourceTemperature, 4) -
                                                   Math.Pow(flameable.CurrentTemperatureKelvin, 4);
                    double receivedEnergy = Math.Max(0d, temperatureDifference) *
                                            emissionFactor * heatFactor;

                    flameable.ConsumeHeat(receivedEnergy);
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void ConsumeHeat(double energyJoules)
        {
            if (CurrentTemperatureKelvin >= _material.MaxTemperature) return;
            if (energyJoules <= 0d) return;
            _lastHeatReceivedTime = Time.time;


            double temperatureDelta = energyJoules /
                                      (_rigidBody.mass * _material.SpecificHeatCapacity);

            CurrentTemperatureKelvin += temperatureDelta;
            _receivedTemperatureDelta = temperatureDelta;
            if (!_isBurning &&
                CurrentTemperatureKelvin >= _material.IgnitionTemperatureKelvin)
            {
                Burn();
            }
        }

        private void LoseHeat(double energyJoules)
        {
            double temperatureDelta = energyJoules /
                                      (_rigidBody.mass * _material.SpecificHeatCapacity);

            double previousTemperature = CurrentTemperatureKelvin;

            CurrentTemperatureKelvin = Math.Max(
                environmentTemperatureKelvin,
                CurrentTemperatureKelvin - temperatureDelta
            );

            _lostTemperatureDelta =
                CurrentTemperatureKelvin - previousTemperature;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out Flameable flameable))
            {
                _flameables.Add(flameable);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent(out Flameable flameable) && _flameables.Contains(flameable))
            {
                _flameables.Remove(flameable);
            }
        }

        private IEnumerator DestroyCoroutine()
        {
            yield return new WaitForSeconds(_material.SecondToTotalDestroyingWhileBurning * _rigidBody.mass);
            Debug.Log("Destroyed");
            Burned?.Invoke();
        }

        public string GetDebugInfo()
        {
            return
                $"<b>{name}</b>\n" +
                $"<b>State:</b> {(_isBurning ? "Burning" : "Not burning")}\n" +
                $"<b>Heat Received:</b> {_receivedTemperatureDelta:F2} K\n" +
                $"<b>Heat Lost:</b> {_lostTemperatureDelta:F2} K\n" +
                $"<b>Temperature:</b> {CurrentTemperatureKelvin:F2} K\n" +
                $"<b>Max Temp:</b> {_material.MaxTemperature:F2} K\n" +
                $"<b>Material:</b> {_material.name}\n" +
                $"<b>Neighbors:</b> {_flameables.Count}";
        }
    }
}
