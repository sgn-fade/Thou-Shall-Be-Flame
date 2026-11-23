using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class RigidbodyDrag : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [SerializeField] private float _dragSpeed = 10f;
    [SerializeField] private float _maxGrabDistance = 100f;



    private float _holdDistance;
    private Vector3 _targetPosition;
    private Rigidbody _rigidbody;
    private Vector3 _collisionNormal;


    private void Awake()
    {
        if (_camera == null)
            _camera = Camera.main;
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TryStartDragging();

        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            StopDragging();
    }

    private void FixedUpdate()
    {
        if (_camera == null || _rigidbody == null)
            return;

        _targetPosition = _camera.transform.position +
                          _camera.transform.forward * _holdDistance;

        Vector3 direction = _targetPosition - _rigidbody.position;

        Vector3 velocity = direction * _dragSpeed;

        if (_collisionNormal != Vector3.zero)
        {
            velocity = Vector3.ProjectOnPlane(velocity, _collisionNormal);
        }

        _rigidbody.linearVelocity = velocity;
        _collisionNormal = Vector3.zero;
    }

    private void TryStartDragging()
    {
        if (_camera == null || Mouse.current == null) return;

        Ray ray = _camera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit, _maxGrabDistance))
            return;

        if (hit.rigidbody == null || hit.rigidbody.isKinematic)
            return;
        _rigidbody = hit.rigidbody;
        _rigidbody.useGravity = false;
        _rigidbody.freezeRotation = true;
        _holdDistance = (_rigidbody.position - _camera.transform.position).magnitude;
    }

    private void StopDragging()
    {
        if (_rigidbody != null)
        {
            _rigidbody.useGravity = true;
        _rigidbody.freezeRotation = false;
            _rigidbody = null;
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            _collisionNormal += contact.normal;
        }

        _collisionNormal.Normalize();
    }

    private void OnDisable()
    {
        StopDragging();
    }
}
