using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Opt-in alternative to runtime injection, for projects that want Looksmith's passes listed on the
    /// renderer explicitly. When a renderer has this feature, the injector leaves that renderer alone,
    /// so passes are never enqueued twice. Adding it is a renderer-asset write and goes through a Changeset.
    /// </summary>
    [DisallowMultipleRendererFeature("Looksmith")]
    public sealed class LooksmithRendererFeature : ScriptableRendererFeature
    {
        public override void Create()
        {
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            LooksmithPassInjector.EnqueueForCamera(renderer, renderingData.cameraData.camera, renderingData.cameraData.renderType);
        }
    }
}
