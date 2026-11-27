using UnityEngine;
using UnityEngine.Events;

namespace Flame
{
    public class ScalingEffect : MaterialReactionEffect
    {
        [Header("Melting Settings")]
        [SerializeField] private Transform _target;
        [SerializeField] private float _meltSpeed = 0.1f;
        [SerializeField] private UnityEvent _meltStarted;
        [SerializeField] private UnityEvent _meltStopped;

        private Vector3 _originalScale;
        private float _meltProgress;
        private float _meltCoefficient;
        private bool _isMelting;
        private Flameable _flameable;

        private void Awake()
        {
            if (_target == null)
                _target = transform;

            _flameable = GetComponent<Flameable>();
            _originalScale = _target.localScale;
        }

        private void Update()
        {
            if (_flameable != null && !_flameable.IsReceivingHeat)
            {
                _meltCoefficient = 0f;
                SetMeltingState(false);
                return;
            }

            if (_meltCoefficient <= 0f || _meltProgress >= 1f)
                return;

            _meltProgress = Mathf.Clamp01(
                _meltProgress + _meltCoefficient * _meltSpeed * Time.deltaTime
            );

            _target.localScale = Vector3.Lerp(
                _originalScale,
                Vector3.zero,
                _meltProgress
            );

            if (_meltProgress >= 1f)
                Destroy(_target.gameObject);
        }

        public override void UpdateEffectStrength(double temperature, float minTemperature)
        {
            _meltCoefficient = Mathf.Max(0f, (float)temperature - 273.15f);
            SetMeltingState(_meltCoefficient > 0f &&
                            (_flameable == null || _flameable.IsReceivingHeat));
        }

        private void SetMeltingState(bool shouldMelt)
        {
            if (shouldMelt == _isMelting)
                return;

            _isMelting = shouldMelt;
            if (_isMelting)
            {
                _meltStarted.Invoke();
            }
            else
            {
                _meltStopped.Invoke();
            }
        }
    }
}
