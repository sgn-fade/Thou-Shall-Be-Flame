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
        if (_camera == null || _rigidbody == null) return;

        _targetPosition = _camera.transform.position + _camera.transform.forward * _holdDistance;

        var moveVector = Vector3.MoveTowards(_rigidbody.position, _targetPosition, Time.fixedDeltaTime  * _dragSpeed);
        _rigidbody.MovePosition(moveVector);
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
        _holdDistance = (_rigidbody.position - _camera.transform.position).magnitude;
    }

    private void StopDragging()
    {
        if (_rigidbody != null)
        {
            _rigidbody.useGravity = true;
            _rigidbody = null;
        }
    }

    private void OnDisable()
    {
        StopDragging();
    }
}
