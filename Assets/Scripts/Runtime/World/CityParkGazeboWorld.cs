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
                CityParkSurfaceKind kind = part.Surface == CityMiscSurfaceKind.Timber
                    ? CityParkSurfaceKind.Timber : part.Surface == CityMiscSurfaceKind.PaintedMetal
                        ? CityParkSurfaceKind.PaintedMetal : CityParkSurfaceKind.Stone;
                Color tint = kind == CityParkSurfaceKind.Timber ? new Color(0.31f, 0.29f, 0.24f)
                    : kind == CityParkSurfaceKind.PaintedMetal ? new Color(0.25f, 0.32f, 0.30f)
                    : new Color(0.42f, 0.43f, 0.39f);
                var placements = new[] { new RuntimeMeshPlacement(part.Mesh, Vector3.zero, Quaternion.identity) };
                GameObject surface = RuntimePrimitiveFactory.CreateCombinedMeshes(
                    part.Component, root, placements,
                    tint, false, CityParkSurfaceAppearance.GetRecipe(kind).MetersPerTile,
                    CityParkSurfaceAppearance.GetUvMode(kind));
                Renderer renderer = surface.GetComponent<Renderer>();
                CityParkSurfaceAppearance.ApplyCombined(renderer, kind, tint);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                MeshCollider collider = surface.AddComponent<MeshCollider>();
                collider.sharedMesh = part.Mesh;
                FootstepGround.Stamp(surface,
                    part.Surface == CityMiscSurfaceKind.Timber
                        ? FootstepGroundKind.Wood : FootstepGroundKind.Stone);
            }
            return world;
        }
    }
}
