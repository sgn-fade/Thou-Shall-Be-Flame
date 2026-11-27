using UnityEngine;

namespace Flame
{
    /// <summary>Presentation adapter: translates gameplay events into Animator parameters.</summary>
    [RequireComponent(typeof(Animator))]
    public sealed class FirstPersonHandsAnimationPresenter : MonoBehaviour
    {
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsDraggingHash = Animator.StringToHash("IsDragging");
        private static readonly int ShootStateHash = Animator.StringToHash("Base Layer.shoot");

        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerMovement _movement;
        [SerializeField] private RigidbodyDrag _drag;
        [SerializeField] private FireShooting _shooting;

        private void Awake()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_movement == null) _movement = GetComponentInParent<PlayerMovement>();
            if (_drag == null) _drag = GetComponentInParent<RigidbodyDrag>();
            if (_shooting == null) _shooting = GetComponentInParent<FireShooting>();
        }

        private void OnEnable()
        {
            if (_movement != null) _movement.MovementStateChanged += OnMovementStateChanged;
            if (_drag != null) _drag.DragStateChanged += OnDragStateChanged;
            if (_shooting != null) _shooting.ShotFired += OnShotFired;

            if (_animator == null) return;
            _animator.SetBool(IsMovingHash, _movement != null && _movement.IsMoving);
            _animator.SetBool(IsDraggingHash, _drag != null && _drag.IsDragging);
        }

        private void OnDisable()
        {
            if (_movement != null) _movement.MovementStateChanged -= OnMovementStateChanged;
            if (_drag != null) _drag.DragStateChanged -= OnDragStateChanged;
            if (_shooting != null) _shooting.ShotFired -= OnShotFired;
        }

        private void OnMovementStateChanged(bool isMoving)
        {
            if (_animator != null) _animator.SetBool(IsMovingHash, isMoving);
        }

        private void OnDragStateChanged(bool isDragging)
        {
            if (_animator != null) _animator.SetBool(IsDraggingHash, isDragging);
        }

        private void OnShotFired()
        {
            if (_animator == null) return;

            _animator.Play(ShootStateHash, 0, 0f);
        }
    }
}
