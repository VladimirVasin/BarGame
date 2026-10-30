using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>A depth-tested pixel contour of one authored control, confined to its gameplay camera.</summary>
    internal sealed class WorldControlOutlineView : IDisposable
    {
        private const int MaximumParts = 16;
        private static Material sharedMaterial;
        private static int users;
        private static readonly int ControlCenter = Shader.PropertyToID("_ControlCenterOS");
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Renderer[] sources = new Renderer[MaximumParts];
        private readonly MeshFilter[] meshes = new MeshFilter[MaximumParts];
        private readonly MeshFilter[] overlayMeshes = new MeshFilter[MaximumParts];
        private readonly MeshRenderer[] overlays = new MeshRenderer[MaximumParts];
        private Camera targetCamera;
        private int count;
        private bool disposed;
        public int RenderedPartCount { get; private set; }

        public WorldControlOutlineView()
        {
            if (sharedMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/WorldControlOutline");
                if (shader == null) throw new InvalidOperationException("Missing shared world-control outline shader.");
                sharedMaterial = new Material(shader) { name = "Shared World Control Outline", hideFlags = HideFlags.HideAndDontSave };
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
                if (overlays[count] == null)
                {
                    var overlay = new GameObject("World Control Contour") { hideFlags = HideFlags.HideAndDontSave };
                    overlayMeshes[count] = overlay.AddComponent<MeshFilter>();
                    overlays[count] = overlay.AddComponent<MeshRenderer>();
                    overlays[count].sharedMaterial = sharedMaterial;
                    overlays[count].shadowCastingMode = ShadowCastingMode.Off;
                    overlays[count].receiveShadows = false;
                    overlays[count].lightProbeUsage = LightProbeUsage.Off;
                    overlays[count].reflectionProbeUsage = ReflectionProbeUsage.Off;
                    overlays[count].forceRenderingOff = true;
                }
                overlays[count].gameObject.layer = source.gameObject.layer;
                overlays[count].renderingLayerMask = source.renderingLayerMask;
                overlays[count].transform.SetParent(mesh.transform, false);
                overlays[count].transform.localPosition = Vector3.zero;
                overlays[count].transform.localRotation = Quaternion.identity;
                overlays[count].transform.localScale = Vector3.one;
                overlayMeshes[count].sharedMesh = mesh.sharedMesh;
                properties.SetVector(ControlCenter, mesh.sharedMesh.bounds.center);
                overlays[count].SetPropertyBlock(properties);
                overlays[count].enabled = false;
                count++;
            }
        }

        public void Present(Camera camera)
        {
            RenderedPartCount = 0;
            if (disposed || camera == null || sharedMaterial == null) return;
            targetCamera = camera;
            for (int index = 0; index < count; index++)
            {
                Renderer source = sources[index];
                MeshFilter mesh = meshes[index];
                MeshRenderer overlay = overlays[index];
                overlay.enabled = false;
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || mesh == null) continue;
                Vector3 centre = camera.WorldToViewportPoint(source.bounds.center);
                if (centre.z <= camera.nearClipPlane || centre.x < 0f || centre.x > 1f || centre.y < 0f || centre.y > 1f) continue;
                overlay.enabled = true;
                RenderedPartCount++;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool allowed = camera == targetCamera;
            for (int index = 0; index < count; index++)
                if (overlays[index] != null) overlays[index].forceRenderingOff = !allowed;
        }

        public void Hide()
        {
            targetCamera = null;
            RenderedPartCount = 0;
            for (int index = 0; index < count; index++)
                if (overlays[index] != null) { overlays[index].enabled = false; overlays[index].forceRenderingOff = true; }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != targetCamera) return;
            for (int index = 0; index < count; index++)
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
            for (int index = 0; index < MaximumParts; index++)
            {
                if (overlays[index] == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(overlays[index].gameObject);
                else UnityEngine.Object.DestroyImmediate(overlays[index].gameObject);
            }
            if (--users != 0 || sharedMaterial == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(sharedMaterial);
            else UnityEngine.Object.DestroyImmediate(sharedMaterial);
            sharedMaterial = null;
        }
    }
}
