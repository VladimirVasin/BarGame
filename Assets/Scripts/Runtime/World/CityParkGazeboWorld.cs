using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Exact passive imported collision for the occupied gazebo. Floor and
    /// steps stay out of the vertical blockers used to route a sitter.
    /// </summary>
    public sealed class CityParkGazeboWorld : MonoBehaviour
    {
        private static Material finish;
        internal static Material Finish => finish != null ? finish :
            finish = Resources.Load<Material>("City/Surfaces/ParkBandstand") ??
                throw new System.InvalidOperationException("Missing park bandstand finish.");

        private readonly List<Collider> rainShelters = new List<Collider>(1);
        public IReadOnlyList<Collider> RainShelters => rainShelters;
        public CityDecorationDescriptor Descriptor { get; private set; }

        internal static CityParkGazeboWorld Build(Transform parent,
            CityDecorationDescriptor descriptor, CityMiscAssetProvider provider)
        {
            Transform root = new GameObject("Park Gazebo Physics").transform;
            root.SetParent(parent, false);
            root.position = descriptor.Position;
            root.rotation = Quaternion.LookRotation(CityParkGazeboGeometry.Forward(descriptor));
            var world = root.gameObject.AddComponent<CityParkGazeboWorld>();
            world.Descriptor = descriptor;
            int count = CityMiscAssetProvider.GetPartCount(CityMiscKind.ParkBandstand);
            for (int index = 0; index < count; index++)
            {
                CityMiscMeshPart part = provider.GetPartOrThrow(CityMiscKind.ParkBandstand, 0, index);
                if (part.Component == "RainShelter_Fixture")
                {
                    GameObject shelter = new GameObject("Gazebo Roof Shelter");
                    shelter.transform.SetParent(root, false);
                    MeshCollider trigger = shelter.AddComponent<MeshCollider>();
                    trigger.sharedMesh = part.Mesh;
                    trigger.convex = true;
                    trigger.isTrigger = true;
                    world.rainShelters.Add(trigger);
                    continue;
                }
                var surface = new GameObject(part.Component);
                surface.transform.SetParent(root, false);
                surface.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = surface.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Finish;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                surface.AddComponent<MeshCollider>().sharedMesh = part.Mesh;
                FootstepGround.Stamp(surface,
                    part.Surface == CityMiscSurfaceKind.Timber
                        ? FootstepGroundKind.Wood : FootstepGroundKind.Stone);
            }
            return world;
        }
    }
}
