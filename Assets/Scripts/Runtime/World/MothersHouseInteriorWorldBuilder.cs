using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class MothersHouseInteriorWorldBuilder
    {
        public const string RootName = "Mother's House Interior World";
        public const string ModelName = "Mother's House Interior 3D";
        public const string CollisionRootName =
            "Mother's House Gameplay Collision";

        private const float AnchorTolerance = 0.02f;
        private const float AnchorRotationToleranceDegrees = 0.1f;

        public static MothersHouseInteriorWorldResult Build(
            Transform parent,
            MothersHouseInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            MothersHouseInteriorLayoutValidator.ValidateOrThrow(plan);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            try
            {
                GameObject prefab = Resources.Load<GameObject>(
                    plan.ModelResourcePath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "The mother's house interior prefab could not be " +
                        $"loaded from Resources/{plan.ModelResourcePath}.");
                }

                GameObject model = Object.Instantiate(prefab, root, false);
                model.name = ModelName;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // Do not normalize localScale. The imported FBX keeps its
                // metre conversion on its authored root.

                MothersHouseInteriorAssetRegistry registry =
                    model.GetComponent<MothersHouseInteriorAssetRegistry>();
                if (registry == null)
                {
                    throw new InvalidOperationException(
                        "The mother's house prefab is missing its typed " +
                        "asset registry.");
                }

                AnchorSet anchors = ValidateModelContract(
                    root,
                    plan,
                    registry);
                DisableAuthoredColliders(model.transform);

                Transform collisionRoot = new GameObject(
                    CollisionRootName).transform;
                collisionRoot.SetParent(root, false);
                var colliders = new List<Collider>();
                BuildRoomCollision(collisionRoot, plan, colliders);
                BuildFixtureCollision(collisionRoot, plan, colliders);
                return new MothersHouseInteriorWorldResult(
                    root,
                    model.transform,
                    registry,
                    collisionRoot,
                    colliders,
                    anchors.Entry,
                    anchors.Spawn,
                    anchors.Exit,
                    anchors.Camera,
                    anchors.CameraTarget,
                    anchors.Fireplace,
                    anchors.FireLight,
                    anchors.FloorLampLight,
                    anchors.Tabletop,
                    anchors.TeapotDock);
            }
            catch
            {
                DestroyBuiltObject(root.gameObject);
                throw;
            }
        }

        private static AnchorSet ValidateModelContract(
            Transform worldRoot,
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorAssetRegistry registry)
        {
            MothersHouseInteriorDimensions dimensions =
                registry.Dimensions;
            if (registry.ModelRoot == null ||
                !Approximately(dimensions.Width, plan.RoomSize.x) ||
                !Approximately(dimensions.Depth, plan.RoomSize.y) ||
                !Approximately(dimensions.Height, plan.RoomHeight) ||
                !Approximately(
                    dimensions.WallThickness,
                    plan.WallThickness) ||
                !Approximately(
                    dimensions.DoorWidth,
                    plan.DoorOpeningWidth) ||
                registry.SourceTriangleCount <= 0 ||
                string.IsNullOrWhiteSpace(registry.DesignId) ||
                string.IsNullOrWhiteSpace(registry.BuildSignature))
            {
                throw new InvalidOperationException(
                    "The mother's house prefab metadata differs from its " +
                    "pure room contract.");
            }

            if (!BoundsMatch(
                    registry.LocalBounds,
                    plan.ModelLocalBounds,
                    AnchorTolerance))
            {
                throw new InvalidOperationException(
                    "The mother's house prefab renderer bounds differ " +
                    "from its pure layout contract.");
            }

            var anchors = new AnchorSet(
                RequireAnchor(registry, "entry"),
                RequireAnchor(registry, "spawn"),
                RequireAnchor(registry, "exit"),
                RequireAnchor(registry, "camera"),
                RequireAnchor(registry, "camera_target"),
                RequireAnchor(registry, "fireplace"),
                RequireAnchor(registry, "fire_light"),
                RequireAnchor(registry, "floor_lamp_light"),
                RequireAnchor(registry, "tabletop"),
                RequireAnchor(registry, "teapot_dock"));

            ValidateAnchor(
                worldRoot,
                anchors.Entry,
                plan.EntryPosition,
                "entry");
            ValidateAnchor(
                worldRoot,
                anchors.Spawn,
                new Vector3(
                    plan.PlayerSpawn.x,
                    0f,
                    plan.PlayerSpawn.z),
                "spawn");
            ValidateAnchor(
                worldRoot,
                anchors.Exit,
                new Vector3(
                    plan.ExitPosition.x,
                    0f,
                    plan.ExitPosition.z),
                "exit");
            ValidateAnchor(
                worldRoot,
                anchors.Camera,
                plan.CameraShot.Position,
                "camera");
            ValidateAnchor(
                worldRoot,
                anchors.CameraTarget,
                plan.CameraTarget,
                "camera_target");
            ValidateAnchor(
                worldRoot,
                anchors.Fireplace,
                plan.FireplacePosition,
                "fireplace");
            ValidateAnchor(
                worldRoot,
                anchors.FireLight,
                plan.FireLightPosition,
                "fire_light");
            ValidateAnchor(
                worldRoot,
                anchors.FloorLampLight,
                plan.FloorLampLightPosition,
                "floor_lamp_light");
            ValidateAnchor(
                worldRoot,
                anchors.Tabletop,
                plan.TabletopPosition,
                "tabletop");
            ValidateAnchor(
                worldRoot,
                anchors.TeapotDock,
                plan.TeapotDockPosition,
                "teapot_dock");
            ValidateIdentityRotation(
                registry.ModelRoot,
                anchors.TeapotDock,
                "teapot_dock");
            return anchors;
        }

        private static Transform RequireAnchor(
            MothersHouseInteriorAssetRegistry registry,
            string role)
        {
            if (!registry.TryGetAnchor(role, out Transform anchor) ||
                anchor == null)
            {
                throw new InvalidOperationException(
                    $"The mother's house prefab is missing anchor " +
                    $"role '{role}'.");
            }

            return anchor;
        }

        private static void ValidateAnchor(
            Transform worldRoot,
            Transform anchor,
            Vector3 expected,
            string role)
        {
            // World space crosses the imported FBX scale correctly; reading
            // anchor.localPosition would shrink every metre to a hundredth.
            Vector3 actual = worldRoot.InverseTransformPoint(
                anchor.position);
            if (Vector3.Distance(actual, expected) > AnchorTolerance)
            {
                throw new InvalidOperationException(
                    $"The mother's house anchor '{role}' is at {actual}, " +
                    $"expected {expected}.");
            }
        }

        private static void ValidateIdentityRotation(
            Transform worldRoot,
            Transform anchor,
            string role)
        {
            Quaternion relative = Quaternion.Inverse(worldRoot.rotation) *
                                  anchor.rotation;
            float angle = Quaternion.Angle(
                relative,
                Quaternion.identity);
            if (angle > AnchorRotationToleranceDegrees)
            {
                throw new InvalidOperationException(
                    $"The mother's house anchor '{role}' is rotated " +
                    $"{angle:0.###} degrees relative to the room; the " +
                    "table kettle dock must remain upright and identity.");
            }
        }

        private static void BuildRoomCollision(
            Transform parent,
            MothersHouseInteriorLayoutPlan plan,
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
                "West Wall",
                new Vector3(
                    room.xMin + thickness * 0.5f,
                    wallHeight * 0.5f,
                    room.center.y),
                new Vector3(thickness, wallHeight, room.height),
                colliders);
            AddCollider(
                parent,
                "East Wall",
                new Vector3(
                    room.xMax - thickness * 0.5f,
                    wallHeight * 0.5f,
                    room.center.y),
                new Vector3(thickness, wallHeight, room.height),
                colliders);
            AddCollider(
                parent,
                "North Wall",
                new Vector3(
                    room.center.x,
                    wallHeight * 0.5f,
                    room.yMax - thickness * 0.5f),
                new Vector3(room.width, wallHeight, thickness),
                colliders);

            float doorMin = plan.EntryPosition.x -
                plan.DoorOpeningWidth * 0.5f;
            float doorMax = plan.EntryPosition.x +
                plan.DoorOpeningWidth * 0.5f;
            AddSouthWallSegment(
                parent,
                "South Wall West of Door",
                room,
                thickness,
                wallHeight,
                room.xMin,
                doorMin,
                colliders);
            AddSouthWallSegment(
                parent,
                "South Wall East of Door",
                room,
                thickness,
                wallHeight,
                doorMax,
                room.xMax,
                colliders);
        }

        private static void AddSouthWallSegment(
            Transform parent,
            string name,
            Rect room,
            float thickness,
            float wallHeight,
            float minimumX,
            float maximumX,
            ICollection<Collider> colliders)
        {
            float length = maximumX - minimumX;
            if (length <= 0f)
            {
                return;
            }

            AddCollider(
                parent,
                name,
                new Vector3(
                    (minimumX + maximumX) * 0.5f,
                    wallHeight * 0.5f,
                    room.yMin + thickness * 0.5f),
                new Vector3(length, wallHeight, thickness),
                colliders);
        }

        private static void BuildFixtureCollision(
            Transform parent,
            MothersHouseInteriorLayoutPlan plan,
            ICollection<Collider> colliders)
        {
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                MothersHouseInteriorFixturePlan fixture =
                    plan.Fixtures[index];
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

        private static bool BoundsMatch(
            Bounds actual,
            Bounds expected,
            float tolerance)
        {
            return Vector3.Distance(actual.min, expected.min) <= tolerance &&
                   Vector3.Distance(actual.max, expected.max) <= tolerance;
        }

        private static bool Approximately(float actual, float expected)
        {
            return Mathf.Abs(actual - expected) <= AnchorTolerance;
        }

        private static void DestroyBuiltObject(GameObject value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }

        private readonly struct AnchorSet
        {
            public AnchorSet(
                Transform entry,
                Transform spawn,
                Transform exit,
                Transform camera,
                Transform cameraTarget,
                Transform fireplace,
                Transform fireLight,
                Transform floorLampLight,
                Transform tabletop,
                Transform teapotDock)
            {
                Entry = entry;
                Spawn = spawn;
                Exit = exit;
                Camera = camera;
                CameraTarget = cameraTarget;
                Fireplace = fireplace;
                FireLight = fireLight;
                FloorLampLight = floorLampLight;
                Tabletop = tabletop;
                TeapotDock = teapotDock;
            }

            public Transform Entry { get; }
            public Transform Spawn { get; }
            public Transform Exit { get; }
            public Transform Camera { get; }
            public Transform CameraTarget { get; }
            public Transform Fireplace { get; }
            public Transform FireLight { get; }
            public Transform FloorLampLight { get; }
            public Transform Tabletop { get; }
            public Transform TeapotDock { get; }
        }
    }
}
