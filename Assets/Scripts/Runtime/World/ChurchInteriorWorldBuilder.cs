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
            // Every walkable box under this root is the nave's stone floor.
            FootstepGround.Stamp(collisionRoot, FootstepGroundKind.Stone);
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

            Bounds measured = default;
            bool firstRenderer = true;
            foreach (Renderer renderer in registry.Renderers)
            {
                if (renderer == null) continue;
                Bounds world = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = registry.transform.InverseTransformPoint(world.center +
                        Vector3.Scale(world.extents, new Vector3(x, y, z)));
                    if (firstRenderer) { measured = new Bounds(corner, Vector3.zero); firstRenderer = false; }
                    else measured.Encapsulate(corner);
                }
            }
            if (firstRenderer || !BoundsMatch(measured, plan.ModelLocalBounds, .03f))
                throw new InvalidOperationException("The placed church renderers differ from the measured metre envelope.");

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
            float t = plan.WallThickness;
            float h = plan.RoomHeight;
            AddCollider(parent, "Floor", new Vector3(0f, -.125f, 0f),
                new Vector3(room.width, .25f, room.height), colliders);
            // The narthex shoulders and aisle walls follow the real shell.
            float join = ChurchInteriorLayoutPlanner.NarthexEndZ;
            float narrow = ChurchInteriorLayoutPlanner.NarthexHalfWidth;
            float wide = ChurchInteriorLayoutPlanner.AisleHalfWidth;
            for (int side = -1; side <= 1; side += 2)
            {
                AddCollider(parent, $"Narthex Wall {side}",
                    new Vector3(side * (narrow + t * .5f), h * .5f, (room.yMin + join) * .5f),
                    new Vector3(t, h, join - room.yMin), colliders);
                AddCollider(parent, $"Aisle Wall {side}",
                    new Vector3(side * (wide + t * .5f), h * .5f, (join + room.yMax) * .5f),
                    new Vector3(t, h, room.yMax - join), colliders);
                AddCollider(parent, $"Narthex Shoulder {side}",
                    new Vector3(side * (narrow + wide) * .5f, h * .5f, join - t * .5f),
                    new Vector3(wide - narrow, h, t), colliders);
            }
            AddCollider(parent, "Altar Rear Wall", new Vector3(0f, h * .5f, room.yMax - t * .5f),
                new Vector3(wide * 2f, h, t), colliders);
            float opening = ChurchInteriorLayoutPlanner.DoorOpeningWidth;
            float sideWidth = narrow - opening * .5f;
            for (int side = -1; side <= 1; side += 2)
                AddCollider(parent, $"Entrance Wall {side}",
                    new Vector3(side * (opening * .5f + sideWidth * .5f), h * .5f, room.yMin + t * .5f),
                    new Vector3(sideWidth, h, t), colliders);
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
