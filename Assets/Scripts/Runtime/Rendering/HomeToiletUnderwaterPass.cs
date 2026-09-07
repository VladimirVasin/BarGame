using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>Only the camera owning the bowl visit receives this pass, before the shared PS1 finish.</summary>
    internal sealed class HomeToiletUnderwaterPass : ScriptableRenderPass
    {
        private Material material;
        private static readonly int StateId = Shader.PropertyToID("_BowlSubmersion");

        public HomeToiletUnderwaterPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
            requiresIntermediateTexture = true;
        }

        public bool IsNeeded(Camera camera)
        {
            if (camera.cameraType != CameraType.Game ||
                !camera.TryGetComponent(out HomeToiletUnderwaterEffect effect) ||
                !effect.IsActive || effect.Amount <= 0f) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/HomeToiletUnderwater");
                if (shader == null) return false;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            return true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            Camera camera = frameData.Get<UniversalCameraData>().camera;
            if (material == null || !camera.TryGetComponent(out HomeToiletUnderwaterEffect effect) ||
                !effect.IsActive || effect.Amount <= 0f) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid()) return;
            material.SetVector(StateId, new Vector4(effect.Amount, effect.Clock, 0f, 0f));
            TextureDesc descriptor = graph.GetTextureDesc(source);
            descriptor.name = "Home Toilet Submerged View";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            TextureHandle destination = graph.CreateTexture(descriptor);
            graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0),
                "Home Toilet Underwater");
            resources.cameraColor = destination;
        }

        public void Dispose() { CoreUtils.Destroy(material); material = null; }
    }
}
