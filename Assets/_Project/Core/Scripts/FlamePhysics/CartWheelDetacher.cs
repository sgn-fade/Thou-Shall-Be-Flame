using UnityEngine;

namespace Flame
{
    /// <summary>
    /// Releases the cart wheels when its body finishes burning.
    /// </summary>
    public sealed class CartWheelDetacher : MonoBehaviour
    {
        [SerializeField] private Flameable _flameable;
        [SerializeField] private Rigidbody _body;
        [SerializeField] private Joint[] _wheelJoints;
        [SerializeField, Min(0f)] private float _detachImpulse = 2f;
        [SerializeField, Min(0f)] private float _upwardImpulse = 0.75f;
        [SerializeField] private GDurell.StylizedDissolve.DissolveController _dissolveController;

        private bool _detached;

        private void Awake()
        {
            if (_flameable == null)
                _flameable = GetComponent<Flameable>();
            if (_body == null)
                _body = GetComponent<Rigidbody>();
            if (_dissolveController == null)
                _dissolveController = GetComponent<GDurell.StylizedDissolve.DissolveController>();
        }

        private void OnEnable()
        {
            if (_flameable != null)
                _flameable.Burned += DetachWheels;
        }

        private void OnDisable()
        {
            if (_flameable != null)
                _flameable.Burned -= DetachWheels;
        }

        private void DetachWheels()
        {
            if (_detached)
                return;

            _detached = true;
            foreach (var joint in _wheelJoints)
            {
                if (joint == null)
                    continue;

                var wheelBody = joint.GetComponent<Rigidbody>();

                if (wheelBody == null)
                    continue;

                wheelBody.linearVelocity = _body != null ? _body.linearVelocity : Vector3.zero;
                var direction = wheelBody.worldCenterOfMass - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.001f)
                    direction = transform.forward;

                wheelBody.AddForce(direction.normalized * _detachImpulse + Vector3.up * _upwardImpulse,
                    ForceMode.Impulse);
                wheelBody.AddTorque(Random.insideUnitSphere * _detachImpulse, ForceMode.Impulse);
            }

            if (_dissolveController != null)
                _dissolveController.TriggerDissolve();
        }
    }
}
