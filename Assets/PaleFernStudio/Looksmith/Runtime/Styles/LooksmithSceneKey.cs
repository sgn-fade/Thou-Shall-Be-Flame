using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The scene's key for tone-driven styles (comic bands and dots, hatching): the tone a mid surface takes in this
    /// scene's light, so day and night both get their bands and a night isn't all shadow. It comes from the light itself
    /// (the main light, the ambient, the exposure and the tonemapper), never from the frame: a surface keeps its band and
    /// its hatching however the camera moves and whatever else comes into view. (Taken from the frame's average tone, the
    /// whole picture lightened as a dark wall came into view, and changed colour as the player stepped forward.)
    /// </summary>
    public static class LooksmithSceneKey
    {
        public static readonly int Id = Shader.PropertyToID("_LooksmithSceneKeyTone");

        /// <summary>The key last computed (for tools and tests).</summary>
        public static float lastTone { get; private set; } = 0.42f;

        static readonly Vector3[] s_Directions =
        {
            Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };
        static readonly Color[] s_Ambient = new Color[5];

        // How bright a mid surface is, on average, against how bright its light is: a typical scene's surfaces face every
        // way (the sun's height decides how much of its light they catch) and have mid albedos. Tuned so a daylit demo
        // frame's key matches the average tone the frame-based key found there.
        const float k_Albedo = 0.25f;
        const float k_Direct = 0.25f, k_DirectHeight = 0.45f, k_Ambient = 0.6f;

        /// <summary>The key as a shader vector (x: tone 0..1, sRGB lightness).</summary>
        public static Vector4 Value(LooksmithStyleContext context) => new Vector4(Tone(context), 0f, 0f, 0f);

        /// <summary>The tone (0..1, sRGB lightness) a mid surface takes in the light the camera sees this frame.</summary>
        public static float Tone(LooksmithStyleContext context)
        {
            Color mainColor = Color.black;
            Vector3 mainForward = Vector3.down;
            if (context.frameData != null && context.frameData.Contains<UniversalLightData>())
            {
                var lights = context.frameData.Get<UniversalLightData>();
                int main = lights.mainLightIndex;
                if (main >= 0 && main < lights.visibleLights.Length)
                {
                    var light = lights.visibleLights[main];
                    mainColor = light.finalColor;
                    mainForward = light.localToWorldMatrix.GetColumn(2);
                }
            }
            return Tone(mainColor, mainForward);
        }

        /// <summary>
        /// The key for a main light (its final colour: linear colour times intensity, and its forward direction), the
        /// scene's ambient light and the current volume stack's exposure and tonemapper.
        /// </summary>
        public static float Tone(Color mainLightColor, Vector3 mainLightForward)
        {
            float height = Mathf.Clamp01(-mainLightForward.normalized.y);
            float direct = Luma(mainLightColor) * (k_Direct + k_DirectHeight * Mathf.Sqrt(height));
            RenderSettings.ambientProbe.Evaluate(s_Directions, s_Ambient);
            float ambient = 0f;
            foreach (var c in s_Ambient)
                ambient += Mathf.Max(Luma(c), 0f);
            ambient /= s_Ambient.Length;

            float lit = k_Albedo * (direct + k_Ambient * ambient);
            var stack = VolumeManager.instance.stack;
            var grading = stack?.GetComponent<ColorAdjustments>();
            if (grading != null && grading.active)
                lit *= Mathf.Pow(2f, grading.postExposure.value);
            var tonemapping = stack?.GetComponent<Tonemapping>();
            var mode = tonemapping != null && tonemapping.active ? tonemapping.mode.value : TonemappingMode.None;
            lit = mode == TonemappingMode.Neutral ? Neutral(lit) : mode == TonemappingMode.ACES ? Aces(lit) : Mathf.Clamp01(lit);
            lastTone = Mathf.Clamp(Mathf.LinearToGammaSpace(Mathf.Clamp01(lit)), 0.02f, 1f);
            return lastTone;
        }

        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // URP's neutral tonemapper (one channel).
        static float NeutralCurve(float x)
        {
            const float a = 0.2f, b = 0.29f, c = 0.24f, d = 0.272f, e = 0.02f, f = 0.3f;
            return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
        }

        static float Neutral(float x)
        {
            float white = 1f / NeutralCurve(5.3f);
            return Mathf.Clamp01(NeutralCurve(x * white) * white);
        }

        // ACES fitted (Narkowicz), close to URP's ACES on a grey.
        static float Aces(float x)
        {
            x *= 0.6f;
            return Mathf.Clamp01(x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f));
        }
    }
}
