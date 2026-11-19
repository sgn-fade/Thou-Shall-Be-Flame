using TMPro;
using UnityEngine;

namespace Flame
{
    public class FlameableDebugUI : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private TMP_Text _debugText;
        [SerializeField] private float _updateInterval = 0.5f;

        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;

            if (_timer < _updateInterval)
                return;

            _timer = 0f;

            UpdateDebugText();
        }

        private void UpdateDebugText()
        {
            Ray ray = _camera.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Flameable flameable = hit.collider.GetComponentInParent<Flameable>();

                if (flameable != null)
                {
                    _debugText.text = flameable.GetDebugInfo();
                    return;
                }
            }

            _debugText.text = "No Flameable selected";
        }
    }
}