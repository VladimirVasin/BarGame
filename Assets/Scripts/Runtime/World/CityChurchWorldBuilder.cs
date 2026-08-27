using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Materialises the church exterior plan. The prefab is visual-only:
    /// every imported collider is disabled and one plan-derived collider,
    /// approach and interactive entrance own gameplay geometry.
    /// </summary>
    public static class CityChurchWorldBuilder
    {
        public const string RootName = "Church";
        public const string ExteriorModelResourcePath =
            ChurchResources.ExteriorPrefabResourcePath;

        // The supermarket's radius: the City's ordinary door reach.
        public const float EntranceTriggerRadius = 1.05f;
        public const float EntranceAnchorTolerance = 0.001f;

        /// <summary>
        /// The authored west portal the placer hides. It is a four-metre
        /// slab of wood standing behind the facade's own stone plinth,
        /// which is neither a door the player recognises nor one he can
        /// see the bottom of; the City draws its ordinary door instead.
        /// </summary>
        public const string AuthoredWestDoorPartName = "EXT_WestDoors";

        public const float DoorLeafWidth = 1.8f;
        public const float DoorLeafHeight = 2.6f;

        private static readonly Color FoundationStone =
            new Color(0.28f, 0.29f, 0.27f);
        private static readonly Color ApproachStone =
            new Color(0.38f, 0.37f, 0.34f);
        // The model's own Wood, Stone and Iron slots, so the door the
        // City draws belongs to the facade it is set into.
        private static readonly Color DoorTimber =
            new Color(0.35f, 0.19f, 0.10f);
        private static readonly Color DoorTrimStone =
            new Color(0.50f, 0.52f, 0.50f);
        private static readonly Color DoorIron =
            new Color(0.13f, 0.14f, 0.13f);
        private static readonly Color DoorLampGlow =
            new Color(1.05f, 0.72f, 0.34f);

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

            GameObject foundation = RuntimePrimitiveFactory.CreateBox(
                "Church Foundation",
                root,
                plan.FoundationBounds.center,
                plan.FoundationBounds.size,
                FoundationStone,
                false);
            Renderer foundationRenderer = foundation.GetComponent<Renderer>();
            if (foundationRenderer != null)
            {
                CityCemeterySurfaceAppearance.ApplyCombined(
                    foundationRenderer,
                    CityCemeterySurfaceKind.Stone,
                    FoundationStone);
            }

            Rect approach = plan.ApproachBounds;
            GameObject approachSurface = RuntimePrimitiveFactory.CreateBox(
                "Church Entrance Approach",
                root,
                new Vector3(
                    approach.center.x,
                    plan.GroundTopY +
                    CityChurchPlanner.ApproachSurfaceTopAboveGround -
                    CityChurchPlanner.ApproachSurfaceHeight * 0.5f,
                    approach.center.y),
                new Vector3(
                    approach.width,
                    CityChurchPlanner.ApproachSurfaceHeight,
                    approach.height),
                ApproachStone,
                false);
            Renderer approachRenderer =
                approachSurface.GetComponent<Renderer>();
            if (approachRenderer != null)
            {
                CityCemeterySurfaceAppearance.ApplyCombined(
                    approachRenderer,
                    CityCemeterySurfaceKind.Stone,
                    ApproachStone);
            }

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
            HideAuthoredWestDoor(registry);
            Collider[] importedColliders =
                model.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < importedColliders.Length; index++)
            {
                importedColliders[index].enabled = false;
            }

            BuildEntranceDoor(root, plan);

            GameObject collision = new GameObject("Church Collision");
            collision.transform.SetParent(root, false);
            collision.transform.position =
                plan.BuildingColliderBounds.center;
            BoxCollider buildingCollider =
                collision.AddComponent<BoxCollider>();
            buildingCollider.size = plan.BuildingColliderBounds.size;

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

        private static void HideAuthoredWestDoor(
            ChurchAssetRegistry registry)
        {
            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                ChurchRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    !string.Equals(
                        binding.SourceName,
                        AuthoredWestDoorPartName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                binding.Renderer.enabled = false;
                return;
            }

            throw new InvalidOperationException(
                "The church exterior prefab no longer publishes its " +
                $"'{AuthoredWestDoorPartName}' part, so the City cannot " +
                "hide it before drawing the door the player uses.");
        }

        /// <summary>
        /// The City's ordinary door, at the same metres as the bar, the
        /// supermarket and the player's own front door: a leaf a person
        /// could pull, a stone surround, a lintel and a handle. It is
        /// drawn proud of the facade's stone plinth, which is what
        /// swallowed the bottom of the authored portal.
        /// </summary>
        private static void BuildEntranceDoor(
            Transform root,
            CityChurchPlan plan)
        {
            Vector3 outward = plan.EntranceOutwardDirection;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            bool frontageIsX = Mathf.Abs(outward.x) > 0.5f;
            Vector3 door = plan.DoorGroundPosition;

            RuntimePrimitiveFactory.CreateBox(
                "Church Door",
                root,
                door +
                (outward * 0.16f) +
                (Vector3.up * (DoorLeafHeight * 0.5f)),
                FacadeSize(frontageIsX, 0.14f, DoorLeafHeight, DoorLeafWidth),
                DoorTimber,
                false);

            // The mullion between the two leaves, so it reads as the
            // pair of doors a church has rather than one flat panel.
            RuntimePrimitiveFactory.CreateBox(
                "Church Door Mullion",
                root,
                door +
                (outward * 0.24f) +
                (Vector3.up * (DoorLeafHeight * 0.5f)),
                FacadeSize(frontageIsX, 0.06f, DoorLeafHeight - 0.10f, 0.09f),
                DoorIron,
                false);

            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Church Door Jamb",
                    root,
                    door +
                    (outward * 0.20f) +
                    (tangent * (side * (DoorLeafWidth * 0.5f + 0.14f))) +
                    (Vector3.up * ((DoorLeafHeight + 0.22f) * 0.5f)),
                    FacadeSize(
                        frontageIsX,
                        0.24f,
                        DoorLeafHeight + 0.22f,
                        0.28f),
                    DoorTrimStone,
                    false);
                RuntimePrimitiveFactory.CreateBox(
                    "Church Door Handle",
                    root,
                    door +
                    (outward * 0.27f) +
                    (tangent * (side * 0.26f)) +
                    (Vector3.up * 1.06f),
                    FacadeSize(frontageIsX, 0.08f, 0.07f, 0.22f),
                    DoorIron,
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Church Door Lintel",
                root,
                door +
                (outward * 0.20f) +
                (Vector3.up * (DoorLeafHeight + 0.11f)),
                FacadeSize(
                    frontageIsX,
                    0.24f,
                    0.22f,
                    DoorLeafWidth + 0.56f),
                DoorTrimStone,
                false);

            // Every other City door is marked after dark - the bar's
            // sign, the grocery's blade, the porch bulb at home. This
            // west front has nothing on it at all, which is most of why
            // the entrance read as a blank panel.
            RuntimePrimitiveFactory.CreateBox(
                "Church Door Lamp Bracket",
                root,
                door +
                (outward * 0.30f) +
                (Vector3.up * (DoorLeafHeight + 0.52f)),
                FacadeSize(frontageIsX, 0.36f, 0.07f, 0.07f),
                DoorIron,
                false);
            GameObject lamp = RuntimePrimitiveFactory.CreateBox(
                "Church Door Lamp",
                root,
                door +
                (outward * 0.44f) +
                (Vector3.up * (DoorLeafHeight + 0.38f)),
                FacadeSize(frontageIsX, 0.20f, 0.24f, 0.20f),
                DoorLampGlow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lamp.GetComponent<Renderer>(),
                DoorLampGlow);

            // A threshold sill, not a step: it is laid flush with the
            // forecourt for the same reason the forecourt is laid flush
            // with the ground - anything the hero cannot stand on must
            // never be between him and the height his dock is measured
            // at.
            RuntimePrimitiveFactory.CreateBox(
                "Church Door Threshold",
                root,
                door +
                (outward * 0.46f) +
                (Vector3.up *
                 (CityChurchPlanner.ApproachSurfaceTopAboveGround -
                  CityChurchPlanner.ApproachSurfaceHeight * 0.5f)),
                FacadeSize(
                    frontageIsX,
                    0.92f,
                    CityChurchPlanner.ApproachSurfaceHeight,
                    DoorLeafWidth + 0.9f),
                DoorTrimStone,
                false);
        }

        private static Vector3 FacadeSize(
            bool frontageIsX,
            float depth,
            float height,
            float width)
        {
            return frontageIsX
                ? new Vector3(depth, height, width)
                : new Vector3(width, height, depth);
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
