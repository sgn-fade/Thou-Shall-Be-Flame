using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// First Looksmith pass of every styled camera (BeforeRendering). Reads the camera's Volume stack,
    /// resolves the Look and pushes the Looksmith shader globals for this camera.
    /// </summary>
    public sealed class LooksmithSetupPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public LooksmithLookState state;
            public CameraType cameraType;
            public Vector4 split, debugSurface;
        }

        public LooksmithSetupPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
            profilingSampler = new ProfilingSampler("Looksmith Setup");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var state = LooksmithLookState.Get(cameraData.camera);
            state.Evaluate(VolumeManager.instance.stack);
            state.sceneLight = LooksmithRuntime.IsPreviewCamera(cameraData.camera) ? 1f : SceneLight(frameData.Get<UniversalLightData>());

            using (var builder = renderGraph.AddUnsafePass<PassData>("Looksmith Setup", out var passData, profilingSampler))
            {
                passData.state = state;
                passData.cameraType = cameraData.cameraType;
                passData.split = LooksmithCompare.ForCamera(cameraData.cameraType);
                bool lightingOnly = LooksmithDebug.mode == LooksmithDebugView.LightingOnly && cameraData.cameraType != CameraType.Preview;
                passData.debugSurface = new Vector4(lightingOnly ? 1f : 0f, 0f, 0f, 0f);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    data.state.Push(context.cmd);
                    context.cmd.SetGlobalVector(LooksmithShaderIDs.Split, data.split);
                    context.cmd.SetGlobalVector(LooksmithShaderIDs.DebugSurface, data.debugSurface);
                    LooksmithPassDiagnostics.RecordSetup(data.cameraType);
                });
            }
        }

        static readonly Vector3[] s_Up = { Vector3.up };
        static readonly Color[] s_UpColor = new Color[1];

        /// <summary>
        /// How much light the scene has: the ambient from above plus a share of the main light (linear luminance; a
        /// clear day is about 0.8 or more, a night under 0.1).
        /// </summary>
        static float SceneLight(UniversalLightData lightData)
        {
            RenderSettings.ambientProbe.Evaluate(s_Up, s_UpColor);
            var a = s_UpColor[0];
            float level = Mathf.Max(0f, 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b);
            int main = lightData.mainLightIndex;
            if (main >= 0 && main < lightData.visibleLights.Length)
            {
                var c = lightData.visibleLights[main].finalColor;
                level += 0.3f * Mathf.Max(0f, 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            }
            return level;
        }
    }
}
