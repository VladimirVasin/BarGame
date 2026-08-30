using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The plan-owned physical contract behind the imported cafe model.
    /// Imported meshes stay visual-only: replacing or reimporting them cannot
    /// move the entrance, counter, service furniture or stools that gameplay
    /// reasons about.
    /// </summary>
    public sealed class MountainRoadCafeCollisionWorldResult
    {
        internal MountainRoadCafeCollisionWorldResult(
            GameObject root,
            IList<Collider> colliders,
            IList<CapsuleCollider> stoolColliders,
            Vector3 entranceStart,
            Vector3 entranceEnd)
        {
            Root = root;
            Colliders = new ReadOnlyCollection<Collider>(colliders);
            StoolColliders =
                new ReadOnlyCollection<CapsuleCollider>(stoolColliders);
            EntranceStart = entranceStart;
            EntranceEnd = entranceEnd;
        }

        public GameObject Root { get; }
        public IReadOnlyList<Collider> Colliders { get; }
        public IReadOnlyList<CapsuleCollider> StoolColliders { get; }
        public Vector3 EntranceStart { get; }
        public Vector3 EntranceEnd { get; }
        public int ColliderCount => Colliders.Count;
        public float EntranceClearWidth =>
            Vector3.Distance(EntranceStart, EntranceEnd);
    }

    /// <summary>
    /// Builds the cafe's collider-only gameplay shell from its terminal plan.
    /// The plateau remains the floor, so this builder emits obstacles only.
    /// </summary>
    public static class MountainRoadCafeCollisionWorldBuilder
    {
        public const float RequiredEntranceWidth = 1.60f;
        public const float BoundaryHeight = 4.16f;
        public const float WallThickness = 0.24f;
        public const float GlazedWallThickness = 0.12f;

        public const int PerimeterColliderCount = 6;
        public const int CounterColliderCount = 2;
        public const int ServiceColliderCount = 2;
        public const int StoolColliderCount = 7;
        public const int MainRowStoolCount = 5;
        public const int ExpectedColliderCount =
            PerimeterColliderCount +
            CounterColliderCount +
            ServiceColliderCount +
            StoolColliderCount;

        public const float StoolForward = -2.18f;
        public const float StoolColliderRadius = 0.25f;
        public const float StoolColliderHeight = 0.50f;
        public const float StoolColliderCenterAboveFloor = 0.25f;

        /// <summary>
        /// The established five-seat row. Index one remains the hero's
        /// interactable empty stool; the two additional stools continue the
        /// row around the angled return instead of changing these positions.
        /// </summary>
        public static readonly float[] MainRowStoolRightOffsets =
        {
            -1.50f,
            -0.38f,
            0.75f,
            1.80f,
            3.00f
        };

        /// <summary>
        /// Local right/forward positions that turn the stool rhythm around
        /// the counter bend while remaining inside the chamfered footprint.
        /// </summary>
        public static readonly Vector2[] ReturnStoolLocalPositions =
        {
            new Vector2(4.08f, -0.62f),
            new Vector2(4.16f, 0.10f)
        };

        private const float CounterHeight = 0.90f;
        private const float CounterDepth = 0.82f;
        private const float CounterLength = 6.10f;
        private const float CounterRight = 0.62f;
        private const float CounterForward = -1.15f;
        private const float ReturnHeight = 0.894f;
        private const float ReturnDepth = 0.62f;
        private const float ReturnYawDegrees = -81.634f;
        private const float ReturnStoolYawDegrees = -81.634f;
        private const float ServiceHeight = 0.86f;

        private static readonly Vector2[] CanonicalFootprintLocal =
        {
            new Vector2(-5.32f, -4.56f),
            new Vector2(1.68f, -4.56f),
            new Vector2(4.48f, -1.76f),
            new Vector2(4.48f, 5.44f),
            new Vector2(-5.32f, 5.44f)
        };

        public static MountainRoadCafeCollisionWorldResult Build(
            Transform parent,
            MountainRoadCafePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ValidatePlan(plan);

            var root = new GameObject("Cafe Plan Collision");
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);

            var colliders = new List<Collider>(ExpectedColliderCount);
            var stools = new List<CapsuleCollider>(StoolColliderCount);

            Transform shell = CreateGroup(root.transform, "Five-Sided Shell");
            Vector3[] corners = GetFootprint(plan);
            Vector3 entranceStart;
            Vector3 entranceEnd;
            BuildPerimeter(
                plan,
                shell,
                corners,
                colliders,
                out entranceStart,
                out entranceEnd);

            Transform fixtures = CreateGroup(
                root.transform,
                "Counter And Service Obstacles");
            BuildCounter(plan, fixtures, colliders);
            BuildServiceFixtures(plan, fixtures, colliders);

            Transform stoolRoot = CreateGroup(root.transform, "Seven Stools");
            BuildStools(plan, stoolRoot, colliders, stools);

            if (colliders.Count != ExpectedColliderCount ||
                stools.Count != StoolColliderCount)
            {
                throw new InvalidOperationException(
                    $"Cafe collision recipe produced {colliders.Count} " +
                    $"colliders and {stools.Count} stools; expected " +
                    $"{ExpectedColliderCount} and {StoolColliderCount}.");
            }

            return new MountainRoadCafeCollisionWorldResult(
                root,
                colliders,
                stools,
                entranceStart,
                entranceEnd);
        }

        private static void BuildPerimeter(
            MountainRoadCafePlan plan,
            Transform parent,
            IReadOnlyList<Vector3> corners,
            ICollection<Collider> colliders,
            out Vector3 entranceStart,
            out Vector3 entranceEnd)
        {
            Vector3 entranceCenter = new Vector3(
                plan.DoorCenter.x,
                plan.FloorY,
                plan.DoorCenter.z);
            Vector3 entranceDirection =
                (corners[1] - corners[0]).normalized;
            entranceStart = entranceCenter -
                entranceDirection * (plan.DoorWidth * 0.5f);
            entranceEnd = entranceCenter +
                entranceDirection * (plan.DoorWidth * 0.5f);

            AddBox(
                "boundary-west",
                parent,
                Local(plan, -5.32f, 2.08f, 0.44f),
                FrameRotation(plan),
                new Vector3(
                    WallThickness,
                    BoundaryHeight,
                    10.0f),
                colliders);
            AddBox(
                "boundary-rear",
                parent,
                Local(plan, -0.42f, 2.08f, 5.44f),
                FrameRotation(plan),
                new Vector3(
                    9.80f,
                    BoundaryHeight,
                    WallThickness),
                colliders);
            AddBox(
                "boundary-south-left",
                parent,
                Local(plan, -4.82f, 2.08f, -4.56f),
                FrameRotation(plan),
                new Vector3(
                    1.0f,
                    BoundaryHeight,
                    WallThickness),
                colliders);
            AddBox(
                "boundary-south-right",
                parent,
                Local(plan, -0.52f, 2.08f, -4.56f),
                FrameRotation(plan),
                new Vector3(
                    4.40f,
                    BoundaryHeight,
                    GlazedWallThickness),
                colliders);
            AddBox(
                "boundary-chamfer",
                parent,
                Local(plan, 3.08f, 2.08f, -3.16f),
                LocalYawRotation(plan, -45f),
                new Vector3(
                    3.96f,
                    BoundaryHeight,
                    GlazedWallThickness),
                colliders);
            AddBox(
                "boundary-east",
                parent,
                Local(plan, 4.48f, 2.08f, 1.84f),
                FrameRotation(plan),
                new Vector3(
                    GlazedWallThickness,
                    BoundaryHeight,
                    7.20f),
                colliders);
        }

        private static void BuildCounter(
            MountainRoadCafePlan plan,
            Transform parent,
            ICollection<Collider> colliders)
        {
            AddBox(
                "counter-main",
                parent,
                Local(
                    plan,
                    CounterRight,
                    CounterHeight * 0.5f,
                    CounterForward),
                FrameRotation(plan),
                new Vector3(
                    CounterLength,
                    CounterHeight,
                    CounterDepth),
                colliders);

            AddBox(
                "counter-return",
                parent,
                Local(plan, 3.325f, 0.447f, -0.30f),
                LocalYawRotation(plan, ReturnYawDegrees),
                new Vector3(1.718f, ReturnHeight, ReturnDepth),
                colliders);
        }

        private static void BuildServiceFixtures(
            MountainRoadCafePlan plan,
            Transform parent,
            ICollection<Collider> colliders)
        {
            AddBox(
                "service-cabinet",
                parent,
                Local(plan, 2.15f, 0.43f, 3.90f),
                FrameRotation(plan),
                new Vector3(3.65f, ServiceHeight, 0.78f),
                colliders);
            AddBox(
                "fridge",
                parent,
                Local(plan, -3.82f, 0.98f, 4.72f),
                FrameRotation(plan),
                new Vector3(1.12f, 1.96f, 0.72f),
                colliders);
        }

        private static void BuildStools(
            MountainRoadCafePlan plan,
            Transform parent,
            ICollection<Collider> colliders,
            ICollection<CapsuleCollider> stools)
        {
            for (int index = 0;
                 index < MainRowStoolRightOffsets.Length;
                 index++)
            {
                AddStool(
                    $"stool-{index:00}",
                    parent,
                    Local(
                        plan,
                        MainRowStoolRightOffsets[index],
                        0f,
                        StoolForward),
                    plan,
                    0f,
                    colliders,
                    stools);
            }

            for (int index = 0;
                 index < ReturnStoolLocalPositions.Length;
                 index++)
            {
                Vector2 local = ReturnStoolLocalPositions[index];
                AddStool(
                    $"stool-{index + MainRowStoolCount:00}",
                    parent,
                    Local(plan, local.x, 0f, local.y),
                    plan,
                    ReturnStoolYawDegrees,
                    colliders,
                    stools);
            }
        }

        private static void AddStool(
            string name,
            Transform parent,
            Vector3 floorCenter,
            MountainRoadCafePlan plan,
            float localYawDegrees,
            ICollection<Collider> colliders,
            ICollection<CapsuleCollider> stools)
        {
            GameObject instance = CreateChild(name, parent);
            instance.transform.position = floorCenter;
            instance.transform.rotation =
                LocalYawRotation(plan, localYawDegrees);
            CapsuleCollider collider =
                instance.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center =
                Vector3.up * StoolColliderCenterAboveFloor;
            collider.height = StoolColliderHeight;
            collider.radius = StoolColliderRadius;
            collider.isTrigger = false;
            colliders.Add(collider);
            stools.Add(collider);
        }

        private static void AddBox(
            string name,
            Transform parent,
            Vector3 center,
            Quaternion rotation,
            Vector3 size,
            ICollection<Collider> colliders)
        {
            GameObject instance = CreateChild(name, parent);
            instance.transform.position = center;
            instance.transform.rotation = rotation;
            BoxCollider collider = instance.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = size;
            collider.isTrigger = false;
            colliders.Add(collider);
        }

        private static Transform CreateGroup(
            Transform parent,
            string name)
        {
            return CreateChild(name, parent).transform;
        }

        private static GameObject CreateChild(
            string name,
            Transform parent)
        {
            var child = new GameObject(name);
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Vector3[] GetFootprint(MountainRoadCafePlan plan)
        {
            var result = new Vector3[plan.FootprintXZ.Count];
            for (int index = 0; index < result.Length; index++)
            {
                Vector2 point = plan.FootprintXZ[index];
                result[index] = new Vector3(
                    point.x,
                    plan.FloorY,
                    point.y);
            }

            return result;
        }

        private static Vector3 Local(
            MountainRoadCafePlan plan,
            float right,
            float up,
            float forward)
        {
            return plan.Center +
                   plan.Right * right +
                   Vector3.up * up +
                   plan.Forward * forward;
        }

        private static Quaternion FrameRotation(
            MountainRoadCafePlan plan)
        {
            return Quaternion.LookRotation(plan.Forward, Vector3.up);
        }

        private static Quaternion LocalYawRotation(
            MountainRoadCafePlan plan,
            float yawDegrees)
        {
            return FrameRotation(plan) *
                   Quaternion.Euler(0f, yawDegrees, 0f);
        }

        private static void ValidatePlan(MountainRoadCafePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (string.IsNullOrWhiteSpace(plan.StableId) ||
                plan.FootprintXZ == null ||
                plan.FootprintXZ.Count != 5 ||
                plan.Height < 4f ||
                Mathf.Abs(plan.DoorWidth - RequiredEntranceWidth) > 0.001f)
            {
                throw new ArgumentException(
                    "Cafe collision requires the stable five-sided plan, " +
                    "a four metre shell and the exact 1.6 metre entrance.",
                    nameof(plan));
            }

            Vector3 first = new Vector3(
                plan.FootprintXZ[0].x,
                plan.FloorY,
                plan.FootprintXZ[0].y);
            Vector3 second = new Vector3(
                plan.FootprintXZ[1].x,
                plan.FloorY,
                plan.FootprintXZ[1].y);
            Vector3 edge = second - first;
            float edgeLength = edge.magnitude;
            if (edgeLength <= plan.DoorWidth + 0.20f)
            {
                throw new ArgumentException(
                    "Cafe entrance edge cannot contain its door corridor.",
                    nameof(plan));
            }

            Vector3 toDoor = plan.DoorCenter - first;
            toDoor.y = 0f;
            Vector3 direction = edge / edgeLength;
            float along = Vector3.Dot(toDoor, direction);
            Vector3 perpendicular = toDoor - direction * along;
            float halfDoor = plan.DoorWidth * 0.5f;
            if (perpendicular.magnitude > 0.01f ||
                along - halfDoor <= 0.01f ||
                along + halfDoor >= edgeLength - 0.01f)
            {
                throw new ArgumentException(
                    "Cafe door must lie wholly on the first footprint edge.",
                    nameof(plan));
            }

            if (Mathf.Abs(plan.Center.y - plan.FloorY) > 0.01f)
            {
                throw new ArgumentException(
                    "Cafe collision requires its center on the floor datum.",
                    nameof(plan));
            }

            for (int index = 0;
                 index < CanonicalFootprintLocal.Length;
                 index++)
            {
                Vector2 point = plan.FootprintXZ[index];
                Vector3 offset = new Vector3(
                    point.x,
                    plan.FloorY,
                    point.y) - plan.Center;
                Vector2 actual = new Vector2(
                    Vector3.Dot(offset, plan.Right),
                    Vector3.Dot(offset, plan.Forward));
                if (Vector2.Distance(
                        actual,
                        CanonicalFootprintLocal[index]) > 0.01f)
                {
                    throw new ArgumentException(
                        "Cafe collision descriptors require the canonical " +
                        "five-sided terminal footprint.",
                        nameof(plan));
                }
            }

            Vector3 doorOffset = plan.DoorCenter - plan.Center;
            Vector2 localDoor = new Vector2(
                Vector3.Dot(doorOffset, plan.Right),
                Vector3.Dot(doorOffset, plan.Forward));
            if (Vector2.Distance(
                    localDoor,
                    new Vector2(-3.52f, -4.56f)) > 0.01f)
            {
                throw new ArgumentException(
                    "Cafe collision descriptors require the canonical " +
                    "terminal entrance position.",
                    nameof(plan));
            }
        }
    }
}
