using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    private float verticalVelocity;

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

        Vector3 direction = transform.right * input.x +
                             transform.forward * input.y;

        direction = Vector3.ClampMagnitude(direction, 1f);

        if (controller.isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * speed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
}
