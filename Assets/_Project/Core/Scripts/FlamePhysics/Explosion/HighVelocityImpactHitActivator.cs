using System;
using UnityEngine;

namespace Flame
{
    public class HighVelocityImpactHitActivator : MonoBehaviour
    {
        [SerializeField] private Explosion _explosion;
        [SerializeField, Min(0f)] private float _minimumImpactSpeed = 10f;
        private bool _hasExploded;

        private void OnCollisionEnter(Collision collision)
        {
            if (_hasExploded) return;

            var rigidBody = gameObject.GetComponent<Rigidbody>();
            if (rigidBody == null) throw new ArgumentNullException(nameof(gameObject));
            if (_explosion == null) throw new ArgumentNullException(nameof(gameObject));

            if (collision.relativeVelocity.magnitude >= _minimumImpactSpeed)
            {
                _hasExploded = true;
                Instantiate(_explosion, transform.position, transform.rotation);
                Destroy(gameObject);
            }
        }
    }
}
