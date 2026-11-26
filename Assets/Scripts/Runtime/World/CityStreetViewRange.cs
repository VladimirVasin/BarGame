using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    internal sealed class CityStreetViewRange : MonoBehaviour
    {
        internal static CityStreetViewRange Install(Camera camera) =>
            camera.TryGetComponent(out CityStreetViewRange range) ? range : camera.gameObject.AddComponent<CityStreetViewRange>();
    }

    internal sealed class CityStreetViewRangePass : ScriptableRenderPass
    {
        private Material material;
        private sealed class PassData { public Material Material; }

        internal CityStreetViewRangePass() => renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (material == null)
                material = CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Shaders/CityStreetViewRange"));
            var resources = frameData.Get<UniversalResourceData>();
            using var builder = graph.AddRasterRenderPass<PassData>("City street draw radius", out var data);
            data.Material = material;
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 0, MeshTopology.Triangles, 3, 1));
        }

        internal void Dispose() => CoreUtils.Destroy(material);
    }
}
