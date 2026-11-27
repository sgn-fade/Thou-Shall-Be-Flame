using UnityEngine;

namespace Flame
{
    /// <summary>
    /// Lets a rigidbody part fall when its burn cycle completes, then removes it.
    /// </summary>
    public sealed class FlameablePartLifetime : MonoBehaviour
    {
        [SerializeField] private Flameable _flameable;
        [SerializeField] private Rigidbody _body;
        [SerializeField, Min(0f)] private float _secondsAfterBurned = 3f;

        private void Awake()
        {
            if (_flameable == null)
                _flameable = GetComponent<Flameable>();
            if (_body == null)
                _body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (_flameable != null)
                _flameable.Burned += HandleBurned;
        }

        private void OnDisable()
        {
            if (_flameable != null)
                _flameable.Burned -= HandleBurned;
        }

        private void HandleBurned()
        {
            if (_body != null)
                _body.isKinematic = false;

            Destroy(gameObject, _secondsAfterBurned);
        }
    }
}
