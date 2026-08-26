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
        public const float EntranceTriggerRadius = 1.1f;
        public const float EntranceAnchorTolerance = 0.001f;

        private static readonly Color FoundationStone =
            new Color(0.28f, 0.29f, 0.27f);
        private static readonly Color ApproachStone =
            new Color(0.38f, 0.37f, 0.34f);

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
            model.transform.localScale = Vector3.one;
            ValidateExteriorEntranceAnchor(registry, plan);
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
