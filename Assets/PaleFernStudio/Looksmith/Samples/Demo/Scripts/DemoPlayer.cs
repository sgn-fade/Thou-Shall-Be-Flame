using UnityEngine;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>
    /// A first-person walker for the demo: WASD, Shift to run, Space to jump, mouse to look (click to capture the
    /// mouse, Esc to release it), F to fly freely. Uses a CharacterController so it walks on the terrain and bumps into
    /// houses and rocks.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Looksmith/Demo/Demo Player")]
    public sealed class DemoPlayer : MonoBehaviour
    {
        public Camera view;
        [Min(0f)] public float walkSpeed = 3.4f;
        [Min(0f)] public float runSpeed = 6.5f;
        [Min(0f)] public float flySpeed = 12f;
        [Min(0f)] public float jumpSpeed = 4.6f;
        [Min(0f)] public float gravity = 14f;
        [Range(0.02f, 1f)] public float mouseSensitivity = 0.16f;
        [Tooltip("Gentle head bob while walking (metres).")]
        [Range(0f, 0.1f)] public float headBob = 0.025f;
        public float eyeHeight = 1.62f;
        [Tooltip("The water's surface height (none: negative infinity). Looksmith water is a surface, so the player wades in no deeper than Wade Depth and never takes the eye under it.")]
        public float waterLevel = float.NegativeInfinity;
        [Tooltip("The deepest water the player walks into (metres).")]
        [Min(0f)] public float wadeDepth = 1.0f;

        CharacterController m_Body;
        float m_Yaw, m_Pitch, m_VerticalSpeed, m_BobPhase;
        Vector3 m_Velocity;
        public bool flying { get; private set; }

        public static DemoPlayer current { get; private set; }

        void OnEnable()
        {
            current = this;
            m_Body = GetComponent<CharacterController>();
            if (view == null)
                view = GetComponentInChildren<Camera>();
            m_Yaw = transform.eulerAngles.y;
            m_Pitch = view != null ? NormalizePitch(view.transform.localEulerAngles.x) : 0f;
        }

        void OnDisable()
        {
            if (current == this)
                current = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static float NormalizePitch(float x) => x > 180f ? x - 360f : x;

        public bool mouseCaptured => Cursor.lockState == CursorLockMode.Locked;

        public void CaptureMouse(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        /// <summary>Moves the player to a point on the ground (spawn points, review viewpoints).</summary>
        public void Teleport(Vector3 feet, float yaw, float pitch = 0f)
        {
            m_Body.enabled = false;
            transform.position = feet;
            m_Body.enabled = true;
            m_Yaw = yaw;
            m_Pitch = pitch;
            m_Velocity = Vector3.zero;
            m_VerticalSpeed = 0f;
            Apply();
        }

        void Update()
        {
            // while Tweaks is open over the game, the mouse is the panel's
            bool tweaks = LooksmithTweaksHooks.overlayOpen;
            if (DemoInput.Click && !mouseCaptured && !tweaks)
                CaptureMouse(true);
            if (DemoInput.Pressed(DemoKey.Release))
                CaptureMouse(false);
            if (DemoInput.Pressed(DemoKey.Fly))
                SetFlying(!flying);
            var look = mouseCaptured && !tweaks ? DemoInput.MouseDelta * mouseSensitivity * 10f : Vector2.zero;
            var move = new Vector2((DemoInput.Held(DemoKey.Right) ? 1 : 0) - (DemoInput.Held(DemoKey.Left) ? 1 : 0),
                                   (DemoInput.Held(DemoKey.Forward) ? 1 : 0) - (DemoInput.Held(DemoKey.Back) ? 1 : 0));
            float vertical = (DemoInput.Held(DemoKey.Up) ? 1 : 0) - (DemoInput.Held(DemoKey.Down) ? 1 : 0);
            Step(move, DemoInput.Held(DemoKey.Run), DemoInput.Pressed(DemoKey.Jump), look, vertical, Time.deltaTime);
        }

        // How deep the water is over the ground under a point (0 on dry land, or with no water).
        float WaterDepthUnder(Vector3 feet)
        {
            if (float.IsNegativeInfinity(waterLevel))
                return 0f;
            float ground = Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : feet.y;
            return Mathf.Max(waterLevel - ground, 0f);
        }

        public void SetFlying(bool on)
        {
            flying = on;
            m_VerticalSpeed = 0f;
        }

        /// <summary>
        /// One step of movement: move (x right, y forward), run, a jump press, a look delta (degrees: x yaw, y pitch),
        /// up/down while flying. Update feeds it from the keyboard and mouse; tests drive it directly.
        /// </summary>
        public void Step(Vector2 move, bool run, bool jump, Vector2 look, float vertical, float dt)
        {
            if (m_Body == null)
                OnEnable();
            m_Yaw += look.x;
            m_Pitch = Mathf.Clamp(m_Pitch - look.y, -85f, 85f);
            var input = Vector2.ClampMagnitude(move, 1f);
            var yawRot = Quaternion.Euler(0f, m_Yaw, 0f);

            if (flying)
            {
                var lookRot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
                var wish = lookRot * new Vector3(input.x, 0f, input.y) + Vector3.up * vertical;
                float speed = flySpeed * (run ? 2.5f : 1f);
                m_Velocity = Vector3.Lerp(m_Velocity, wish * speed, 1f - Mathf.Exp(-8f * dt));
                m_Body.Move(m_Velocity * dt);
                float under = waterLevel + 0.2f - (transform.position.y + eyeHeight);
                if (under > 0f)
                {
                    m_Body.Move(Vector3.up * under);        // the eye stays above the water (it's a surface: none below)
                    m_Velocity.y = Mathf.Max(m_Velocity.y, 0f);
                }
            }
            else
            {
                float speed = run ? runSpeed : walkSpeed;
                var wish = yawRot * new Vector3(input.x, 0f, input.y) * speed;
                // quick to start and stop, a touch of momentum
                var flat = Vector3.Lerp(new Vector3(m_Velocity.x, 0f, m_Velocity.z), wish, 1f - Mathf.Exp((m_Body.isGrounded ? -12f : -2f) * dt));
                if (m_Body.isGrounded)
                {
                    m_VerticalSpeed = -2f;
                    if (jump)
                        m_VerticalSpeed = jumpSpeed;
                }
                else
                {
                    m_VerticalSpeed -= gravity * dt;
                }
                m_Velocity = new Vector3(flat.x, m_VerticalSpeed, flat.z);
                var before = transform.position;
                float depthBefore = WaterDepthUnder(before);
                var flags = m_Body.Move(m_Velocity * dt);
                if (!float.IsNegativeInfinity(waterLevel))
                {
                    // wading, no deeper: a step toward deeper water is undone as at a wall (Looksmith water is a surface,
                    // with nothing to see from under it)
                    float depth = WaterDepthUnder(transform.position);
                    if (depth > wadeDepth && depth > depthBefore + 1e-3f)
                    {
                        var back = before - transform.position;
                        back.y = 0f;
                        m_Body.Move(back);
                        flat = Vector3.zero;
                    }
                    // fallen in (off the dock): afloat at wading depth, free to make for the shore
                    float floor = waterLevel - wadeDepth;
                    if (transform.position.y < floor)
                    {
                        m_Body.Move(Vector3.up * (floor - transform.position.y));
                        m_VerticalSpeed = Mathf.Max(m_VerticalSpeed, 0f);
                    }
                    m_Velocity = new Vector3(flat.x, m_VerticalSpeed, flat.z);
                }
                if ((flags & CollisionFlags.Above) != 0 && m_VerticalSpeed > 0f)
                    m_VerticalSpeed = 0f;
                float moving = new Vector2(flat.x, flat.z).magnitude;
                if (m_Body.isGrounded && moving > 0.3f)
                    m_BobPhase += dt * moving * 2.1f;
                else
                    m_BobPhase = Mathf.MoveTowards(m_BobPhase, Mathf.Round(m_BobPhase / Mathf.PI) * Mathf.PI, dt * 3f);
            }
            Apply();
        }

        static readonly int k_Pushers = Shader.PropertyToID("_LooksmithGrassPushers");
        static readonly Vector4[] s_Pushers = new Vector4[4];

        void Apply()
        {
            // grass bends away from the player's feet (Looksmith Grass)
            var feet = transform.position;
            s_Pushers[0] = new Vector4(feet.x, feet.y, feet.z, flying ? 0f : 0.9f);
            Shader.SetGlobalVectorArray(k_Pushers, s_Pushers);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (view == null)
                return;
            float bob = flying ? 0f : Mathf.Sin(m_BobPhase * 2f) * headBob;
            view.transform.localPosition = new Vector3(0f, eyeHeight + bob, 0f);
            view.transform.localRotation = Quaternion.Euler(m_Pitch, 0f, Mathf.Sin(m_BobPhase) * headBob * 8f);
        }
    }
}
