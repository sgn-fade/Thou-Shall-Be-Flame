using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// Converts one material in memory: reads every source value first, swaps the shader, then writes the mapped
    /// values and refreshes keywords and render state. The material keeps its GUID, so references and prefabs stay.
    /// </summary>
    public static class LooksmithMaterialConverter
    {
        // Names low-poly packs and older shaders use for the usual inputs, most common first.
        internal static readonly string[] BaseMapNames = { "_BaseMap", "_MainTex", "_Albedo", "_AlbedoMap", "_AlbedoTexture", "_Diffuse", "_DiffuseMap", "_MainTexture", "_Texture" };
        internal static readonly string[] BaseColorNames = { "_BaseColor", "_Color", "_Tint", "_MainColor", "_AlbedoColor", "_Albedo" };
        static readonly string[] k_NormalNames = { "_BumpMap", "_NormalMap", "_Normal" };
        static readonly string[] k_NormalScaleNames = { "_BumpScale", "_NormalStrength", "_NormalScale" };
        static readonly string[] k_EmissionColorNames = { "_EmissionColor", "_Emission", "_EmissiveColor" };
        static readonly string[] k_EmissionMapNames = { "_EmissionMap", "_EmissiveMap" };

        /// <summary>Every value the source material holds, captured before its shader changes.</summary>
        sealed class Source
        {
            public string shaderName;
            public readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
            public readonly Dictionary<string, Vector4> textureST = new Dictionary<string, Vector4>();
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
            public readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>();
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            public HashSet<string> keywords;
            public int renderQueue;

            public static Source Read(Material m)
            {
                var s = new Source { shaderName = m.shader != null ? m.shader.name : "", keywords = new HashSet<string>(m.shaderKeywords), renderQueue = m.renderQueue };
                var shader = m.shader;
                if (shader == null)
                    return s;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var name = shader.GetPropertyName(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Texture:
                            s.textures[name] = m.GetTexture(name);
                            s.textureST[name] = new Vector4(m.GetTextureScale(name).x, m.GetTextureScale(name).y, m.GetTextureOffset(name).x, m.GetTextureOffset(name).y);
                            break;
                        case ShaderPropertyType.Color: s.colors[name] = m.GetColor(name); break;
                        case ShaderPropertyType.Vector: s.vectors[name] = m.GetVector(name); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: s.floats[name] = m.GetFloat(name); break;
                        case ShaderPropertyType.Int: s.floats[name] = m.GetInteger(name); break;
                    }
                }
                return s;
            }

            public string FirstTexture(IEnumerable<string> names) => names.FirstOrDefault(n => textures.ContainsKey(n));
            public string FirstColor(IEnumerable<string> names) => names.FirstOrDefault(n => colors.ContainsKey(n));
            public float Float(string name, float fallback) => floats.TryGetValue(name, out var v) ? v : fallback;
            public bool Has(string name) => floats.ContainsKey(name) || colors.ContainsKey(name) || textures.ContainsKey(name) || vectors.ContainsKey(name);
            public bool Keyword(string k) => keywords.Contains(k);
        }

        /// <summary>Converts <paramref name="material"/> in place. Returns false (and leaves it untouched) when the target is missing.</summary>
        public static bool Convert(Material material, RestyleSpec spec)
        {
            var target = spec.ResolveTarget();
            if (target == null || material == null)
                return false;
            var src = Source.Read(material);
            material.shader = target;
            switch (spec.recipe)
            {
                case RestyleRecipe.Lit: ToLooksmithLit(material, src, spec); break;
                case RestyleRecipe.Unlit: ToLooksmithUnlit(material, src); break;
                case RestyleRecipe.Particles: ToLooksmithParticles(material, src); break;
                case RestyleRecipe.Terrain: ToLooksmithTerrain(material, src); break;
                case RestyleRecipe.ToUrpLit: ToUrpLit(material, src); break;
                case RestyleRecipe.ToUrpUnlit: ToUrpUnlit(material, src); break;
                case RestyleRecipe.ToUrpParticles: ToUrpParticles(material, src); break;
                case RestyleRecipe.ToUrpTerrain: ToUrpTerrain(material, src); break;
            }
            foreach (var pair in spec.pairs)
                CopyPair(material, src, pair);
            if (spec.twoSided && material.HasProperty("_Cull"))
                Set(material, "_Cull", 0f);
            Validate(material, spec.recipe);
            return true;
        }

        // ------------------------------------------------------------------ to Looksmith

        static void ToLooksmithLit(Material m, Source s, RestyleSpec spec)
        {
            BaseInputs(m, s);
            Set(m, "_VertexColorAmount", spec.vertexColorAmount);

            var normal = s.FirstTexture(k_NormalNames);
            if (normal != null)
            {
                SetTexture(m, "_BumpMap", s, normal);
                var scale = k_NormalScaleNames.FirstOrDefault(n => s.floats.ContainsKey(n));
                Set(m, "_BumpScale", scale != null ? s.floats[scale] : 1f);
            }
            if (s.textures.ContainsKey("_OcclusionMap"))
            {
                SetTexture(m, "_OcclusionMap", s, "_OcclusionMap");
                Set(m, "_OcclusionStrength", s.Float("_OcclusionStrength", 1f));
            }
            Emission(m, s);

            // Smoothness and metallic by family
            float smoothness = 0.3f, metallic = 0f;
            if (s.shaderName == "Autodesk Interactive")
            {
                smoothness = 1f - s.Float("_Glossiness", 0.5f);   // Autodesk's slider is roughness
                metallic = s.Float("_Metallic", 0f);
            }
            // URP first: URP Lit also carries hidden legacy _Glossiness/_Color/_MainTex for old projects, with stale values.
            else if (s.Has("_Smoothness"))
            {
                smoothness = s.Float("_Smoothness", 0.5f);
                bool specularWorkflow = s.Has("_WorkflowMode") && s.Float("_WorkflowMode", 1f) < 0.5f;
                metallic = specularWorkflow ? 0f : s.Float("_Metallic", 0f);
            }
            else if (s.Has("_Glossiness"))
            {
                smoothness = s.Float("_Glossiness", 0.5f);       // Built-in Standard (both setups)
                metallic = s.shaderName.Contains("Specular") ? 0f : s.Float("_Metallic", 0f);
            }
            else if (s.Has("_Shininess"))
                smoothness = Mathf.Clamp01(s.Float("_Shininess", 0.08f));   // Legacy Specular
            Set(m, "_Smoothness", smoothness);
            Set(m, "_Metallic", metallic);
            // URP's "Specular Highlights" toggle off (Lit / Simple Lit) keeps highlights off in Looksmith too.
            Set(m, "_SpecularAmount", s.Has("_SpecularHighlights") && s.Float("_SpecularHighlights", 1f) < 0.5f ? 0f : 1f);

            SurfaceOptions(m, s);
        }

        static void ToLooksmithUnlit(Material m, Source s)
        {
            BaseInputs(m, s);
            Set(m, "_GradientAmount", 0f);
            var emission = s.FirstColor(k_EmissionColorNames);
            Set(m, "_EmissionColor", emission != null && (s.Keyword("_EMISSION") || !s.Has("_EmissionColor")) ? s.colors[emission] : Color.black);
            SurfaceOptions(m, s);
        }

        static void ToLooksmithParticles(Material m, Source s)
        {
            BaseInputs(m, s);
            bool lit = s.shaderName.Contains("/Lit") || s.shaderName.Contains("Simple Lit");
            Set(m, "_Lit", lit ? 1f : 0f);
            bool soft = s.Float("_SoftParticlesEnabled", 0f) > 0.5f;
            float near = s.Float("_SoftParticlesNearFadeDistance", 0f), far = s.Float("_SoftParticlesFarFadeDistance", 1f);
            Set(m, "_SoftDistance", soft ? Mathf.Max(0.05f, far - near) : 0f);
            Set(m, "_EmissionColor", s.Keyword("_EMISSION") && s.colors.TryGetValue("_EmissionColor", out var e) ? e : Color.black);
            SurfaceOptions(m, s, defaultSurface: 1f, defaultCull: 0f);
        }

        static void ToLooksmithTerrain(Material m, Source s)
        {
            bool heightBlend = s.Keyword("_TERRAIN_BLEND_HEIGHT") || s.Float("_EnableHeightBlend", 0f) > 0.5f;
            Set(m, "_HeightBlend", heightBlend ? Mathf.Clamp01(1f - s.Float("_HeightTransition", 0f)) : 0f);
        }

        // ------------------------------------------------------------------ back to URP (inverse rules)

        static void ToUrpLit(Material m, Source s)
        {
            Set(m, "_WorkflowMode", 1f);
            BaseInputs(m, s);
            if (s.textures.TryGetValue("_BumpMap", out var bump) && bump != null)
            {
                SetTexture(m, "_BumpMap", s, "_BumpMap");
                Set(m, "_BumpScale", s.Float("_BumpScale", 1f));
            }
            if (s.textures.TryGetValue("_OcclusionMap", out var occ) && occ != null)
            {
                SetTexture(m, "_OcclusionMap", s, "_OcclusionMap");
                Set(m, "_OcclusionStrength", s.Float("_OcclusionStrength", 1f));
            }
            Set(m, "_Smoothness", s.Float("_Smoothness", 0.5f));
            Set(m, "_Metallic", s.Float("_Metallic", 0f));
            var emission = s.colors.TryGetValue("_EmissionColor", out var e) ? e : Color.black;
            Set(m, "_EmissionColor", emission);
            if (s.textures.ContainsKey("_EmissionMap"))
                SetTexture(m, "_EmissionMap", s, "_EmissionMap");
            if (emission.maxColorComponent > 0f)
                m.EnableKeyword("_EMISSION");
            else
                m.DisableKeyword("_EMISSION");
            SurfaceOptions(m, s);
        }

        static void ToUrpUnlit(Material m, Source s)
        {
            BaseInputs(m, s);
            SurfaceOptions(m, s);
        }

        static void ToUrpParticles(Material m, Source s)
        {
            BaseInputs(m, s);
            var e = s.colors.TryGetValue("_EmissionColor", out var c) ? c : Color.black;
            Set(m, "_EmissionColor", e);
            if (e.maxColorComponent > 0f) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            float soft = s.Float("_SoftDistance", 0f);
            Set(m, "_SoftParticlesEnabled", soft > 0f ? 1f : 0f);
            Set(m, "_SoftParticlesNearFadeDistance", 0f);
            Set(m, "_SoftParticlesFarFadeDistance", soft > 0f ? soft : 1f);
            SurfaceOptions(m, s, defaultSurface: 1f, defaultCull: 0f);
        }

        static void ToUrpTerrain(Material m, Source s)
        {
            float blend = s.Float("_HeightBlend", 0f);
            Set(m, "_EnableHeightBlend", blend > 0.001f ? 1f : 0f);
            Set(m, "_HeightTransition", Mathf.Clamp01(1f - blend));
        }

        // ------------------------------------------------------------------ shared pieces

        static void BaseInputs(Material m, Source s)
        {
            var map = s.FirstTexture(BaseMapNames);
            if (map != null)
                SetTexture(m, "_BaseMap", s, map);
            var color = s.FirstColor(BaseColorNames);
            Set(m, "_BaseColor", color != null && !ClearBlack(s.colors[color]) ? Visible(s.colors[color]) : Color.white);
            if (s.floats.ContainsKey("_Cutoff"))
                Set(m, "_Cutoff", s.floats["_Cutoff"]);
        }

        static void Emission(Material m, Source s)
        {
            var color = s.FirstColor(k_EmissionColorNames);
            // URP and Built-in only emit with the _EMISSION keyword; shaders without the toggle emit whatever is set.
            bool on = color != null && (s.Keyword("_EMISSION") || !s.Has("_EmissionColor"));
            Set(m, "_EmissionColor", on ? s.colors[color] : Color.black);
            var map = s.FirstTexture(k_EmissionMapNames);
            if (on && map != null)
                SetTexture(m, "_EmissionMap", s, map);
        }

        /// <summary>Opaque/transparent, alpha clip, blend mode and culling, from URP's options or Built-in's _Mode.</summary>
        static void SurfaceOptions(Material m, Source s, float defaultSurface = 0f, float defaultCull = 2f)
        {
            float surface = defaultSurface, blend = 0f, clip = 0f, cull = defaultCull;
            if (s.Has("_Surface"))
            {
                surface = s.Float("_Surface", defaultSurface);
                blend = s.Float("_Blend", 0f);
                clip = s.Float("_AlphaClip", 0f);
                cull = s.Float("_Cull", defaultCull);
            }
            else if (s.Has("_Mode"))
            {
                switch (Mathf.RoundToInt(s.Float("_Mode", 0f)))
                {
                    case 1: clip = 1f; break;                       // Cutout
                    case 2: surface = 1f; blend = 0f; break;        // Fade → Alpha
                    case 3: surface = 1f; blend = 1f; break;        // Transparent → Premultiply
                }
            }
            else if (s.Keyword("_ALPHATEST_ON") || s.shaderName.Contains("Cutout"))
                clip = 1f;                                          // legacy "Transparent/Cutout/…", "Unlit/Transparent Cutout"
            else if (s.shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 || s.renderQueue >= 2750)
                surface = 1f;                                       // legacy "Transparent/…", "Unlit/Transparent", a pack's transparent master
            Set(m, "_Surface", surface);
            Set(m, "_Blend", blend);
            Set(m, "_AlphaClip", clip);
            Set(m, "_Cull", cull);
            if (s.floats.TryGetValue("_QueueOffset", out var q))
                Set(m, "_QueueOffset", q);
        }

        static void CopyPair(Material m, Source s, PropertyPair pair)
        {
            if (string.IsNullOrEmpty(pair.source) || string.IsNullOrEmpty(pair.target) || !m.HasProperty(pair.target))
                return;
            if (s.textures.ContainsKey(pair.source))
                SetTexture(m, pair.target, s, pair.source);
            else if (s.colors.TryGetValue(pair.source, out var c))
            {
                if (!(pair.target == "_BaseColor" && ClearBlack(c)))
                    m.SetColor(pair.target, pair.target == "_BaseColor" ? Visible(c) : c);
            }
            else if (s.vectors.TryGetValue(pair.source, out var v))
                m.SetVector(pair.target, v);
            else if (s.floats.TryGetValue(pair.source, out var f))
                m.SetFloat(pair.target, f);
        }

        static void SetTexture(Material m, string target, Source s, string source)
        {
            if (!m.HasProperty(target))
                return;
            m.SetTexture(target, s.textures[source]);
            var st = s.textureST[source];
            m.SetTextureScale(target, new Vector2(st.x, st.y));
            m.SetTextureOffset(target, new Vector2(st.z, st.w));
        }

        /// <summary>Transparent black: a mask or add colour, never a base colour to multiply by (it would blacken the surface).</summary>
        static bool ClearBlack(Color c) => c.maxColorComponent < 0.004f && c.a < 0.004f;

        /// <summary>
        /// A tint with zero alpha means its source ignored alpha (Leartes' transparent master), not "invisible": Looksmith Lit
        /// multiplies by it, so it's kept at 1.
        /// </summary>
        static Color Visible(Color c) => c.a < 0.004f ? new Color(c.r, c.g, c.b, 1f) : c;

        static void Set(Material m, string name, float v)
        {
            if (m.HasProperty(name))
                m.SetFloat(name, v);
        }

        static void Set(Material m, string name, Color c)
        {
            if (m.HasProperty(name))
                m.SetColor(name, c);
        }

        // ------------------------------------------------------------------ keywords and render state

        static readonly Dictionary<RestyleRecipe, string> k_UrpGuis = new Dictionary<RestyleRecipe, string>
        {
            { RestyleRecipe.ToUrpLit, "UnityEditor.Rendering.Universal.ShaderGUI.LitShader" },
            { RestyleRecipe.ToUrpUnlit, "UnityEditor.Rendering.Universal.ShaderGUI.UnlitShader" },
            { RestyleRecipe.ToUrpParticles, "UnityEditor.Rendering.Universal.ShaderGUI.ParticlesUnlitShader" },
        };

        static void Validate(Material m, RestyleRecipe recipe)
        {
            switch (recipe)
            {
                case RestyleRecipe.Lit:
                case RestyleRecipe.Unlit:
                case RestyleRecipe.Particles:
                    LooksmithLitGUI.Validate(m);
                    break;
                case RestyleRecipe.ToUrpLit:
                case RestyleRecipe.ToUrpUnlit:
                case RestyleRecipe.ToUrpParticles:
                    // URP's own inspectors set keywords and render state from the properties (public ValidateMaterial).
                    var type = Type.GetType(k_UrpGuis[recipe] + ", Unity.RenderPipelines.Universal.Editor");
                    if (type != null && Activator.CreateInstance(type) is UnityEditor.ShaderGUI gui)
                        gui.ValidateMaterial(m);
                    break;
            }
        }
    }
}
