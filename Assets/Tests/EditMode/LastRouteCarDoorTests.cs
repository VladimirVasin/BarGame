using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The car's front doors open now, and opening a door is the kind of
    /// change that invalidates things nobody was watching.
    ///
    /// Two of them in particular. The leaf sweeps a 1.5 m disc around its
    /// hinge, and both docks - the hero's passenger dock and the Ferryman's
    /// at the driver's side - used to stand well inside it, so the door
    /// would have swung straight through whoever was waiting there. And the
    /// handle used to be drawn into the flank's trim mesh, which was
    /// invisible for exactly as long as the door never moved and a chrome
    /// bar hanging in an empty doorway the moment it did.
    ///
    /// Everything here is measured off the DRAWN geometry rather than the
    /// generator's numbers, so a redrawn door moves the contract with it.
    /// </summary>
    public sealed class LastRouteCarDoorTests
    {
        /// <summary>The hero's capsule and its skin, from PlayerFactory.
        /// </summary>
        private const float CapsuleReach = 0.36f;

        /// <summary>A door that opens less than this is a gap, not a way
        /// in.</summary>
        private const float MinimumUsefulOpenAngle = 50f;

        private static GameObject BuildCar(
            out LastRouteCarAssetRegistry registry,
            out LastRouteCarDoors doors)
        {
            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            Assert.That(prefab, Is.Not.Null, "The car prefab is missing.");
            var root = new GameObject("Door Test Car");
            GameObject instance = Object.Instantiate(prefab, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            registry =
                instance.GetComponentInChildren<LastRouteCarAssetRegistry>(true);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.IsBound, Is.True);
            doors = root.AddComponent<LastRouteCarDoors>();
            doors.Initialize(registry);
            return root;
        }

        [Test]
        public void Leaves_SwingOutOfTheCabinAndComeBackExactly()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDoors doors);
            try
            {
                Transform car = registry.transform;
                foreach (bool driver in new[] { true, false })
                {
                    Transform leaf = driver
                        ? registry.DriverDoorLeaf
                        : registry.PassengerDoorLeaf;
                    Renderer panel = leaf.GetComponentInChildren<Renderer>(true);
                    Assert.That(panel, Is.Not.Null);

                    Vector3 closed = panel.bounds.center;
                    float closedSide = Mathf.Abs(
                        Vector3.Dot(closed - car.position, car.right));
                    Quaternion closedRotation = leaf.rotation;

                    if (driver)
                    {
                        doors.SetDriverOpenness(1f);
                    }
                    else
                    {
                        doors.SetPassengerOpenness(1f);
                    }

                    Vector3 open = panel.bounds.center;
                    float openSide = Mathf.Abs(
                        Vector3.Dot(open - car.position, car.right));
                    string side = driver ? "driver" : "passenger";
                    Assert.That(
                        openSide,
                        Is.GreaterThan(closedSide + 0.25f),
                        $"The {side} leaf must swing OUT of the car, not " +
                        "into its own cabin.");
                    Assert.That(
                        Quaternion.Angle(closedRotation, leaf.rotation),
                        Is.GreaterThan(MinimumUsefulOpenAngle),
                        $"The {side} door has to open far enough to get " +
                        "through.");

                    if (driver)
                    {
                        doors.SetDriverOpenness(0f);
                    }
                    else
                    {
                        doors.SetPassengerOpenness(0f);
                    }

                    Assert.That(
                        Quaternion.Angle(closedRotation, leaf.rotation),
                        Is.LessThan(0.01f),
                        $"A shut {side} door must return to the pose it was " +
                        "drawn in, not to an accumulated one.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Handle_RidesTheLeafRatherThanTheFlank()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDoors doors);
            try
            {
                Renderer handle = null;
                foreach (LastRouteCarRendererBinding binding in
                         registry.Bindings)
                {
                    if (binding.Role == "door_handle" &&
                        binding.Renderer != null &&
                        binding.Renderer.transform.IsChildOf(
                            registry.DriverDoorLeaf))
                    {
                        handle = binding.Renderer;
                        break;
                    }
                }

                Assert.That(
                    handle,
                    Is.Not.Null,
                    "The driver's door handle must be a child of the leaf " +
                    "it opens; drawn into the body's trim it stays bolted " +
                    "to the flank while the door swings away from it.");

                Vector3 closed = handle.bounds.center;
                doors.SetDriverOpenness(1f);
                Assert.That(
                    Vector3.Distance(closed, handle.bounds.center),
                    Is.GreaterThan(0.30f),
                    "The handle has to travel with the door.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BothDocks_StandOutsideTheirDoorsSwing()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDoors doors);
            try
            {
                float groundY = registry.transform.position.y;
                LastRouteCarSeatPlan seat =
                    LastRouteCarSeatPlan.Create(registry, groundY);
                Assert.That(seat.IsPresent, Is.True);
                LastRouteFerrymanBoardingPlan boarding =
                    LastRouteFerrymanBoardingPlan.Create(registry, groundY);
                Assert.That(boarding.IsPresent, Is.True);

                float passengerClearance =
                    LastRouteCarDoors.MeasureSwingClearance(
                        seat.EntryRootPosition,
                        registry.PassengerDoorLeaf.position,
                        doors.PassengerLeafReach);
                Assert.That(
                    passengerClearance,
                    Is.GreaterThan(CapsuleReach),
                    "The hero waits at his dock while the passenger door " +
                    "opens over him. The leaf sweeps every bearing between " +
                    "shut and open, so the only safe place to stand is " +
                    $"beyond its {doors.PassengerLeafReach:0.###} m reach.");

                Assert.That(
                    boarding.DoorSwingClearance,
                    Is.GreaterThan(CapsuleReach),
                    "The Ferryman steps back to this point as the door " +
                    "comes; he must be clear of it by the time it is open.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LeafReach_IsMeasuredFromTheDrawnDoorRatherThanAssumed()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDoors doors);
            try
            {
                // A front door on a 4.83 m saloon. The band is wide on
                // purpose: what matters is that the number comes off the
                // renderers, not that it is any particular value.
                Assert.That(doors.DriverLeafReach, Is.InRange(1.2f, 1.9f));
                Assert.That(
                    doors.PassengerLeafReach,
                    Is.EqualTo(doors.DriverLeafReach).Within(0.01f),
                    "The two front doors are the same door mirrored.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The passenger leaf belongs to the hand that is pulling it.
        ///
        /// It used to belong to a timer instead: the seat set a target the
        /// moment the hero was told to walk over, and a
        /// <c>MoveTowards</c> in <c>Update</c> swung the door open while he
        /// was still two metres away with his hands by his sides. Now
        /// `CarBoardEnter` is the Ferryman's own beat authored on the
        /// hero's rig, so the leaf reads the same curve his does - shut
        /// until the hand is on the handle, open before the body moves,
        /// and pulled to once he is down.
        /// </summary>
        [Test]
        public void PassengerLeaf_FollowsTheFerrymansOwnCurveOnTheWayIn()
        {
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(0f),
                Is.Zero,
                "Shut while he is still reaching.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(
                    LastRouteFerrymanBoardingTimeline.DoorPullPhase),
                Is.Zero,
                "And shut right up to the moment the hand takes it.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(
                    LastRouteFerrymanBoardingTimeline.DoorOpenPhase),
                Is.EqualTo(1f).Within(0.001f),
                "Open before the body starts moving into it.");
            Assert.That(
                LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness(1f),
                Is.Zero,
                "And shut over him by the last frame.");
        }

        /// <summary>
        /// And on the way out it is his own curve, because the Ferryman
        /// never gets out of the car and has none: shoved open from inside
        /// almost at once, held while he unfolds himself through it, and
        /// pushed to behind him while he is already walking away.
        /// </summary>
        [Test]
        public void PassengerLeaf_OpensFirstAndShutsLastOnTheWayOut()
        {
            Assert.That(
                LastRouteCarSeatInteraction.EvaluateAlightDoorOpenness(0f),
                Is.Zero,
                "Shut around a seated man.");
            Assert.That(
                LastRouteCarSeatInteraction.EvaluateAlightDoorOpenness(
                    LastRouteCarSeatInteraction.AlightDoorOpenPhase),
                Is.EqualTo(1f).Within(0.001f),
                "Open long before his shoulders are in the aperture.");
            Assert.That(
                LastRouteCarSeatInteraction.EvaluateAlightDoorOpenness(
                    LastRouteCarSeatInteraction.AlightDoorShutStartPhase),
                Is.EqualTo(1f).Within(0.001f),
                "Still open while he is stepping down through it.");
            Assert.That(
                LastRouteCarSeatInteraction.EvaluateAlightDoorOpenness(1f),
                Is.Zero,
                "And shut behind him.");

            // Monotonic through each half, so no frame of the beat ever
            // shows the leaf going the wrong way.
            float previous = 0f;
            for (int step = 0; step <= 40; step++)
            {
                float progress =
                    LastRouteCarSeatInteraction.AlightDoorShutStartPhase *
                    (step / 40f);
                float openness = LastRouteCarSeatInteraction
                    .EvaluateAlightDoorOpenness(progress);
                Assert.That(openness, Is.GreaterThanOrEqualTo(previous));
                previous = openness;
            }
        }
    }
}
