using System;
using Unity.VisualScripting.Dependencies.NCalc;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Flame
{
public class FireShooting : MonoBehaviour
    {
        [SerializeField] private GameObject _sparkProjectile;
        [SerializeField] private float _shootImpulse = 10f;
        [SerializeField] private Camera _camera;
        public event Action ShotFired;

        public void Shoot(InputAction.CallbackContext context)
        {
            if (!context.started) return;

            var sparkObj = Instantiate(_sparkProjectile,
                _camera.transform.position + _camera.transform.forward,
                transform.rotation);

            ShotFired?.Invoke();

            if (sparkObj.TryGetComponent(out Rigidbody rb))
            {
                rb.AddForce(_camera.transform.forward * _shootImpulse, ForceMode.Impulse);
            }
            else
            {
                Debug.LogError("Spark projectile doesn't have a rigidbody! " +  sparkObj);
            }
        }
    }
}
