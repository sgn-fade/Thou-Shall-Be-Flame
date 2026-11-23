using UnityEngine;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The scene's wind for everything Looksmith moves: plants with Wind Sway on their material (grass, flowers, reeds,
    /// trees) lean with it and ripple as gusts roll across the ground; a Wind Zone, if you give it one, carries
    /// particles (falling leaves, smoke) the same way. Without a Looksmith Wind, swaying materials use a gentle breeze.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Looksmith/Looksmith Wind")]
    public sealed class LooksmithWind : MonoBehaviour
    {
        [Tooltip("Where the wind blows toward (degrees; 0 = +z, 90 = +x).")]
        [Range(0f, 360f)] public float direction = 60f;
        [Tooltip("How hard it blows: multiplies every material's sway (0 = still air, 1 = a breeze, 3 = a gale).")]
        [Range(0f, 3f)] public float strength = 1f;
        [Tooltip("How quickly plants sway back and forth.")]
        [Range(0.1f, 4f)] public float speed = 1f;
        [Tooltip("How much the wind comes in gusts rather than a steady push.")]
        [Range(0f, 1f)] public float gustiness = 0.55f;
        [Tooltip("Size of the gust fronts rolling over the ground (m).")]
        [Min(1f)] public float gustSize = 22f;
        [Tooltip("How fast the gust fronts travel (m/s).")]
        [Min(0f)] public float gustSpeed = 5f;
        [Tooltip("Optional: a Wind Zone kept in step (particles with External Forces, Unity trees).")]
        public WindZone windZone;

        public static LooksmithWind active { get; private set; }

        static readonly int k_Wind = Shader.PropertyToID("_LooksmithWind");
        static readonly int k_Wind2 = Shader.PropertyToID("_LooksmithWind2");
        static readonly int k_WindClock = Shader.PropertyToID("_LooksmithWindClock");

        /// <summary>
        /// Holds the wind's clock (seconds; negative releases it), so captures and tests see a chosen moment of the sway.
        /// </summary>
        public static void SetTimeOverride(float seconds) =>
            Shader.SetGlobalVector(k_WindClock, seconds >= 0f ? new Vector4(seconds, 1f, 0f, 0f) : Vector4.zero);

        /// <summary>The wind's direction on the ground (unit xz).</summary>
        public Vector2 groundDirection
        {
            get
            {
                float a = direction * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            }
        }

        void OnEnable()
        {
            active = this;
            Push();
        }

        void OnDisable()
        {
            if (active == this)
                active = null;
            Shader.SetGlobalVector(k_Wind, Vector4.zero);
            Shader.SetGlobalVector(k_Wind2, Vector4.zero);
        }

        void OnValidate() => Push();

        void Update() => Push();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active = null;

        public void Push()
        {
            if (!isActiveAndEnabled)
                return;
            var d = groundDirection;
            // a strength of exactly zero would read as "unset" in the shaders: keep still air just above it
            Shader.SetGlobalVector(k_Wind, new Vector4(d.x, d.y, speed, Mathf.Max(gustiness, 1e-4f)));
            Shader.SetGlobalVector(k_Wind2, new Vector4(Mathf.Max(strength, 1e-4f), 1f / gustSize, gustSpeed, 0f));
            if (windZone != null)
            {
                windZone.mode = WindZoneMode.Directional;
                windZone.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up);
                windZone.windMain = strength * 0.6f;
                windZone.windTurbulence = gustiness * strength * 0.8f;
                windZone.windPulseMagnitude = gustiness;
                windZone.windPulseFrequency = 0.08f * speed;
            }
        }
    }
}
