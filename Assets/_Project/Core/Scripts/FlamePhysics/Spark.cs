using System;
using UnityEngine;

namespace Flame
{
    public class Spark : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent(out Flameable flameable))
            {
                flameable.Burn();
            }

            Destroy(gameObject);
        }
    }
}