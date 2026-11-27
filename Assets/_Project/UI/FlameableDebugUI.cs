using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Flame
{
    public class FlameableDebugUI : MonoBehaviour
    {
        [SerializeField] private Color _aimPointColor = Color.white;
        [SerializeField] private float _aimPointSize = 6f;
        [SerializeField] private Camera _camera;
        [SerializeField] private TMP_Text _debugText;
        [SerializeField] private float _updateInterval = 0.5f;
        [SerializeField] private float _missGracePeriod = 0.15f;

        private float _timer;
        private float _lastTargetHitTime;
        private Flameable _currentTarget;

        private void Awake()
        {
            // Small screen-space point, centered by anchors so it stays put at any resolution.
            var point = new GameObject("Aim Point", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            point.transform.SetParent(transform, false);
            var rect = (RectTransform)point.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(_aimPointSize, _aimPointSize);

            var image = point.GetComponent<Image>();
            image.color = _aimPointColor;
            image.raycastTarget = false;
        }

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
            if (_camera == null || _debugText == null || UnityEngine.InputSystem.Mouse.current == null)
                return;

            Ray ray = _camera.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Flameable flameable = hit.collider.GetComponentInParent<Flameable>();

                if (flameable != null)
                {
                    _currentTarget = flameable;
                    _lastTargetHitTime = Time.unscaledTime;
                }
            }

            if (_currentTarget != null && Time.unscaledTime - _lastTargetHitTime <= _missGracePeriod)
            {
                _debugText.text = _currentTarget.GetDebugInfo();
                return;
            }

            _currentTarget = null;
            _debugText.text = "No Flameable selected";
        }
    }
}
