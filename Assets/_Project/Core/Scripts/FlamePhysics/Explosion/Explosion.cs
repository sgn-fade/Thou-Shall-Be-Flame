using System;
using UnityEngine;

namespace Flame
{
    public class Explosion : MonoBehaviour
    {
        [SerializeField] private float _power;

        public void OnEnable()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, 5f);

            foreach (var collider in colliders)
            {
                if (collider.transform == transform.root)
                    continue;

                if (collider.TryGetComponent(out Rigidbody rigidBody))
                {
                    rigidBody.AddExplosionForce(_power,  transform.position, 5f, 10);
                }

                if (collider.TryGetComponent(out Flameable flameable))
                {
                    flameable.Burn();
                }
            }
        }
    }
}