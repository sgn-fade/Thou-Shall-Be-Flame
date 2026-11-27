using System;
using UnityEngine;

namespace Flame
{
    public class Explosion : MonoBehaviour
    {
        [SerializeField] private float _power;
        [SerializeField] private float _radius;
        [SerializeField] private float _timeToSelfDestroy = 5f;
        public void OnEnable()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, _radius);

            foreach (var collider in colliders)
            {
                if (collider.transform == transform.root)
                    continue;

                if (collider.TryGetComponent(out Rigidbody rigidBody) && _power > 0)
                {
                    rigidBody.AddExplosionForce(_power,  transform.position, _radius, 10);
                }

                if (collider.TryGetComponent(out Flameable flameable))
                {
                    flameable.Burn();
                }
            }
            Invoke(nameof(SelfDestroy), _timeToSelfDestroy);
        }

        public void SelfDestroy()
        {
            Destroy(gameObject);
        }
    }
}