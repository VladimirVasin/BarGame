using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ClosedDoorCollisionTests
    {
        [TestCase("Bar")]
        [TestCase("Home")]
        [TestCase("Church")]
        [TestCase("StairwellStreet")]
        [TestCase("StairwellApartment")]
        public void ClosedLeaf_BlocksTheHeroFromBothSidesAndLeavesItsDockReachable(string location)
        {
            var host = new GameObject("Closed Door Collision " + location);
            try
            {
                DoorCase door = Build(host.transform, location);
                // Room authoring guards validate the scene's original axes.
                // Move the completed hierarchy to exercise placed collision.
                host.transform.SetPositionAndRotation(new Vector3(31f, 6f, -29f),
                    Quaternion.Euler(0f, 37f, 0f));
                Assert.That(door.Leaf, Is.Not.Null, location + " has no solid door leaf.");
                Assert.That(door.Leaf.enabled && !door.Leaf.isTrigger, Is.True);
                Vector3 normal = door.Root.TransformDirection(door.LocalNormal);
                Vector3 plane = door.Visual.bounds.center;
                plane.y = door.Root.TransformPoint(new Vector3(0f,
                    door.FloorHeight + PlayerFactory.GroundedRootOffset, 0f)).y;

                // Backing walls, floors and walkable masks must not make a
                // render-only leaf look solid. Exercise only the actual leaf.
                Collider[] colliders = host.GetComponentsInChildren<Collider>(true);
                var previousEnabled = new bool[colliders.Length];
                for (int index = 0; index < colliders.Length; index++)
                {
                    previousEnabled[index] = colliders[index].enabled;
                    colliders[index].enabled = colliders[index] == door.Leaf;
                }
                Physics.SyncTransforms();
                for (int side = -1; side <= 1; side += 2)
                {
                    CharacterController hero = CreateHero(host.transform, plane - normal * (side * .8f));
                    try
                    {
                        CollisionFlags hits = CollisionFlags.None;
                        for (int step = 0; step < 25; step++)
                            hits |= hero.Move(normal * (side * .08f));
                        Assert.That((hits & CollisionFlags.Sides) != 0, Is.True,
                            location + " did not physically stop the capsule from side " + side);
                        Assert.That(Vector3.Dot(hero.transform.position - plane, normal) * side,
                            Is.LessThan(-.20f), location + " let the hero cross its closed leaf.");
                    }
                    finally { Object.DestroyImmediate(hero.gameObject); }
                }

                for (int index = 0; index < colliders.Length; index++)
                    colliders[index].enabled = previousEnabled[index];
                Physics.SyncTransforms();

                Vector3 dock = door.Root.TransformPoint(door.LocalDock);
                Vector3 inward = door.Root.TransformDirection(door.LocalInward);
                CharacterController approaching = CreateHero(host.transform, dock + inward * .35f);
                try
                {
                    for (int step = 0; step < 20; step++)
                        approaching.Move(Vector3.ClampMagnitude(dock - approaching.transform.position, .04f));
                    Assert.That(Vector3.Distance(approaching.transform.position, dock),
                        Is.LessThan(PlayerMotor.InteractionPositionTolerance),
                        location + " blocks its existing E-action dock.");
                }
                finally { Object.DestroyImmediate(approaching.gameObject); }
            }
            finally { Object.DestroyImmediate(host); }
        }

        private static CharacterController CreateHero(Transform parent, Vector3 position)
        {
            var actor = new GameObject("Door Probe Hero");
            actor.transform.SetParent(parent, false);
            actor.transform.position = position;
            CharacterController body = actor.AddComponent<CharacterController>();
            body.height = 1.7f;
            body.radius = .32f;
            body.center = new Vector3(0f, .85f, 0f);
            body.skinWidth = PlayerFactory.GroundedRootOffset;
            body.stepOffset = PlayerFactory.StepOffset;
            body.slopeLimit = PlayerFactory.SlopeLimitDegrees;
            body.minMoveDistance = 0f;
            Physics.SyncTransforms();
            return body;
        }

        private static DoorCase Build(Transform parent, string location)
        {
            switch (location)
            {
                case "Bar":
                {
                    BarInteriorLayoutPlan plan = BarInteriorLayoutPlanner.Generate(
                        17, "closed-door-test", BarActivityKind.BeerPong);
                    Transform room = BarInteriorWorldBuilder.Build(parent, plan);
                    Vector3 dock = plan.ExitApproachPosition;
                    dock.y = PlayerFactory.GroundedRootOffset;
                    return new DoorCase(room, room.Find("Entrance Door Collision")?.GetComponent<Collider>(),
                        room.Find("Entrance Door").GetComponent<Renderer>(), dock, Vector3.forward, Vector3.forward);
                }
                case "Home":
                {
                    HomeInteriorLayoutPlan plan = HomeInteriorLayoutPlanner.Generate();
                    Transform room = HomeInteriorWorldBuilder.Build(parent, plan);
                    Transform leaf = room.Find("Home Entrance Door/Home Exit Door");
                    int solidCount = 0;
                    foreach (Collider candidate in leaf.parent.GetComponentsInChildren<Collider>(true))
                        if (candidate.enabled && !candidate.isTrigger) solidCount++;
                    Assert.That(solidCount, Is.EqualTo(1),
                        "The shared apartment assembly owns one solid leaf, without a duplicate frame blocker.");
                    Vector3 dock = new Vector3(plan.ExitPosition.x, PlayerFactory.GroundedRootOffset,
                        plan.WalkableBounds.yMin + PlayerDoorActionPlan.DockBoundaryClearance);
                    return new DoorCase(room, leaf.GetComponent<Collider>(), leaf.GetComponent<Renderer>(),
                        dock, Vector3.forward, Vector3.forward);
                }
                case "Church":
                {
                    ChurchInteriorLayoutPlan plan = ChurchInteriorLayoutPlanner.Generate(17);
                    ChurchInteriorWorldResult world = ChurchInteriorWorldBuilder.Build(parent, plan);
                    Renderer visual = null;
                    foreach (Renderer candidate in world.Root.GetComponentsInChildren<Renderer>())
                        if (candidate.name == "INT_WestDoor") visual = candidate;
                    Assert.That(visual, Is.Not.Null, "The closed west door model is missing.");
                    Vector3 dock = new Vector3(plan.ExitPosition.x, PlayerFactory.GroundedRootOffset,
                        plan.WalkableBounds.yMin + PlayerDoorActionPlan.DockBoundaryClearance);
                    return new DoorCase(world.Root,
                        world.Root.Find(ChurchInteriorWorldBuilder.CollisionRootName + "/Entrance Door")?.GetComponent<Collider>(),
                        visual, dock, Vector3.forward, Vector3.forward);
                }
                default:
                {
                    StairwellLayoutPlan plan = StairwellLayoutPlanner.Generate();
                    StairwellWorldResult world = StairwellWorldBuilder.Build(parent, plan);
                    bool street = location == "StairwellStreet";
                    Transform leaf = (street ? world.StreetDoor : world.ApartmentDoor)
                        .Find(street ? "Street Door Leaf" : "Apartment Door Leaf");
                    return new DoorCase(world.Root, leaf.GetComponent<Collider>(), leaf.GetComponent<Renderer>(),
                        street ? plan.StreetDoorActionRoot : plan.ApartmentDoorActionRoot,
                        street ? Vector3.forward : Vector3.right,
                        street ? Vector3.forward : Vector3.left,
                        street ? plan.StreetElevation : plan.ApartmentElevation);
                }
            }
        }

        private readonly struct DoorCase
        {
            public DoorCase(Transform root, Collider leaf, Renderer visual, Vector3 dock,
                Vector3 normal, Vector3 inward, float floorHeight = 0f)
            {
                Root = root; Leaf = leaf; Visual = visual; LocalDock = dock;
                LocalNormal = normal; LocalInward = inward; FloorHeight = floorHeight;
            }
            public Transform Root { get; }
            public Collider Leaf { get; }
            public Renderer Visual { get; }
            public Vector3 LocalDock { get; }
            public Vector3 LocalNormal { get; }
            public Vector3 LocalInward { get; }
            public float FloorHeight { get; }
        }
    }
}
