using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Materialises the church exterior plan. The prefab is visual-only:
    /// every imported collider is disabled and plan-derived collision
    /// plus the interactive entrance own gameplay geometry. The model owns
    /// the foundation, threshold and working door presentation. The separate
    /// courtyard builder owns the Blender-authored approach surface.
    /// </summary>
    public static class CityChurchWorldBuilder
    {
        public const string RootName = "Church";
        public const string ExteriorModelResourcePath =
            ChurchResources.ExteriorPrefabResourcePath;

        // The supermarket's radius: the City's ordinary door reach.
        public const float EntranceTriggerRadius = 1.05f;
        public const float EntranceAnchorTolerance = 0.001f;

        public const float DoorLeafWidth = 1.8f;
        public const float DoorLeafHeight = 2.6f;
        public const float DoorThresholdDepth = 0.92f;
        public const float DoorThresholdWidth = DoorLeafWidth + 0.9f;
        public const string ThresholdCollisionName = "Church Threshold Support";

        internal static Rect GetDoorThresholdBounds(Vector3 door)
        {
            return new Rect(door.x - DoorThresholdDepth,
                door.z - DoorThresholdWidth * 0.5f,
                DoorThresholdDepth, DoorThresholdWidth);
        }

        public static ChurchEntrance Build(
            Transform parent,
            CityChurchPlan plan,
            RoadWalkableArea walkableArea)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                return null;
            }

            if (walkableArea == null)
            {
                throw new ArgumentNullException(nameof(walkableArea));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            ChurchAssetRegistry registry;
            try
            {
                registry = ChurchResources.Instantiate(
                    ChurchAssetKind.Exterior,
                    root);
            }
            catch
            {
                Object.Destroy(root.gameObject);
                throw;
            }

            GameObject model = registry.gameObject;
            model.name = "Church Exterior 3D";
            model.transform.SetPositionAndRotation(
                plan.ModelRootPosition,
                plan.ModelRotation);
            model.transform.localScale =
                Vector3.one * CityChurchPlanner.ExteriorModelScale;
            ValidateExteriorEntranceAnchor(registry, plan);
            foreach (ChurchRendererBinding binding in registry.RendererBindings)
            {
                if (binding.SourceName == "EXT_Foundation" &&
                    binding.MaterialSlot == ChurchMaterialSlot.Stone)
                    GroundAuthoredSurfaceAppearance.Apply(binding.Renderer,
                        GroundSurfaceKind.Paving, cityClimate: true);
                if (binding.SourceName == "EXT_DoorLampWarm" && binding.MaterialSlot == ChurchMaterialSlot.GlassWarm)
                    CityNightGlowRegistry.Register(binding.Renderer, new Color(1.05f, .72f, .34f));
            }
            Collider[] importedColliders =
                model.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < importedColliders.Length; index++)
            {
                importedColliders[index].enabled = false;
            }

            GameObject collision = new GameObject("Church Collision");
            collision.transform.SetParent(root, false);
            collision.transform.position =
                plan.BuildingColliderBounds.center;
            BoxCollider buildingCollider =
                collision.AddComponent<BoxCollider>();
            buildingCollider.size = plan.BuildingColliderBounds.size;
            BuildThresholdSupport(root, plan);

            GameObject entranceObject = new GameObject(
                "Interactive Church Entrance");
            entranceObject.transform.SetParent(root, false);
            entranceObject.transform.position = plan.InteractionPosition;
            SphereCollider trigger =
                entranceObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = EntranceTriggerRadius;
            ChurchEntrance entrance =
                entranceObject.AddComponent<ChurchEntrance>();
            entrance.Configure(plan.ReturnPosition);
            PlayerDoorActionTarget doorAction =
                entranceObject.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(plan.DoorAction);

            walkableArea.Add(plan.ApproachBounds);
            return entrance;
        }

        private static void BuildThresholdSupport(Transform root, CityChurchPlan plan)
        {
            // Grass and paving leave this exact footprint to the imported sill.
            // Its FBX stays passive/non-readable; this tiny owned collision mesh
            // follows the same metre datums without retaining its visual meshes.
            Vector3 outward = plan.EntranceOutwardDirection;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            Vector3 outer = plan.DoorGroundPosition + outward * DoorThresholdDepth;
            Vector3 inner = plan.DoorGroundPosition;
            float half = DoorThresholdWidth * .5f;
            float bottom = plan.GroundTopY + CityChurchPlanner.FoundationTopAboveGround -
                CityChurchPlanner.FoundationHeight;
            var vertices = new Vector3[8];
            vertices[0] = outer - tangent * half;
            vertices[1] = outer + tangent * half;
            vertices[2] = inner + tangent * half;
            vertices[3] = inner - tangent * half;
            for (int index = 0; index < 4; index++)
            {
                vertices[index].y = bottom;
                vertices[index + 4] = vertices[index];
                vertices[index + 4].y = plan.GroundTopY + (index < 2
                    ? CityChurchCourtyardWorldBuilder.SurfaceTopAboveGround
                    : CityChurchPlanner.FoundationTopAboveGround);
            }
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = root.InverseTransformPoint(vertices[index]);
            var mesh = new Mesh { name = "Church Threshold Support Mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
            mesh.RecalculateBounds();
            var support = new GameObject(ThresholdCollisionName);
            support.transform.SetParent(root, false);
            support.AddComponent<MeshCollider>().sharedMesh = mesh;
            support.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            FootstepGround.Stamp(support, FootstepGroundKind.Stone);
            mesh.UploadMeshData(false);
        }

        internal static void ValidateExteriorEntranceAnchor(
            ChurchAssetRegistry registry,
            CityChurchPlan plan)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (registry.Kind != ChurchAssetKind.Exterior ||
                registry.EntranceAnchor == null)
            {
                throw new InvalidOperationException(
                    "The church exterior registry has no typed entrance " +
                    "anchor or identifies the wrong asset kind.");
            }

            Vector3 anchorLocalPosition =
                registry.transform.InverseTransformPoint(
                    registry.EntranceAnchor.position);
            Vector2 expectedLocalXZ = new Vector2(
                CityChurchPlanner.ExteriorEntranceAnchorLocalPosition.x,
                CityChurchPlanner.ExteriorEntranceAnchorLocalPosition.z);
            Vector2 actualLocalXZ = new Vector2(
                anchorLocalPosition.x,
                anchorLocalPosition.z);
            Vector2 actualWorldXZ = new Vector2(
                registry.EntranceAnchor.position.x,
                registry.EntranceAnchor.position.z);
            Vector2 doorWorldXZ = new Vector2(
                plan.DoorGroundPosition.x,
                plan.DoorGroundPosition.z);
            if (Vector2.Distance(
                    expectedLocalXZ,
                    actualLocalXZ) > EntranceAnchorTolerance ||
                Vector2.Distance(
                    doorWorldXZ,
                    actualWorldXZ) > EntranceAnchorTolerance)
            {
                throw new InvalidOperationException(
                    "The church exterior entrance anchor does not match " +
                    "the plan-derived central door.");
            }
        }
    }
}
