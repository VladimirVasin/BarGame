using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The authored house has complete pierced side walls. Its fixed cameras
    /// sit beyond a side wall, so only that near wall's upper renderers and
    /// windows are hidden for that camera. Collision and camera poses remain
    /// plan-owned; no geometry is removed from the interior asset.
    /// </summary>
    public sealed class MothersHouseWindowCutaway : MonoBehaviour
    {
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly List<Binding> bathroomFront = new List<Binding>();
        private Transform coordinateRoot;

        public void Configure(MothersHouseInteriorAssetRegistry registry, Transform root)
        {
            Restore();
            bindings.Clear();
            bathroomFront.Clear();
            coordinateRoot = root;
            foreach (MothersHouseInteriorPartBinding part in registry.Parts)
            {
                if (part?.Renderer == null)
                {
                    continue;
                }

                string name = part.SourceName;
                if (name == "FIX_BathroomSouthWall" || name == "FIX_BathroomDoor.Frame")
                    bathroomFront.Add(new Binding(part.Renderer, false));
                bool east = name == "FIX_Wall.EastUpper" ||
                            name == "FIX_UpperWall.EastUpper";
                bool west = name == "FIX_Wall.WestUpper" ||
                            name == "FIX_UpperWall.WestUpper";
                foreach (MothersHouseWindowDescriptor opening in
                         MothersHouseInteriorLayoutPlanner.Windows)
                {
                    if (name != opening.FramePartName && name != opening.GlassPartName)
                    {
                        continue;
                    }
                    east |= opening.Wall == MothersHouseWindowWall.East;
                    west |= opening.Wall == MothersHouseWindowWall.West;
                }
                if (east || west)
                {
                    bindings.Add(new Binding(part.Renderer, east));
                }
            }
        }

        public void RefreshForCamera(Camera camera)
        {
            if (coordinateRoot == null || camera == null)
            {
                return;
            }
            float x = coordinateRoot.InverseTransformPoint(camera.transform.position).x;
            Vector3 localCamera = coordinateRoot.InverseTransformPoint(camera.transform.position);
            // Only the bathroom camera looks through this partition. The
            // corridor camera is above the south stair landing and must see
            // the complete wall and doorway at the far end of the corridor.
            bool bathroomShot = camera.cameraType == CameraType.Game &&
                Vector3.Distance(localCamera, MothersHouseInteriorLayoutPlanner.BathroomCameraPosition) < 0.05f;
            foreach (Binding binding in bathroomFront)
                if (binding.Renderer != null)
                    binding.Renderer.enabled = binding.InitiallyEnabled && !bathroomShot;
            bool hideEast = camera.cameraType == CameraType.Game &&
                            x > MothersHouseInteriorLayoutPlanner.RoomWidth * 0.5f;
            bool hideWest = camera.cameraType == CameraType.Game &&
                            x < -MothersHouseInteriorLayoutPlanner.RoomWidth * 0.5f;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer != null)
                {
                    binding.Renderer.enabled = binding.InitiallyEnabled &&
                        !(binding.East ? hideEast : hideWest);
                }
            }
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Restore();
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera) =>
            RefreshForCamera(camera);

        private void EndCamera(ScriptableRenderContext context, Camera camera) => Restore();

        private void Restore()
        {
            foreach (Binding binding in bathroomFront)
                if (binding.Renderer != null)
                    binding.Renderer.enabled = binding.InitiallyEnabled;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer != null)
                {
                    binding.Renderer.enabled = binding.InitiallyEnabled;
                }
            }
        }

        private readonly struct Binding
        {
            public Binding(Renderer renderer, bool east)
            {
                Renderer = renderer;
                East = east;
                InitiallyEnabled = renderer.enabled;
            }
            public Renderer Renderer { get; }
            public bool East { get; }
            public bool InitiallyEnabled { get; }
        }
    }
}
