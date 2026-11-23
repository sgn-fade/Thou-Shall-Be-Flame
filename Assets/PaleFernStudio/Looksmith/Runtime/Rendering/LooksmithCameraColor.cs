using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// Hands a full-screen pass's result to the camera. The last camera of a stack takes the new texture as its colour. A
    /// camera with another after it (a scene camera under a HUD overlay) can't: the next camera draws on the stack's shared
    /// colour target, so a new texture is dropped and the image lost the pass (Comic Line's halftone vanished under a HUD).
    /// There the result is copied back into the texture the pass read.
    /// </summary>
    public static class LooksmithCameraColor
    {
        public static void Set(RenderGraph renderGraph, UniversalResourceData resources, UniversalCameraData cameraData,
            TextureHandle source, TextureHandle result, string passName)
        {
            if (cameraData.resolveFinalTarget)
            {
                resources.cameraColor = result;
                return;
            }
            if (!result.Equals(source))
                renderGraph.AddBlitPass(result, source, Vector2.one, Vector2.zero, passName: passName);
        }
    }
}
