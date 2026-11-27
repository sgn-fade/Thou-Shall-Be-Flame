using UnityEngine;
using UnityEngine.InputSystem;
using System;
namespace Flame
{
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float gravity = -20f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.5f;

    private CharacterController controller;
    private float verticalVelocity;
    public bool IsMoving { get; private set; }
    public event Action<bool> MovementStateChanged;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current != null)
        {
            input.x = Keyboard.current.dKey.ReadValue() -
                      Keyboard.current.aKey.ReadValue();

            input.y = Keyboard.current.wKey.ReadValue() -
                      Keyboard.current.sKey.ReadValue();
        }

        var isMoving = input.sqrMagnitude > 0.01f;
        if (IsMoving != isMoving)
        {
            IsMoving = isMoving;
            MovementStateChanged?.Invoke(IsMoving);
        }

        Vector3 direction = transform.right * input.x +
                             transform.forward * input.y;

        direction = Vector3.ClampMagnitude(direction, 1f);

        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;

        if (isGrounded && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * speed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
}
}
