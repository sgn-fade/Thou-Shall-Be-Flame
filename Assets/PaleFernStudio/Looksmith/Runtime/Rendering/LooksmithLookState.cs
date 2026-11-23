using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The resolved look for one camera: the Volume-blended LooksmithLook turned into shader globals and a baked ramp.
    /// One instance per camera, so each camera's own Volume stack (and Look Zones) drives its own look.
    /// </summary>
    public sealed class LooksmithLookState
    {
        static readonly Dictionary<Camera, LooksmithLookState> s_States = new Dictionary<Camera, LooksmithLookState>();
        static readonly List<Camera> s_Dead = new List<Camera>();

        public static LooksmithLookState Get(Camera camera)
        {
            if (!s_States.TryGetValue(camera, out var state))
            {
                PruneDestroyedCameras();
                state = new LooksmithLookState();
                s_States.Add(camera, state);
            }
            return state;
        }

        static void PruneDestroyedCameras()
        {
            s_Dead.Clear();
            foreach (var pair in s_States)
                if (pair.Key == null)
                    s_Dead.Add(pair.Key);
            foreach (var camera in s_Dead)
            {
                s_States[camera].Dispose();
                s_States.Remove(camera);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            foreach (var state in s_States.Values)
                state.Dispose();
            s_States.Clear();
            s_StyleTypes = null;
        }

        /// <summary>True when the camera's Volume stack has an active Looksmith Look.</summary>
        public bool lookActive { get; private set; }
        /// <summary>The look asks for the depth texture (soft particles); read by the injector for ConfigureInput.</summary>
        public bool wantsDepth { get; private set; }

        /// <summary>The scene's light level for this camera (set by the setup pass; see LooksmithSetupPass.SceneLight).</summary>
        public float sceneLight = 1f;

        /// <summary>The Look's Lines group, resolved for this camera (drawn by the Outlines module).</summary>
        public struct LineSettings
        {
            public bool enabled;
            public float weight, fromSurface, depth, normal, colour, fadeStart, fadeEnd, lightWeight, jitter, wobble, outlineWidth;
            public bool silhouettesOnly, objectEdges;
            public int layerMask;
            public Color color;
            /// <summary>Pro inputs (object ids, colour edges, layer masks) are needed.</summary>
            public bool wantsIds => enabled && (objectEdges || layerMask != -1);
        }

        public LineSettings lines;

        /// <summary>The Look's Finish group (the last layer of the Art Style stack).</summary>
        public struct FinishSettings
        {
            public bool active;
            public float grain, grainSize, vignette, vignetteFromPalette, paper, paperScale, fringe, lutContribution;
            public bool paperOnSurfaces;
            public Texture paperTexture, lut;
            /// <summary>The sky keeps its rendered look (Style Sky off): styles and Finish leave it alone.</summary>
            public bool keepSky;
            /// <summary>With keepSky: anything farther than this (m) counts as sky too (0 = only the background).</summary>
            public float skyDistance;
            /// <summary>Paper on the surfaces reads the scene's depth and normals.</summary>
            public ScriptableRenderPassInput inputs => paper > 0.001f && paperOnSurfaces ? ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal : ScriptableRenderPassInput.None;
        }

        public FinishSettings finish;

        /// <summary>What the Art Style stack needs from a camera this frame (decided before URP updates its Volumes).</summary>
        public struct StylePlan
        {
            public bool active;
            public ScriptableRenderPassInput inputs;
            /// <summary>An active style limits itself to some rendering layers: the Pro mask buffer is needed.</summary>
            public bool wantsMask;
        }

        public StylePlan styles;

        public static FinishSettings FinishOf(LooksmithLook look)
        {
            var f = new FinishSettings
            {
                grain = look.grain.value,
                grainSize = look.grainSize.value,
                vignette = look.vignette.value,
                vignetteFromPalette = look.vignetteFromPalette.value,
                paper = look.paper.value,
                paperScale = look.paperScale.value,
                paperOnSurfaces = look.paperOnSurfaces.value,
                paperTexture = look.paperTexture.value,
                fringe = look.chromaticFringe.value,
                lut = look.gradingLut.value,
                lutContribution = look.gradingLut.value != null ? look.gradingLutContribution.value : 0f,
                keepSky = !look.styleSky.value,
                skyDistance = look.skyDistance.value,
            };
            f.active = f.grain > 0.001f || f.vignette > 0.001f || f.paper > 0.001f || f.fringe > 0.001f || f.lutContribution > 0.001f;
            return f;
        }

        static System.Type[] s_StyleTypes;

        /// <summary>Every Art Style override type in this build (Lite has fewer).</summary>
        public static System.Type[] StyleTypes
        {
            get
            {
                if (s_StyleTypes == null)
                {
                    var list = new List<System.Type>();
                    var all = VolumeManager.instance.baseComponentTypeArray;
                    if (all != null)
                        foreach (var t in all)
                            if (typeof(LooksmithStyle).IsAssignableFrom(t) && !t.IsAbstract)
                                list.Add(t);
                    s_StyleTypes = list.ToArray();
                }
                return s_StyleTypes;
            }
        }

        static StylePlan PlanStyles(VolumeStack stack, in FinishSettings finish)
        {
            var plan = new StylePlan { active = finish.active, inputs = finish.active ? finish.inputs : ScriptableRenderPassInput.None };
            if (stack == null)
                return plan;
            foreach (var type in StyleTypes)
            {
                if (!(stack.GetComponent(type) is LooksmithStyle style) || !style.IsStyleActive())
                    continue;
                plan.active = true;
                plan.inputs |= style.inputs;
                plan.wantsMask |= style.layers.value != -1;
            }
            // telling the sky apart takes the depth texture
            if (plan.active && finish.keepSky)
                plan.inputs |= ScriptableRenderPassInput.Depth;
            return plan;
        }

        /// <summary>
        /// The style plan for this frame: the camera's previous frame, or on its very first frame the active Look Volumes
        /// (any overridden style with Intensity above 0), like <see cref="PlannedLines"/>.
        /// </summary>
        public static StylePlan PlannedStyles(Camera camera)
        {
            var state = Get(camera);
            if (state.evaluated)
                return state.styles;
            var plan = default(StylePlan);
            foreach (var v in LooksmithRuntime.ActiveVolumes)
            {
                if (v == null || !v.TryGetComponent(out Volume volume) || !volume.enabled || volume.sharedProfile == null)
                    continue;
                foreach (var component in volume.sharedProfile.components)
                {
                    if (component is LooksmithStyle style && style.active && style.intensity.overrideState && style.intensity.value > 0.001f)
                    {
                        plan.active = true;
                        plan.inputs |= style.inputs;
                        plan.wantsMask |= style.layers.overrideState && style.layers.value != -1;
                    }
                    else if (component is LooksmithLook look && look.IsActive())
                    {
                        var f = FinishOf(look);
                        if (f.active)
                        {
                            plan.active = true;
                            plan.inputs |= f.inputs;
                        }
                        if (f.keepSky)
                            plan.inputs |= ScriptableRenderPassInput.Depth;
                    }
                }
            }
            return plan;
        }

        /// <summary>True once this camera's Volume stack has been read (its first frame is still unknown).</summary>
        public bool evaluated { get; private set; }

        public static LineSettings LinesOf(LooksmithLook look) => new LineSettings
        {
            enabled = look.lineWeight.value > 0.01f || look.outlineWidth.value > 0.01f,
            weight = look.lineWeight.value,
            color = look.lineColor.value.linear,
            fromSurface = look.lineFromSurface.value,
            silhouettesOnly = look.silhouettesOnly.value,
            depth = look.lineDepthSensitivity.value,
            normal = look.lineNormalSensitivity.value,
            colour = look.lineColorSensitivity.value,
            objectEdges = look.lineObjectEdges.value,
            layerMask = look.lineLayerMask.value,
            fadeStart = look.lineFadeStart.value,
            fadeEnd = Mathf.Max(look.lineFadeEnd.value, look.lineFadeStart.value + 0.01f),
            lightWeight = look.lineLightWeight.value,
            jitter = look.lineJitter.value,
            wobble = look.lineWobble.value,
            outlineWidth = look.outlineWidth.value,
        };

        /// <summary>
        /// The line settings to plan this frame's passes with. Passes are enqueued before URP updates the camera's
        /// Volume stack, so a camera uses its previous frame; on its very first frame (a new camera, a screenshot
        /// camera) the active Look Volumes are read directly, so the first image already has its lines.
        /// </summary>
        public static LineSettings PlannedLines(Camera camera)
        {
            var state = Get(camera);
            if (state.evaluated)
                return state.lines;
            var planned = default(LineSettings);
            foreach (var v in LooksmithRuntime.ActiveVolumes)
            {
                if (v == null || !v.TryGetComponent(out Volume volume) || !volume.enabled || volume.sharedProfile == null)
                    continue;
                if (!volume.sharedProfile.TryGet(out LooksmithLook look) || !look.IsActive())
                    continue;
                var l = LinesOf(look);
                if (!planned.enabled || l.weight > planned.weight)
                {
                    float outline = Mathf.Max(planned.outlineWidth, l.outlineWidth);
                    bool ids = planned.wantsIds;
                    planned = l;
                    planned.outlineWidth = outline;
                    if (ids && !planned.wantsIds)
                        planned.objectEdges = true;
                }
                else
                {
                    planned.outlineWidth = Mathf.Max(planned.outlineWidth, l.outlineWidth);
                    planned.objectEdges |= l.objectEdges || l.layerMask != -1;
                }
                planned.enabled |= l.enabled;
            }
            return planned;
        }

        Texture2D m_Ramp;
        int m_RampHash = int.MinValue;

        /// <summary>The Look's palette roles as authored (OKLCH): key, shadow, sky, accent, fog.</summary>
        public readonly Vector3[] paletteLch = new Vector3[5];

        /// <summary>The resolved shadow colour (linear), after the Look's macros.</summary>
        public Color shadowColor => new Color(m_Shadow.x, m_Shadow.y, m_Shadow.z);

        Vector4 m_Key, m_Shadow, m_Sky, m_Accent, m_Fog, m_Light, m_Ambient, m_Rim, m_RimColor, m_Spec, m_SpecColor, m_Terminator, m_Atmos, m_Atmos2, m_Surface, m_Vertex;

        public void Evaluate(VolumeStack stack)
        {
            Evaluate(stack?.GetComponent<LooksmithLook>());
            styles = PlanStyles(stack, finish);
        }

        /// <summary>Resolves a Look directly (material previews, tools); cameras go through their Volume stack.</summary>
        public void Evaluate(LooksmithLook look)
        {
            evaluated = true;
            lookActive = look != null && look.IsActive();
            wantsDepth = lookActive && look.softParticles.value;
            lines = default;
            finish = default;
            styles = default;
            if (!lookActive)
                return;
            lines = LinesOf(look);
            finish = FinishOf(look);

            // Macros (0.5 = as authored)
            float soft = look.softness.value - 0.5f;
            float softMul = Mathf.Pow(2f, soft * 3f);
            float contrast = look.contrast.value;
            float warmth = look.warmth.value - 0.5f;
            float rimMul = look.rim.value * 2f;
            float shineMul = look.shine.value * 2f;
            float hazeMul = look.haze.value * 2f;

            var keyLch = look.keyColor.value;
            var shadowLch = look.shadowColor.value;
            // warmth is the light's temperature: the key turns toward amber (or ice blue) and strengthens, and the whole
            // light (key, shadow, the palette's ambient) shifts warm or cool, the shadow a little less than the key so
            // warm light keeps cooler shadows; it reads clearly even on a palette that was warm or cool to begin with
            keyLch.z = Mathf.Repeat(keyLch.z + LooksmithColor.HueDelta(keyLch.z, warmth >= 0f ? 65f : 245f) * Mathf.Clamp01(Mathf.Abs(warmth) * 1.2f), 360f);
            shadowLch.z = Mathf.Repeat(shadowLch.z + LooksmithColor.HueDelta(shadowLch.z, 275f) * Mathf.Clamp01(Mathf.Abs(warmth) * 0.4f) * Mathf.Sign(warmth), 360f);
            keyLch.y = Mathf.Max(0f, keyLch.y + Mathf.Abs(warmth) * 0.12f);
            float t = warmth * 2f;
            var temperature = new Vector3(1f + 0.12f * t, 1f, Mathf.Max(0.2f, 1f - 0.3f * t));
            var shadowTemperature = new Vector3(1f + 0.08f * t, 1f, Mathf.Max(0.2f, 1f - 0.16f * t));
            shadowLch.x *= Mathf.Lerp(1.45f, 0.6f, contrast);

            var key = Vector3.Scale(LooksmithColor.OklchToLinearInGamut(keyLch), temperature);
            float keyMax = Mathf.Max(key.x, Mathf.Max(key.y, key.z), 1e-4f);
            key /= keyMax; // a tint: brightest channel = 1
            var shadow = Vector3.Scale(LooksmithColor.OklchToLinearInGamut(shadowLch), shadowTemperature);
            var sky = Vector3.Scale(LooksmithColor.OklchToLinearInGamut(look.skyColor.value), shadowTemperature);
            var accent = LooksmithColor.OklchToLinearInGamut(look.accentColor.value);
            var fog = LooksmithColor.OklchToLinearInGamut(look.fogColor.value);

            paletteLch[0] = look.keyColor.value;
            paletteLch[1] = look.shadowColor.value;
            paletteLch[2] = look.skyColor.value;
            paletteLch[3] = look.accentColor.value;
            paletteLch[4] = look.fogColor.value;
            m_Key = new Vector4(key.x, key.y, key.z, 1f);
            m_Shadow = new Vector4(shadow.x, shadow.y, shadow.z, 1f);
            m_Sky = new Vector4(sky.x, sky.y, sky.z, 1f);
            m_Accent = new Vector4(accent.x, accent.y, accent.z, 1f);
            m_Fog = new Vector4(fog.x, fog.y, fog.z, 1f);
            m_Light = new Vector4(look.painted.value, look.pbrBlend.value, look.shadowHardness.value, look.keyTint.value);
            m_Ambient = new Vector4(look.ambient.value * Mathf.Lerp(1.35f, 0.7f, contrast), look.ambientFlatten.value, look.ambientFromPalette.value, look.reflections.value);
            m_Rim = new Vector4(look.rimStrength.value * rimMul, look.rimWidth.value, look.rimSoftness.value, look.rimLightFacing.value);
            m_RimColor = Vector4.Lerp(Vector4.Lerp(m_Key, Vector4.one, 0.35f), m_Accent * 1.2f, look.rimFromAccent.value);
            m_Spec = new Vector4(look.specularStrength.value * shineMul, look.specularSize.value, look.specularSoftness.value, (float)look.specularStyle.value);
            m_SpecColor = Vector4.Lerp(m_Key, Vector4.one, 0.5f);
            m_Terminator = new Vector4(look.terminatorAccent.value, look.keyLevel.value, 0f, look.additionalLights.value);
            m_Atmos = new Vector4(look.hazeStart.value, look.hazeDensity.value * Mathf.Max(hazeMul, 0f), look.hazeHeightFalloff.value, Mathf.Clamp01(look.hazeMax.value * hazeMul));
            m_Atmos2 = new Vector4(look.hazeDesaturation.value, look.hazeHeightBase.value, look.hazeFromSky.value, 0f);
            m_Surface = new Vector4(look.saturation.value, look.facets.value, look.brushScale.value, 0f);
            m_Vertex = new Vector4(look.vertexSnap.value, look.affineWarp.value, 0f, 0f);

            var ramp = new LooksmithRamp.Settings
            {
                bands = look.bands.value,
                bandMix = Mathf.Clamp01(look.bandMix.value - soft * 0.8f),
                edge = Mathf.Clamp(look.edgeSoftness.value * softMul, 0.002f, 0.45f),
                terminator = look.terminator.value,
                wrap = Mathf.Clamp01(look.wrap.value + soft * 0.6f),
                spread = look.bandSpread.value,
                gradientWeight = look.gradientRamp.value,
                gradientShadow = look.gradientShadow.value.linear,
                gradientMid = look.gradientMid.value.linear,
                gradientLit = look.gradientLit.value.linear,
            };
            int hash = ramp.Hash();
            if (m_Ramp == null)
                m_Ramp = LooksmithRamp.Create();
            if (hash != m_RampHash)
            {
                LooksmithRamp.Bake(m_Ramp, ramp);
                m_RampHash = hash;
            }
        }

        public void Push(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(LooksmithShaderIDs.Bypass, 0f);
            cmd.SetGlobalFloat(LooksmithShaderIDs.Enabled, lookActive ? 1f : 0f);
            cmd.SetGlobalVector(LooksmithShaderIDs.Split, Vector4.zero);   // cameras set their own after Push
            cmd.SetGlobalVector(LooksmithShaderIDs.DebugSurface, Vector4.zero);
            cmd.SetGlobalFloat(LooksmithShaderIDs.DepthAvailable, wantsDepth ? 1f : 0f);
            if (!lookActive)
                return;
            cmd.SetGlobalVector(LooksmithShaderIDs.KeyColor, m_Key);
            cmd.SetGlobalVector(LooksmithShaderIDs.ShadowColor, m_Shadow);
            cmd.SetGlobalVector(LooksmithShaderIDs.SkyColor, m_Sky);
            cmd.SetGlobalVector(LooksmithShaderIDs.AccentColor, m_Accent);
            // Without a Looksmith Sky the haze is the palette's fog colour, lit like the scene: a clear day keeps the
            // palette's brightness, a night darkens it (fog that glowed over a dark scene read as a light source).
            var fog = m_Fog;
            if (Shader.GetGlobalVector(LooksmithShaderIDs.SkyHorizon).w <= 0.5f)
            {
                float lit = Mathf.Clamp01(sceneLight / 0.8f);
                fog = new Vector4(m_Fog.x * lit, m_Fog.y * lit, m_Fog.z * lit, m_Fog.w);
            }
            cmd.SetGlobalVector(LooksmithShaderIDs.FogColor, fog);
            cmd.SetGlobalVector(LooksmithShaderIDs.LightParams, m_Light);
            // A baked scene's lightmaps and light probes hold its own lights (torches, lamps) as well as the sky: they keep
            // the strength they were baked with and take only a little of the palette's sky colour.
            var ambient = m_Ambient;
            if (LightmapSettings.lightmaps.Length > 0)
            {
                ambient.x = Mathf.Max(ambient.x, 1f);
                ambient.z *= 0.35f;
            }
            cmd.SetGlobalVector(LooksmithShaderIDs.AmbientParams, ambient);
            cmd.SetGlobalVector(LooksmithShaderIDs.RimParams, m_Rim);
            cmd.SetGlobalVector(LooksmithShaderIDs.RimColor, m_RimColor);
            cmd.SetGlobalVector(LooksmithShaderIDs.SpecParams, m_Spec);
            cmd.SetGlobalVector(LooksmithShaderIDs.SpecColor, m_SpecColor);
            cmd.SetGlobalVector(LooksmithShaderIDs.TerminatorParams, m_Terminator);
            cmd.SetGlobalVector(LooksmithShaderIDs.AtmosParams, m_Atmos);
            cmd.SetGlobalVector(LooksmithShaderIDs.AtmosParams2, m_Atmos2);
            cmd.SetGlobalVector(LooksmithShaderIDs.SurfaceParams, m_Surface);
            cmd.SetGlobalVector(LooksmithShaderIDs.VertexParams, m_Vertex);
            cmd.SetGlobalTexture(LooksmithShaderIDs.RampAtlas, m_Ramp);
            cmd.SetGlobalTexture(LooksmithShaderIDs.BrushNoise, LooksmithBrushNoise.Get());
        }

        public void Push(UnsafeCommandBuffer cmd) => Push(CommandBufferHelpers.GetNativeCommandBuffer(cmd));

        /// <summary>A Look state that isn't tied to a camera (material previews). Dispose it when done.</summary>
        public static LooksmithLookState CreateDetached() => new LooksmithLookState();

        public void Release() => Dispose();

        void Dispose()
        {
            if (m_Ramp != null)
                CoreUtils.Destroy(m_Ramp);
            m_Ramp = null;
        }
    }
}
