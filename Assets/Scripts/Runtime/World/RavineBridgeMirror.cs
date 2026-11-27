using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    // The clamped head keeps its setting. A small directional view samples the actual
    // bridge; each fragment chooses its reflected ray from the current eye position.
    internal sealed class RavineBridgeMirror : MonoBehaviour
    {
        private WorldItemPickup pickup;
        private Transform pivot;
        private Renderer pane;
        private Material original;
        private Camera viewer, capture;
        private RenderTexture image;
        private const float ViewAngle = 60f;
        private readonly UniversalRenderPipeline.SingleCameraRequest request = new();
        private Vector3 glassPoint, direction, normal;
        private float nextSample;
        internal Vector3 GlassPoint => glassPoint;
        internal Vector3 Normal => normal;
        internal RenderTexture BridgeView => image;

        internal void Initialize(WorldItemPickup owner, Vector3 pillow, Vector3 rail)
        {
            pickup = owner;
            pivot = PawnshopFindCatalog.FindAnchor(owner.Model,"MirrorPivot");
            pane = PawnshopFindCatalog.FindAnchor(owner.Model,"GlassSurface").GetComponent<Renderer>();
            PawnshopFindCatalog.FindAnchor(owner.Model,"BezelSurface").SetParent(pivot,true);
            pane.transform.SetParent(pivot,true);
            // The imported empty keeps Blender's basis. Swing its actual glass
            // face about the trunnions, keeping the upper edge horizontal.
            Vector3 faceOffset = pane.bounds.center-pivot.position;
            Vector3 authoredNormal = faceOffset.normalized;
            Vector3 point = pivot.position;
            for (int i = 0; i < 3; i++)
            {
                normal = ((pillow-point).normalized + (rail-point).normalized).normalized;
                point = pivot.position + normal*faceOffset.magnitude;
            }
            pivot.rotation = Quaternion.LookRotation(normal,Vector3.up) *
                Quaternion.Inverse(Quaternion.LookRotation(authoredNormal,Vector3.up)) * pivot.rotation;
            glassPoint = pane.bounds.center + normal*.003f;
            direction = (rail-glassPoint).normalized;
            original = pane.sharedMaterial;
        }

        private void LateUpdate()
        {
            if (!pickup || pickup.IsPresenting)
            {
                if (pane && original && pane.sharedMaterial != original) pane.sharedMaterial = original;
                return;
            }
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .30f;
            if (!viewer) viewer = Camera.main;
            if (!viewer || (viewer.transform.position-glassPoint).sqrMagnitude > 36f) return;
            Vector3 screen = viewer.WorldToViewportPoint(glassPoint);
            if (screen.z <= 0f || screen.x < -.12f || screen.x > 1.12f || screen.y < -.12f || screen.y > 1.12f) return;
            if (!capture)
            {
                var cameraObject = new GameObject("View from the clamped mirror");
                cameraObject.transform.SetParent(transform,false);
                capture = cameraObject.AddComponent<Camera>();
                capture.enabled = false;
                capture.cameraType = CameraType.Reflection;
                capture.allowHDR = false;
                capture.allowMSAA = false;
                capture.fieldOfView = ViewAngle;
                capture.aspect = 1f;
                capture.nearClipPlane = .035f;
                capture.farClipPlane = 48f;
                var settings = capture.GetUniversalAdditionalCameraData();
                settings.renderPostProcessing = false;
                settings.renderShadows = false;
                settings.requiresColorOption = CameraOverrideOption.Off;
                settings.requiresDepthOption = CameraOverrideOption.Off;
                image = new RenderTexture(192,192,16,RenderTextureFormat.ARGB32)
                    { name = "Ravine window reflection", wrapMode = TextureWrapMode.Clamp };
                image.Create();
                request.destination = image;
            }
            capture.transform.SetPositionAndRotation(glassPoint+direction*.025f,Quaternion.LookRotation(direction,Vector3.up));
            capture.cullingMask = viewer.cullingMask & ~(1 << 5);
            capture.clearFlags = CameraClearFlags.SolidColor;
            capture.backgroundColor = RenderSettings.fogColor;
            RenderPipeline.SubmitRenderRequest(capture,request);
            if (pane.sharedMaterial == original)
            {
                pane.sharedMaterial = Resources.Load<Material>("Pawnshop/Finds/RavineMirrorGlass");
                var block = new MaterialPropertyBlock();
                pane.GetPropertyBlock(block);
                block.SetTexture("_BridgeView",image);
                float focalLength = 1f/Mathf.Tan(ViewAngle*.5f*Mathf.Deg2Rad);
                block.SetVector("_ViewRight",capture.transform.right*focalLength);
                block.SetVector("_ViewUp",capture.transform.up*focalLength);
                block.SetVector("_ViewForward",direction);
                block.SetVector("_PaneNormal",normal);
                pane.SetPropertyBlock(block);
            }
        }

        private void OnDisable()
        {
            if (pane && original) pane.sharedMaterial = original;
            if (capture) Destroy(capture.gameObject);
            if (image) { image.Release(); Destroy(image); }
            capture = null;
            image = null;
            request.destination = null;
        }
    }
}
