using System;
using UnityEngine;

namespace Flame
{
    public class HighVelocityImpactHitActivator : MonoBehaviour
    {
        [SerializeField] private Explosion _explosion;
        private bool _hasExploded;

        private void OnCollisionEnter(Collision other)
        {
            if (_hasExploded) return;

            var rigidBody = gameObject.GetComponent<Rigidbody>();

            if (rigidBody == null) throw new ArgumentNullException(nameof(gameObject));
            if (_explosion == null) throw new ArgumentNullException(nameof(gameObject));

            if(rigidBody.linearVelocity.magnitude > 10)
            {
                _hasExploded = true;
                Instantiate(_explosion, transform.position, transform.rotation);
                Destroy(gameObject);
            }
        }
    }
}