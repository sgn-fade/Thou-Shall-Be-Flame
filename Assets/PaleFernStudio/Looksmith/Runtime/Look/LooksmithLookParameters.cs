using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// A palette colour in OKLCH (x = lightness 0..1, y = chroma, z = hue in degrees). Volumes blend it in OKLCH,
    /// with hue taking the shortest way round, so two looks cross-fade without passing through grey or mud.
    /// </summary>
    [Serializable]
    public sealed class OklchParameter : VolumeParameter<Vector3>
    {
        public OklchParameter(Vector3 value, bool overrideState = false) : base(value, overrideState) { }

        public static OklchParameter FromHex(string hex, bool overrideState = false)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return new OklchParameter(LooksmithColor.ColorToOklch(c), overrideState);
        }

        public override void Interp(Vector3 from, Vector3 to, float t)
        {
            // Hue is meaningless at zero chroma: take the other side's hue.
            float hFrom = from.y < 0.002f ? to.z : from.z;
            float hTo = to.y < 0.002f ? from.z : to.z;
            float h = hFrom + LooksmithColor.HueDelta(hFrom, hTo) * t;
            m_Value = new Vector3(Mathf.Lerp(from.x, to.x, t), Mathf.Lerp(from.y, to.y, t), Mathf.Repeat(h, 360f));
        }
    }

    public enum LooksmithSpecularStyle
    {
        Stepped = 0,
        Blinn = 1,
        AnisotropicHair = 2,
    }

    [Serializable]
    public sealed class SpecularStyleParameter : VolumeParameter<LooksmithSpecularStyle>
    {
        public SpecularStyleParameter(LooksmithSpecularStyle value, bool overrideState = false) : base(value, overrideState) { }

        // Blend as a number so Blinn ↔ Stepped cross-fades (the shader treats the style as continuous).
        public override void Interp(LooksmithSpecularStyle from, LooksmithSpecularStyle to, float t) =>
            m_Value = t < 0.5f ? from : to;
    }
}
