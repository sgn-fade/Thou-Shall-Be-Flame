using System;
using UnityEngine;

namespace _Project.Core.Scripts.FlamePhysics
{
    public class Spark : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            Destroy(gameObject);
            if (other.TryGetComponent(out Flameable flameable))
            {
                Debug.Log("flameable");
                flameable.Burn();
                Destroy(gameObject);
            }
        }
    }
}