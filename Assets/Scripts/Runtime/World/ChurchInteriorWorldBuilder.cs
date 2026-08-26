using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class ChurchInteriorWorldBuilder
    {
        public const string RootName = "Church Interior World";
        public const string ModelName = "Church Interior 3D";
        public const string CollisionRootName =
            "Church Gameplay Collision";

        public static ChurchInteriorWorldResult Build(
            Transform parent,
            ChurchInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ChurchInteriorLayoutValidator.ValidateOrThrow(plan);
            if (!string.Equals(
                    plan.ModelResourcePath,
                    ChurchResources.InteriorPrefabResourcePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The church layout points at a prefab outside the " +
                    "typed interior asset contract.");
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            ChurchAssetRegistry registry;
            try
            {
                registry = ChurchResources.Instantiate(
                    ChurchAssetKind.Interior,
                    root);
                registry.gameObject.name = ModelName;
                registry.transform.localPosition = Vector3.zero;
                registry.transform.localRotation = Quaternion.identity;
                registry.transform.localScale = Vector3.one;
                ValidateModelContract(plan, registry);
            }
            catch
            {
                Object.Destroy(root.gameObject);
                throw;
            }

            GameObject modelObject = registry.gameObject;
            DisableAuthoredColliders(modelObject.transform);

            Transform collisionRoot = new GameObject(
                CollisionRootName).transform;
            collisionRoot.SetParent(root, false);
            var colliders = new List<Collider>();
            BuildRoomCollision(collisionRoot, plan, colliders);
            BuildFixtureCollision(collisionRoot, plan, colliders);
            return new ChurchInteriorWorldResult(
                root,
                modelObject.transform,
                registry,
                collisionRoot,
                colliders);
        }

        private static void ValidateModelContract(
            ChurchInteriorLayoutPlan plan,
            ChurchAssetRegistry registry)
        {
            const float tolerance = 0.01f;
            ChurchDimensions dimensions = registry.Dimensions;
            if (Mathf.Abs(dimensions.Width - plan.RoomSize.x) > tolerance ||
                Mathf.Abs(dimensions.Length - plan.RoomSize.y) > tolerance ||
                !BoundsMatch(
                    registry.LocalBounds,
                    plan.ModelLocalBounds,
                    tolerance) ||
                registry.SpawnAnchor == null ||
                registry.ExitAnchor == null)
            {
                throw new InvalidOperationException(
                    "The church interior prefab dimensions, source " +
                    "bounds or required anchors differ from the layout " +
                    "plan.");
            }

            Vector3 spawn = registry.transform.InverseTransformPoint(
                registry.SpawnAnchor.position);
            Vector3 exit = registry.transform.InverseTransformPoint(
                registry.ExitAnchor.position);
            if (Mathf.Abs(spawn.x - plan.PlayerSpawn.x) > tolerance ||
                Mathf.Abs(spawn.z - plan.PlayerSpawn.z) > tolerance ||
                Mathf.Abs(exit.x - plan.ExitPosition.x) > tolerance ||
                Mathf.Abs(exit.z - plan.ExitPosition.z) > tolerance)
            {
                throw new InvalidOperationException(
                    "The church interior prefab spawn or exit anchor " +
                    "differs from the layout plan.");
            }
        }

        private static bool BoundsMatch(
            Bounds actual,
            Bounds expected,
            float tolerance)
        {
            Vector3 actualMin = actual.min;
            Vector3 expectedMin = expected.min;
            Vector3 actualMax = actual.max;
            Vector3 expectedMax = expected.max;
            return Mathf.Abs(actualMin.x - expectedMin.x) <= tolerance &&
                   Mathf.Abs(actualMin.y - expectedMin.y) <= tolerance &&
                   Mathf.Abs(actualMin.z - expectedMin.z) <= tolerance &&
                   Mathf.Abs(actualMax.x - expectedMax.x) <= tolerance &&
                   Mathf.Abs(actualMax.y - expectedMax.y) <= tolerance &&
                   Mathf.Abs(actualMax.z - expectedMax.z) <= tolerance;
        }

        private static void BuildRoomCollision(
            Transform parent,
            ChurchInteriorLayoutPlan plan,
            ICollection<Collider> colliders)
        {
            Rect room = plan.RoomBounds;
            float thickness = plan.WallThickness;
            float wallHeight = plan.RoomHeight;
            AddCollider(
                parent,
                "Floor",
                new Vector3(room.center.x, -0.125f, room.center.y),
                new Vector3(room.width, 0.25f, room.height),
                colliders);

            AddCollider(
                parent,
                "North Side Wall",
                new Vector3(
                    room.xMin + thickness * 0.5f,
                    wallHeight * 0.5f,
                    room.center.y),
                new Vector3(
                    thickness,
                    wallHeight,
                    room.height),
                colliders);
            AddCollider(
                parent,
                "South Side Wall",
                new Vector3(
                    room.xMax - thickness * 0.5f,
                    wallHeight * 0.5f,
                    room.center.y),
                new Vector3(
                    thickness,
                    wallHeight,
                    room.height),
                colliders);
            AddCollider(
                parent,
                "Altar Rear Wall",
                new Vector3(
                    room.center.x,
                    wallHeight * 0.5f,
                    room.yMax - thickness * 0.5f),
                new Vector3(
                    room.width,
                    wallHeight,
                    thickness),
                colliders);

            float sideWidth =
                (room.width - plan.ExitTriggerSize.x) * 0.5f;
            float leftCenter = room.xMin + sideWidth * 0.5f;
            float rightCenter = room.xMax - sideWidth * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                AddCollider(
                    parent,
                    side == 0
                        ? "Entrance Wall North"
                        : "Entrance Wall South",
                    new Vector3(
                        side == 0 ? leftCenter : rightCenter,
                        wallHeight * 0.5f,
                        room.yMin + thickness * 0.5f),
                    new Vector3(
                        sideWidth,
                        wallHeight,
                        thickness),
                    colliders);
            }
        }

        private static void BuildFixtureCollision(
            Transform parent,
            ChurchInteriorLayoutPlan plan,
            ICollection<Collider> colliders)
        {
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                ChurchInteriorFixturePlan fixture = plan.Fixtures[index];
                if (!fixture.BlocksMovement)
                {
                    continue;
                }

                AddCollider(
                    parent,
                    $"Fixture {fixture.Id}",
                    fixture.Center,
                    fixture.Size,
                    colliders);
            }
        }

        private static void AddCollider(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            ICollection<Collider> colliders)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            BoxCollider collider = holder.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            colliders.Add(collider);
        }

        private static void DisableAuthoredColliders(Transform model)
        {
            Collider[] authored =
                model.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < authored.Length; index++)
            {
                authored[index].enabled = false;
            }
        }
    }
}
