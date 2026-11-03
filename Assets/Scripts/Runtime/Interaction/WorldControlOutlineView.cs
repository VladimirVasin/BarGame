using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>A restrained surface wash and continuous depth-tested contour of one selected control.</summary>
    internal sealed class WorldControlOutlineView : IDisposable
    {
        private const int MaximumParts = 16;
        private const int LayerCount = 3;
        private static Material[] sharedMaterials;
        private static Material[] sharedLootMaterials;
        private static int users;
        private static readonly int ControlCenter = Shader.PropertyToID("_ControlCenterOS");
        private static readonly int ControlExtents = Shader.PropertyToID("_ControlExtentsOS");
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Renderer[] sources = new Renderer[MaximumParts];
        private readonly MeshFilter[] meshes = new MeshFilter[MaximumParts];
        private readonly MeshFilter[] overlayMeshes = new MeshFilter[MaximumParts * LayerCount];
        private readonly MeshRenderer[] overlays = new MeshRenderer[MaximumParts * LayerCount];
        private Camera targetCamera;
        private int count, retainedOverlays;
        private bool disposed;
        private readonly bool lootHighlight;
        public int RenderedPartCount { get; private set; }
        public int RetainedOverlayCount => disposed ? 0 : retainedOverlays;

        public WorldControlOutlineView(bool lootHighlight = false)
        {
            this.lootHighlight = lootHighlight;
            if (sharedMaterials == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/WorldControlOutline");
                if (shader == null) throw new InvalidOperationException("Missing shared world-control outline shader.");
                // Separate bounded draws guarantee all layers in URP without
                // introducing a renderer feature or relying on multipass dispatch.
                sharedMaterials = new[]
                {
                    Layer("Outer Edge", 3f, new Color(.035f, .03f, .025f, 1f), CullMode.Front, 2020),
                    Layer("Ivory Edge", 2f, new Color(.88f, .84f, .73f, 1f), CullMode.Front, 2021),
                    Layer("Surface", 0f, new Color(.88f, .84f, .73f, .18f), CullMode.Back, 2022)
                };
                Material Layer(string name, float width, Color color, CullMode cull, int queue)
                {
                    var material = new Material(shader)
                    { name = "Shared Control " + name, hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
                    material.SetFloat("_OutlineWidth", width);
                    material.SetColor("_OverlayColor", color);
                    material.SetInt("_Cull", (int)cull);
                    return material;
                }
            }
            if (lootHighlight && sharedLootMaterials == null)
            {
                // URP leaves the low user bits available. Bit 2 (value 4) is
                // separate from mirror aperture 2 and SMAA's private stencil 64.
                // Clear our bit after the contour; transparent bottle cells then
                // keep their ordinary colour/depth instead of becoming a wash.
                sharedLootMaterials = new[]
                {
                    LootLayer("Silhouette Mask", 0f, 0, 4, CompareFunction.Always, StencilOp.Replace, 4, CullMode.Back, 2019),
                    LootLayer("Yellow Contour", 1.5f, 15, 4, CompareFunction.NotEqual, StencilOp.Keep, 0, CullMode.Front, 2021),
                    LootLayer("Silhouette Clear", 0f, 0, 0, CompareFunction.Always, StencilOp.Replace, 4, CullMode.Back, 2023)
                };
                Material LootLayer(string name, float width, int channels, int reference,
                    CompareFunction comparison, StencilOp operation, int writeMask, CullMode cull, int queue)
                {
                    var material = new Material(sharedMaterials[0].shader)
                    { name = "Shared Loot " + name, hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
                    material.SetFloat("_OutlineWidth", width);
                    material.SetColor("_OverlayColor", new Color(1f, .78f, .035f, 1f));
                    material.SetInt("_Cull", (int)cull);
                    material.SetInt("_ColorMask", channels);
                    material.SetInt("_StencilRef", reference);
                    material.SetInt("_StencilReadMask", 4);
                    material.SetInt("_StencilWriteMask", writeMask);
                    material.SetInt("_StencilComp", (int)comparison);
                    material.SetInt("_StencilPass", (int)operation);
                    return material;
                }
            }
            users++;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        public void Select(IReadOnlyList<Renderer> renderers)
        {
            Clear();
            if (renderers == null || renderers.Count > MaximumParts)
                throw new ArgumentException("A world control must supply a bounded authored renderer list.", nameof(renderers));
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer source = renderers[index];
                MeshFilter mesh = source != null ? source.GetComponent<MeshFilter>() : null;
                if (mesh == null || mesh.sharedMesh == null)
                    throw new ArgumentException("World-control contours require authored mesh renderers.", nameof(renderers));
                sources[count] = source;
                meshes[count] = mesh;
                for (int layer = 0; layer < LayerCount; layer++)
                {
                    int slot = count * LayerCount + layer;
                    if (overlays[slot] == null)
                    {
                        var overlay = new GameObject("World Control Selection " + layer) { hideFlags = HideFlags.HideAndDontSave };
                        overlayMeshes[slot] = overlay.AddComponent<MeshFilter>();
                        overlays[slot] = overlay.AddComponent<MeshRenderer>();
                        overlays[slot].sharedMaterial = (lootHighlight ? sharedLootMaterials : sharedMaterials)[layer];
                        overlays[slot].shadowCastingMode = ShadowCastingMode.Off;
                        overlays[slot].receiveShadows = false;
                        overlays[slot].lightProbeUsage = LightProbeUsage.Off;
                        overlays[slot].reflectionProbeUsage = ReflectionProbeUsage.Off;
                        overlays[slot].forceRenderingOff = true;
                        retainedOverlays++;
                    }
                    MeshRenderer overlayRenderer = overlays[slot];
                    overlayRenderer.gameObject.layer = source.gameObject.layer;
                    overlayRenderer.renderingLayerMask = source.renderingLayerMask;
                    overlayRenderer.transform.SetParent(mesh.transform, false);
                    overlayRenderer.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    overlayRenderer.transform.localScale = Vector3.one;
                    overlayMeshes[slot].sharedMesh = mesh.sharedMesh;
                    properties.SetVector(ControlCenter, mesh.sharedMesh.bounds.center);
                    properties.SetVector(ControlExtents, mesh.sharedMesh.bounds.extents);
                    overlayRenderer.SetPropertyBlock(properties);
                    overlayRenderer.enabled = false;
                }
                count++;
            }
        }

        public void Present(Camera camera)
        {
            RenderedPartCount = 0;
            if (disposed || camera == null || sharedMaterials == null) return;
            targetCamera = camera;
            for (int index = 0; index < count; index++)
            {
                Renderer source = sources[index];
                MeshFilter mesh = meshes[index];
                for (int layer = 0; layer < LayerCount; layer++) overlays[index * LayerCount + layer].enabled = false;
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || mesh == null) continue;
                Vector3 centre = camera.WorldToViewportPoint(source.bounds.center);
                if (centre.z <= camera.nearClipPlane || centre.x < 0f || centre.x > 1f || centre.y < 0f || centre.y > 1f) continue;
                for (int layer = 0; layer < LayerCount; layer++) overlays[index * LayerCount + layer].enabled = true;
                RenderedPartCount++;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool allowed = camera == targetCamera &&
                (!lootHighlight || camera.cameraType == CameraType.Game &&
                    !camera.TryGetComponent(out Rendering.ScenePresentationExclusion _));
            for (int index = 0; index < count * LayerCount; index++)
                if (overlays[index] != null) overlays[index].forceRenderingOff = !allowed;
        }

        public void Hide()
        {
            targetCamera = null;
            RenderedPartCount = 0;
            for (int index = 0; index < count * LayerCount; index++)
                if (overlays[index] != null) { overlays[index].enabled = false; overlays[index].forceRenderingOff = true; }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != targetCamera) return;
            for (int index = 0; index < count * LayerCount; index++)
                if (overlays[index] != null) overlays[index].forceRenderingOff = true;
        }

        public void Clear()
        {
            Hide();
            Array.Clear(sources, 0, count);
            Array.Clear(meshes, 0, count);
            count = RenderedPartCount = 0;
            targetCamera = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            for (int index = 0; index < overlays.Length; index++)
            {
                if (overlays[index] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(overlays[index].gameObject);
                else UnityEngine.Object.DestroyImmediate(overlays[index].gameObject);
            }
            if (--users != 0) return;
            if (sharedMaterials != null) foreach (Material material in sharedMaterials) Release(material);
            if (sharedLootMaterials != null) foreach (Material material in sharedLootMaterials) Release(material);
            sharedMaterials = null;
            sharedLootMaterials = null;
            static void Release(UnityEngine.Object value)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(value);
                else UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
