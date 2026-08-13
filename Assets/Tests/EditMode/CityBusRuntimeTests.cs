using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusRuntimeTests
    {
        private const float VisibleEncounterDistance = 24f;

        [Test]
        public void SamePlanAndAdvanceSequence_RepeatsBusLifecycle()
        {
            CityBusPlan plan = CreateDirectedApproachPlan(
                80.4f,
                true);
            RuntimeFixture first = null;
            RuntimeFixture second = null;
            try
            {
                first = RuntimeFixture.Create("First", plan);
                second = RuntimeFixture.Create("Second", plan);

                Assert.That(
                    first.Director.TimeUntilNextSpawn,
                    Is.EqualTo(second.Director.TimeUntilNextSpawn)
                        .Within(0.000001f));

                float initialDelay =
                    first.Director.TimeUntilNextSpawn + 0.01f;
                first.Director.Advance(initialDelay);
                second.Director.Advance(initialDelay);

                Assert.That(first.Director.ActiveCount, Is.EqualTo(1));
                Assert.That(second.Director.ActiveCount, Is.EqualTo(1));
                Assert.That(first.Actor.EngineAudioSource.clip, Is.Not.Null);
                Assert.That(first.Actor.EngineAudioSource.loop, Is.True);
                Assert.That(
                    first.Actor.EngineAudioSource.spatialBlend,
                    Is.EqualTo(1f));
                Assert.That(
                    first.Actor.EngineAudioSource.volume,
                    Is.EqualTo(CityBusActor.EngineIdleVolume)
                        .Within(0.0001f));
                Assert.That(
                    first.Actor.SpawnAnchorId,
                    Is.EqualTo(second.Actor.SpawnAnchorId));
                Assert.That(first.Director.ActiveCount, Is.LessThanOrEqualTo(1));

                bool observedOpenDoor = false;
                for (int step = 0; step < 120; step++)
                {
                    first.Director.Advance(0.25f);
                    second.Director.Advance(0.25f);

                    Assert.That(
                        first.Actor.MotionState,
                        Is.EqualTo(second.Actor.MotionState));
                    Assert.That(
                        first.Actor.DistanceAlongLink,
                        Is.EqualTo(second.Actor.DistanceAlongLink)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.Speed,
                        Is.EqualTo(second.Actor.Speed).Within(0.0001f));
                    Assert.That(
                        first.Presentation.DoorOpenness,
                        Is.EqualTo(second.Presentation.DoorOpenness)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.DwellCount,
                        Is.EqualTo(second.Actor.DwellCount));
                    observedOpenDoor |=
                        first.Presentation.DoorOpenness > 0.01f;
                }

                Assert.That(observedOpenDoor, Is.True);
                Assert.That(first.Actor.DwellCount, Is.EqualTo(1),
                    "The first partial loop must not serve the same stop " +
                    "twice before the route wraps.");
                first.Director.Shutdown();
                Assert.That(first.Director.ActiveCount, Is.Zero);
                Assert.That(first.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dormant));
                Assert.That(first.Actor.BodyCollider.enabled, Is.False);
                Assert.That(first.Presentation.DoorOpenness, Is.Zero);
                Assert.That(first.Presentation.NightFactor, Is.Zero);
                Assert.That(first.Presentation.BrakeFactor, Is.Zero);
                Assert.That(first.Actor.EngineAudioSource.isPlaying, Is.False);
                Assert.That(first.Actor.EngineAudioSource.volume, Is.Zero);
                Assert.That(
                    first.Actor.EngineAudioSource.pitch,
                    Is.EqualTo(CityBusActor.EngineIdlePitch)
                        .Within(0.0001f));
            }
            finally
            {
                first?.Destroy();
                second?.Destroy();
            }
        }

        [Test]
        public void PresentationNightLights_AreSprungScaledAndPoolSafe()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Night Lights",
                    CreateCyclicPlan(false));
                CityBusPresentation presentation = fixture.Presentation;
                Light[] lights = presentation.GetComponentsInChildren<Light>(
                    true);

                Assert.That(lights, Has.Length.EqualTo(4));
                Assert.That(
                    presentation.HeadlightLights.Count,
                    Is.EqualTo(2));
                Assert.That(
                    presentation.CabinLights.Count,
                    Is.EqualTo(2));
                Assert.That(
                    presentation.HeadlightLights[0].name,
                    Is.EqualTo("Bus Headlight Left"));
                Assert.That(
                    presentation.HeadlightLights[1].name,
                    Is.EqualTo("Bus Headlight Right"));
                Assert.That(
                    presentation.CabinLights[0].name,
                    Is.EqualTo("Bus Cabin Light Front"));
                Assert.That(
                    presentation.CabinLights[1].name,
                    Is.EqualTo("Bus Cabin Light Rear"));

                for (int index = 0; index < lights.Length; index++)
                {
                    Light light = lights[index];
                    Assert.That(light.type, Is.EqualTo(LightType.Spot));
                    Assert.That(
                        light.transform.IsChildOf(
                            presentation.SuspensionVisual),
                        Is.True,
                        $"{light.name} must follow the sprung bus body.");
                }

                for (int index = 0;
                     index < presentation.HeadlightLights.Count;
                     index++)
                {
                    Vector3 direction = presentation.transform
                        .InverseTransformDirection(
                            presentation.HeadlightLights[index]
                                .transform.forward);
                    Assert.That(
                        Vector3.Dot(direction, Vector3.forward),
                        Is.GreaterThan(0.98f));
                    Assert.That(
                        Vector3.Dot(direction, Vector3.down),
                        Is.GreaterThan(0.08f));
                }

                for (int index = 0;
                     index < presentation.CabinLights.Count;
                     index++)
                {
                    Vector3 direction = presentation.transform
                        .InverseTransformDirection(
                            presentation.CabinLights[index]
                                .transform.forward);
                    Assert.That(
                        Vector3.Dot(direction, Vector3.down),
                        Is.GreaterThan(0.99f));
                }

                presentation.SetNightFactor(0f);
                for (int index = 0; index < lights.Length; index++)
                {
                    Assert.That(lights[index].enabled, Is.False);
                    Assert.That(lights[index].intensity, Is.Zero);
                }

                presentation.SetNightFactor(0.5f);
                float[] halfIntensities = new float[lights.Length];
                for (int index = 0; index < lights.Length; index++)
                {
                    Assert.That(lights[index].enabled, Is.True);
                    Assert.That(lights[index].intensity, Is.GreaterThan(0f));
                    halfIntensities[index] = lights[index].intensity;
                }

                presentation.SetNightFactor(1f);
                for (int index = 0; index < lights.Length; index++)
                {
                    Assert.That(lights[index].enabled, Is.True);
                    Assert.That(
                        lights[index].intensity,
                        Is.EqualTo(halfIntensities[index] * 2f)
                            .Within(0.0001f));
                }

                presentation.ResetForPool();
                Light[] pooledLights =
                    presentation.GetComponentsInChildren<Light>(true);
                Assert.That(presentation.NightFactor, Is.Zero);
                Assert.That(pooledLights, Has.Length.EqualTo(4));
                CollectionAssert.AreEquivalent(lights, pooledLights);
                for (int index = 0; index < pooledLights.Length; index++)
                {
                    Assert.That(pooledLights[index].enabled, Is.False);
                    Assert.That(pooledLights[index].intensity, Is.Zero);
                }
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void PresentationWipers_SweepWithRainAndParkWhenDry()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Wipers",
                    CreateCyclicPlan(false));
                CityBusPresentation presentation = fixture.Presentation;
                CityBusAssetRegistry registry = presentation.Registry;
                Assert.That(registry.LeftWiperPivot, Is.Not.Null);
                Assert.That(registry.RightWiperPivot, Is.Not.Null);
                Quaternion leftRest =
                    registry.LeftWiperPivot.localRotation;
                Quaternion rightRest =
                    registry.RightWiperPivot.localRotation;

                presentation.AdvanceWipers(0f, 0.25f);
                Assert.That(presentation.WiperAngleDegrees, Is.Zero);
                Assert.That(
                    Quaternion.Angle(
                        registry.LeftWiperPivot.localRotation,
                        leftRest),
                    Is.LessThan(0.001f));

                presentation.AdvanceWipers(1f, 0.2f);
                float sweptAngle = presentation.WiperAngleDegrees;
                Assert.That(presentation.RainIntensity, Is.EqualTo(1f));
                Assert.That(Mathf.Abs(sweptAngle), Is.GreaterThan(1f));
                Assert.That(
                    Mathf.Abs(sweptAngle),
                    Is.LessThanOrEqualTo(
                        CityBusPresentation.MaximumWiperSweepDegrees));
                Assert.That(
                    Quaternion.Angle(
                        registry.LeftWiperPivot.localRotation,
                        leftRest),
                    Is.EqualTo(Mathf.Abs(sweptAngle)).Within(0.01f));
                Assert.That(
                    Quaternion.Angle(
                        registry.RightWiperPivot.localRotation,
                        rightRest),
                    Is.EqualTo(Mathf.Abs(sweptAngle)).Within(0.01f));
                // Mirrored blades: the two relative rotations must cancel.
                Quaternion leftRelative =
                    Quaternion.Inverse(leftRest) *
                    registry.LeftWiperPivot.localRotation;
                Quaternion rightRelative =
                    Quaternion.Inverse(rightRest) *
                    registry.RightWiperPivot.localRotation;
                Assert.That(
                    Quaternion.Angle(
                        leftRelative,
                        Quaternion.Inverse(rightRelative)),
                    Is.LessThan(0.01f));

                for (int step = 0; step < 60; step++)
                {
                    presentation.AdvanceWipers(0f, 0.05f);
                }

                Assert.That(presentation.WiperAngleDegrees, Is.Zero);
                Assert.That(
                    Quaternion.Angle(
                        registry.LeftWiperPivot.localRotation,
                        leftRest),
                    Is.LessThan(0.001f));

                presentation.AdvanceWipers(1f, 0.2f);
                Assert.That(
                    Mathf.Abs(presentation.WiperAngleDegrees),
                    Is.GreaterThan(1f));
                presentation.ResetForPool();
                Assert.That(presentation.WiperAngleDegrees, Is.Zero);
                Assert.That(presentation.RainIntensity, Is.Zero);
                Assert.That(
                    Quaternion.Angle(
                        registry.LeftWiperPivot.localRotation,
                        leftRest),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(
                        registry.RightWiperPivot.localRotation,
                        rightRest),
                    Is.LessThan(0.001f));
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void PresentationSuspension_MovesBodyRelativeToGroundedWheels_AndPoolResetRestoresNeutralPose()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Suspension",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();

                CityBusPresentation presentation = fixture.Presentation;
                CityBusAssetRegistry registry = presentation.Registry;
                Transform suspension = presentation.SuspensionVisual;
                Assert.That(suspension, Is.Not.Null);
                Assert.That(registry.Body.parent, Is.SameAs(suspension));

                Transform[] wheelRoots =
                {
                    registry.FrontLeftSteeringPivot,
                    registry.FrontRightSteeringPivot,
                    registry.RearLeftWheel,
                    registry.RearRightWheel
                };
                Vector3[] groundedWheelPositions =
                    new Vector3[wheelRoots.Length];
                Quaternion[] wheelRotations =
                    new Quaternion[wheelRoots.Length];
                for (int index = 0; index < wheelRoots.Length; index++)
                {
                    Assert.That(
                        wheelRoots[index].IsChildOf(suspension),
                        Is.False,
                        "Wheel assemblies must stay outside the sprung " +
                        "body hierarchy.");
                    groundedWheelPositions[index] =
                        presentation.transform.InverseTransformPoint(
                            wheelRoots[index].position);
                    wheelRotations[index] = wheelRoots[index].localRotation;
                }

                Vector3 neutralSuspensionPosition =
                    suspension.localPosition;
                Quaternion neutralSuspensionRotation =
                    suspension.localRotation;
                Vector3 neutralBodyPosition =
                    presentation.transform.InverseTransformPoint(
                        registry.Body.position);
                Vector3 actorPosition = fixture.Actor.Position;
                Quaternion actorRotation = fixture.Actor.Rotation;
                Vector3 colliderCenter = fixture.Actor.BodyCollider.center;
                Vector3 colliderSize = fixture.Actor.BodyCollider.size;
                float maximumBodyDisplacement = 0f;

                for (int step = 0; step < 12; step++)
                {
                    presentation.SetMotion(
                        0.37f,
                        4.8f,
                        step < 6
                            ? CityBusActor.Acceleration
                            : -CityBusActor.ServiceDeceleration,
                        14f,
                        step >= 6,
                        0.1f);
                    maximumBodyDisplacement = Mathf.Max(
                        maximumBodyDisplacement,
                        Vector3.Distance(
                            neutralBodyPosition,
                            presentation.transform.InverseTransformPoint(
                                registry.Body.position)));
                    for (int index = 0;
                         index < wheelRoots.Length;
                         index++)
                    {
                        Assert.That(
                            presentation.transform.InverseTransformPoint(
                                wheelRoots[index].position),
                            Is.EqualTo(groundedWheelPositions[index]));
                    }
                }

                Assert.That(
                    maximumBodyDisplacement,
                    Is.GreaterThan(0.001f),
                    "Moving suspension must visibly displace the body " +
                    "relative to grounded wheel contacts.");
                Assert.That(
                    Mathf.Abs(presentation.SuspensionHeave),
                    Is.LessThanOrEqualTo(
                        CityBusPresentation.MaximumSuspensionHeave +
                        0.0001f));
                Assert.That(
                    Mathf.Abs(presentation.SuspensionPitch),
                    Is.LessThanOrEqualTo(
                        CityBusPresentation.MaximumSuspensionPitch +
                        0.0001f));
                Assert.That(
                    Mathf.Abs(presentation.SuspensionRoll),
                    Is.LessThanOrEqualTo(
                        CityBusPresentation.MaximumSuspensionRoll +
                        0.0001f));
                Assert.That(fixture.Actor.Position, Is.EqualTo(actorPosition));
                Assert.That(
                    fixture.Actor.Rotation,
                    Is.EqualTo(actorRotation));
                Assert.That(
                    fixture.Actor.BodyCollider.center,
                    Is.EqualTo(colliderCenter));
                Assert.That(
                    fixture.Actor.BodyCollider.size,
                    Is.EqualTo(colliderSize));

                presentation.ResetForPool();

                Assert.That(presentation.SuspensionHeave, Is.Zero);
                Assert.That(presentation.SuspensionPitch, Is.Zero);
                Assert.That(presentation.SuspensionRoll, Is.Zero);
                Assert.That(
                    suspension.localPosition,
                    Is.EqualTo(neutralSuspensionPosition));
                Assert.That(
                    Quaternion.Angle(
                        suspension.localRotation,
                        neutralSuspensionRotation),
                    Is.LessThan(0.0001f));
                for (int index = 0; index < wheelRoots.Length; index++)
                {
                    Assert.That(
                        presentation.transform.InverseTransformPoint(
                            wheelRoots[index].position),
                        Is.EqualTo(groundedWheelPositions[index]));
                    Assert.That(
                        Quaternion.Angle(
                            wheelRoots[index].localRotation,
                            wheelRotations[index]),
                        Is.LessThan(0.0001f));
                }
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [TestCase(0f)]
        [TestCase(6f)]
        public void Actor_RoutePosePreservesFlatMotionAndSupportsGrades(
            float firstLinkRise)
        {
            CityBusPlan plan = CreateDirectedApproachPlan(
                80.4f,
                false,
                firstLinkRise: firstLinkRise);
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create("Route Grade", plan);
                fixture.SpawnDirectly();

                CityBusRouteLink link = plan.Links[0];
                CityBusPathSample start = link.Samples[0];
                CityBusPathSample end = link.Samples[1];
                Vector3 routeForward = start.Forward.normalized;
                RigidbodyConstraints constraints =
                    fixture.Actor.RigidBody.constraints;
                Assert.That(
                    constraints & RigidbodyConstraints.FreezePositionY,
                    Is.EqualTo(RigidbodyConstraints.None));
                Assert.That(
                    constraints & RigidbodyConstraints.FreezeRotationX,
                    Is.EqualTo(RigidbodyConstraints.None));
                Assert.That(
                    constraints & RigidbodyConstraints.FreezeRotationZ,
                    Is.EqualTo(RigidbodyConstraints.FreezeRotationZ));

                Transform suspension =
                    fixture.Presentation.SuspensionVisual;
                fixture.Actor.Advance(
                    0.5f,
                    CityBusObstacleState.Clear,
                    0f);

                float progress = fixture.Actor.DistanceAlongLink /
                    link.Length;
                Vector3 expectedPosition = Vector3.Lerp(
                    start.Position,
                    end.Position,
                    progress);
                Assert.That(
                    Vector3.Distance(
                        fixture.Actor.Position,
                        expectedPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Angle(
                        fixture.Actor.TravelDirection,
                        routeForward),
                    Is.LessThan(0.001f));
                Assert.That(
                    fixture.Actor.Position.y,
                    Is.EqualTo(expectedPosition.y).Within(0.0001f));
                Assert.That(
                    fixture.Actor.TravelDirection.y,
                    Is.EqualTo(routeForward.y).Within(0.0001f));
                Assert.That(
                    Mathf.Abs(Vector3.Dot(
                        fixture.Actor.transform.right,
                        Vector3.up)),
                    Is.LessThan(0.0001f),
                    "The road pose may pitch, but must not add route roll.");
                Assert.That(
                    fixture.Presentation.transform.localRotation,
                    Is.EqualTo(Quaternion.identity));
                Assert.That(
                    suspension.IsChildOf(
                        fixture.Presentation.transform),
                    Is.True,
                    "The sprung body must stay inside the visual hierarchy " +
                    "that inherits the 3D route pose.");
                Assert.That(
                    Vector3.Angle(
                        fixture.Presentation.transform.forward,
                        fixture.Actor.TravelDirection),
                    Is.LessThan(0.001f),
                    "The visual root must inherit the actor's grade pose.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void StopDwell_HoldsForTenSecondsBeforeResuming()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Ten Second Dwell",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                for (int guard = 0;
                     guard < 1200 &&
                     fixture.Actor.MotionState !=
                         CityBusMotionState.Dwelling;
                     guard++)
                {
                    fixture.Actor.Advance(
                        0.05f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dwelling));
                Assert.That(
                    fixture.Presentation.DoorPhase,
                    Is.EqualTo(CityBusDoorPhase.Opening));
                Assert.That(
                    fixture.Presentation.DriverDoorSample.ButtonPress01,
                    Is.EqualTo(1f));
                Assert.That(
                    CityBusActor.DwellDuration,
                    Is.EqualTo(10f));
                Assert.That(
                    CityBusActor.MinimumDwellDuration,
                    Is.EqualTo(CityBusActor.DwellDuration));
                Assert.That(
                    CityBusActor.MaximumDwellDuration,
                    Is.EqualTo(CityBusActor.DwellDuration));
                Vector3 stoppedPosition = fixture.Actor.Position;
                const float boundaryMargin = 0.01f;
                float fullyOpenSample =
                    CityBusActor.DoorTransitionDuration + 0.01f;

                fixture.Actor.Advance(
                    fullyOpenSample,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Presentation.DoorOpenness,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    fixture.Presentation.DoorPhase,
                    Is.EqualTo(CityBusDoorPhase.Open));
                fixture.Actor.Advance(
                    CityBusActor.DwellDuration -
                    fullyOpenSample -
                    boundaryMargin,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dwelling),
                    "The bus must remain stopped immediately before the " +
                    "ten-second boundary.");
                Assert.That(
                    fixture.Presentation.DoorPhase,
                    Is.EqualTo(CityBusDoorPhase.Closing));
                Assert.That(
                    fixture.Actor.Position,
                    Is.EqualTo(stoppedPosition));
                fixture.Actor.Advance(
                    boundaryMargin * 2f,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.Not.EqualTo(CityBusMotionState.Dwelling));
                Assert.That(fixture.Actor.Position, Is.EqualTo(stoppedPosition));
                Assert.That(fixture.Presentation.DoorOpenness, Is.Zero);
                Assert.That(
                    fixture.Presentation.DoorPhase,
                    Is.EqualTo(CityBusDoorPhase.Closed));
                Assert.That(fixture.Actor.DwellCount, Is.EqualTo(1));
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void PassengerServiceHold_FreezesOpenDwell_AndForcedCleanupOwnsRelease()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Passenger Service Hold",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                for (int guard = 0;
                     guard < 1200 &&
                     fixture.Actor.MotionState !=
                         CityBusMotionState.Dwelling;
                     guard++)
                {
                    fixture.Actor.Advance(
                        0.05f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                fixture.Actor.Advance(
                    CityBusActor.DoorTransitionDuration + 0.01f,
                    CityBusObstacleState.Clear,
                    0f);

                object owner = new object();
                object otherOwner = new object();
                Assert.That(fixture.Actor.CurrentStopIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(fixture.Actor.CurrentStop, Is.Not.Null);
                Assert.That(fixture.Actor.DoorsFullyOpen, Is.True);
                Assert.That(fixture.Actor.ServiceOrdinal, Is.EqualTo(1));
                Assert.That(
                    fixture.Actor.CurrentDwellDuration,
                    Is.EqualTo(CityBusActor.DwellDuration));
                Assert.That(
                    fixture.Actor.TryAcquireServiceHold(owner),
                    Is.True);
                // The hold is shared, not exclusive: an ambient passenger
                // stepping through the doorway must not silently disable the
                // hero's own board prompt. The doors resume only once every
                // owner has let go.
                Assert.That(
                    fixture.Actor.TryAcquireServiceHold(otherOwner),
                    Is.True);
                Assert.That(
                    fixture.Actor.ReleaseServiceHold(otherOwner),
                    Is.True);
                Assert.That(fixture.Actor.HasServiceHold, Is.True);

                // Long enough that the dwell would have finished without the
                // hold, but inside the bound that breaks a leaked one; the
                // expiry itself is covered by
                // LeakedServiceHold_ExpiresInsteadOfSealingTheDoors.
                float heldElapsed = fixture.Actor.DwellElapsed;
                fixture.Actor.Advance(
                    CityBusActor.DwellDuration + 2f,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Actor.DwellElapsed,
                    Is.EqualTo(heldElapsed));
                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dwelling));
                Assert.That(fixture.Actor.DoorsFullyOpen, Is.True);
                Assert.That(fixture.Actor.HasServiceHold, Is.True);
                Assert.That(fixture.Actor.TryAttachPassenger(owner), Is.True);
                Assert.That(fixture.Actor.HasPassenger, Is.True);
                Assert.That(fixture.Actor.HasPlayerPassenger, Is.True);
                Assert.That(
                    fixture.Actor.TryGetOccupantSeatIndex(
                        owner,
                        out int heroSeat),
                    Is.True);
                Assert.That(heroSeat, Is.EqualTo(CityBusActor.PlayerSeatIndex));

                // Ambient passengers always leave one place for the hero, so
                // the cabin never carries more than three.
                var ambient = new List<object>();
                for (int index = 0;
                     index < CityBusActor.CabinCapacity + 1;
                     index++)
                {
                    var candidate = new object();
                    if (!fixture.Actor.TryAttachNpcPassenger(
                            candidate,
                            out int seatIndex))
                    {
                        break;
                    }

                    Assert.That(
                        seatIndex,
                        Is.Not.EqualTo(CityBusActor.PlayerSeatIndex),
                        "Seat 07 stays reserved for the hero.");
                    Assert.That(
                        ambient.Count,
                        Is.LessThan(CityBusActor.MaximumNpcOccupants));
                    ambient.Add(candidate);
                }

                Assert.That(
                    fixture.Actor.NpcOccupantCount,
                    Is.EqualTo(CityBusActor.MaximumNpcOccupants));
                Assert.That(
                    fixture.Actor.OccupantCount,
                    Is.EqualTo(CityBusActor.CabinCapacity));
                for (int index = 0; index < ambient.Count; index++)
                {
                    Assert.That(
                        fixture.Actor.ReleasePassenger(ambient[index]),
                        Is.True);
                }

                Assert.That(
                    fixture.Actor.ReleasePassenger(otherOwner),
                    Is.False);
                Assert.That(
                    fixture.Actor.ReleaseServiceHold(otherOwner),
                    Is.False);

                bool cleanupCalled = false;
                fixture.Director.RegisterPassengerCleanup(() =>
                {
                    cleanupCalled = true;
                    fixture.Actor.ReleasePassenger(owner);
                    fixture.Actor.ReleaseServiceHold(owner);
                });
                // The hero and the ambient passenger controller each own their
                // own cleanup, so registration is multicast rather than a
                // single exclusive callback.
                bool secondCleanupCalled = false;
                fixture.Director.RegisterPassengerCleanup(
                    () => secondCleanupCalled = true);
                fixture.Director.Shutdown();

                Assert.That(cleanupCalled, Is.True);
                Assert.That(secondCleanupCalled, Is.True);
                Assert.That(fixture.Actor.HasPassenger, Is.False);
                Assert.That(fixture.Actor.HasServiceHold, Is.False);
                Assert.That(fixture.Director.ActiveCount, Is.Zero);
                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dormant));
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        /// <summary>
        /// The hero must never find his seat taken. A bus can activate with
        /// ambient passengers already aboard, so the order that matters is the
        /// reverse of an ordinary board: cabin filled first, hero second.
        /// </summary>
        [Test]
        public void FilledCabin_StillAdmitsTheHeroToSeat07()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Reserved Hero Seat",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                // Fill every place ambient passengers are allowed to hold,
                // and keep asking past the limit.
                var ambient = new List<object>();
                for (int index = 0;
                     index < CityBusActor.CabinCapacity + 2;
                     index++)
                {
                    var candidate = new object();
                    if (!fixture.Actor.TryAttachNpcPassenger(
                            candidate,
                            out int seatIndex))
                    {
                        continue;
                    }

                    Assert.That(
                        seatIndex,
                        Is.Not.EqualTo(CityBusActor.PlayerSeatIndex),
                        "No ambient passenger may ever hold seat 07.");
                    ambient.Add(candidate);
                }

                Assert.That(
                    ambient.Count,
                    Is.EqualTo(CityBusActor.MaximumNpcOccupants),
                    "Ambient passengers stop one short of the cabin so the " +
                    "hero always has a place.");
                Assert.That(
                    fixture.Actor.IsSeatOccupied(
                        CityBusActor.PlayerSeatIndex),
                    Is.False);

                var hero = new object();
                Assert.That(
                    fixture.Actor.TryAttachPassenger(hero),
                    Is.True,
                    "A full ambient cabin must not lock the hero out.");
                Assert.That(
                    fixture.Actor.TryGetOccupantSeatIndex(
                        hero,
                        out int heroSeat),
                    Is.True);
                Assert.That(
                    heroSeat,
                    Is.EqualTo(CityBusActor.PlayerSeatIndex));
                Assert.That(
                    fixture.Actor.OccupantCount,
                    Is.EqualTo(CityBusActor.CabinCapacity));
                Assert.That(fixture.Actor.HasPlayerPassenger, Is.True);

                // Seats stay distinct, so nobody is ever seated on anybody.
                var seats = new HashSet<int> { heroSeat };
                for (int index = 0; index < ambient.Count; index++)
                {
                    Assert.That(
                        fixture.Actor.TryGetOccupantSeatIndex(
                            ambient[index],
                            out int seat),
                        Is.True);
                    Assert.That(seats.Add(seat), Is.True);
                }

                for (int index = 0; index < ambient.Count; index++)
                {
                    fixture.Actor.ReleasePassenger(ambient[index]);
                }

                fixture.Actor.ReleasePassenger(hero);
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        /// <summary>
        /// Regression: a service hold freezes the dwell timer, and the door
        /// timeline is sampled from that timer, so an owner that never lets
        /// go leaves the bus parked at every later stop with its doors shut —
        /// no prompt for the hero, no boarding for anyone. The hold is broken
        /// and reported rather than stranding the route for the session.
        /// </summary>
        [Test]
        public void LeakedServiceHold_ExpiresInsteadOfSealingTheDoors()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Leaked Service Hold",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();
                DriveToOpenDoors(fixture);

                var abandoned = new object();
                Assert.That(
                    fixture.Actor.TryAcquireServiceHold(abandoned),
                    Is.True);

                // The owner is never heard from again.
                float held = 0f;
                while (held < CityBusActor.MaximumServiceHoldDuration + 1f &&
                       fixture.Actor.HasServiceHold)
                {
                    fixture.Actor.Advance(
                        0.1f,
                        CityBusObstacleState.Clear,
                        0f);
                    held += 0.1f;
                }

                Assert.That(
                    fixture.Actor.HasServiceHold,
                    Is.False,
                    "A hold that outlives its owner must expire.");
                Assert.That(
                    held,
                    Is.GreaterThanOrEqualTo(
                        CityBusActor.MaximumServiceHoldDuration - 0.2f),
                    "It must not expire early enough to cut a real transfer.");

                // The dwell resumes, so the doors close and the loop goes on
                // instead of the bus sitting at this stop forever.
                for (int guard = 0;
                     guard < 400 &&
                     fixture.Actor.MotionState ==
                         CityBusMotionState.Dwelling;
                     guard++)
                {
                    fixture.Actor.Advance(
                        0.1f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.Not.EqualTo(CityBusMotionState.Dwelling),
                    "The dwell must finish once the leaked hold is gone.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        private static void DriveToOpenDoors(RuntimeFixture fixture)
        {
            for (int guard = 0;
                 guard < 1200 &&
                 fixture.Actor.MotionState != CityBusMotionState.Dwelling;
                 guard++)
            {
                fixture.Actor.Advance(
                    0.05f,
                    CityBusObstacleState.Clear,
                    0f);
            }

            fixture.Actor.Advance(
                CityBusActor.DoorTransitionDuration + 0.01f,
                CityBusObstacleState.Clear,
                0f);
            Assert.That(fixture.Actor.DoorsFullyOpen, Is.True);
        }

        /// <summary>
        /// Regression: the bus could not cover the last third of a metre into a
        /// stop.
        /// <para>
        /// Approach speed rides the service-braking curve exactly, so
        /// `v = sqrt(2 * ServiceDeceleration * distanceToStop)` and one frame
        /// moves `v * deltaTime`. `MoveAlongRoute` used to discard any frame
        /// whose travel was under `DistanceTolerance`, and at `60 fps` that
        /// threshold is crossed as soon as the stop is `0.31 m` away. The
        /// discarded travel left the distance unchanged, so the speed cap was
        /// unchanged, so the next frame was under the threshold too: a latch,
        /// not a rounding loss. `BeginDwell` never ran and the doors, which are
        /// driven only by the dwell timer, never opened.
        /// </para>
        /// <para>
        /// Every other bus test steps at `0.05 s`, where the freeze band is
        /// only `0.034 m` and hides inside the arrival tolerance. This one runs
        /// at a real frame rate on purpose.
        /// </para>
        /// </summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 40f)]
        [TestCase(1f / 45f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 120f)]
        [TestCase(1f / 144f)]
        public void ServiceApproach_ReachesTheStopAtRealFrameRates(
            float deltaTime)
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Approach Dead Zone",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                // Sixty simulated seconds is several times what one approach
                // needs; the old code stood still here for all of them.
                int steps = Mathf.CeilToInt(60f / deltaTime);
                float closest = float.PositiveInfinity;
                for (int index = 0;
                     index < steps &&
                     fixture.Actor.MotionState !=
                         CityBusMotionState.Dwelling;
                     index++)
                {
                    fixture.Actor.Advance(
                        deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                    closest = Mathf.Min(closest, fixture.Actor.Speed);
                }

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dwelling),
                    $"At {1f / deltaTime:F0} fps the bus never reached its " +
                    "stop, so its doors could never open.");
                Assert.That(fixture.Actor.CurrentStop, Is.Not.Null);

                fixture.Actor.Advance(
                    CityBusActor.DoorTransitionDuration + 0.01f,
                    CityBusObstacleState.Clear,
                    0f);
                Assert.That(
                    fixture.Actor.DoorsFullyOpen,
                    Is.True,
                    "Arriving must lead to open doors.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        /// <summary>
        /// The regime that actually stranded the bus in play. Coming down from
        /// cruise, `MoveTowards` saturates at `ServiceDeceleration * deltaTime`
        /// and keeps the bus overspeed relative to the braking curve, so it
        /// punches through the sub-tolerance band. Setting off again from a
        /// standstill it never gets above that band at all: at `40 fps` a frame
        /// commits motion only while `v > 0.80 m/s`, and the curve drops under
        /// that `0.14 m` from the stop. A yield lasting a fraction of a second
        /// is therefore enough to arm a stall of arbitrary length — which is
        /// why nobody had to be standing in front of the bus for fifteen
        /// seconds for it to stand there for fifteen seconds.
        /// </summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 40f)]
        [TestCase(1f / 60f)]
        public void ApproachResumingFromAYield_StillReachesTheStop(
            float deltaTime)
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Yield Then Resume",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                // Hold it at a full stop for a moment on the approach, exactly
                // as an obstacle would.
                var blocked = new CityBusObstacleState(true, 0f);
                for (int index = 0; index < 40; index++)
                {
                    fixture.Actor.Advance(deltaTime, blocked, 0f);
                }

                Assert.That(
                    fixture.Actor.Speed,
                    Is.EqualTo(0f).Within(0.0001f),
                    "The obstacle contract is that the bus stops dead.");
                Assert.That(
                    fixture.Actor.MotionState,
                    Is.Not.EqualTo(CityBusMotionState.Dwelling));

                int steps = Mathf.CeilToInt(60f / deltaTime);
                for (int index = 0;
                     index < steps &&
                     fixture.Actor.MotionState !=
                         CityBusMotionState.Dwelling;
                     index++)
                {
                    fixture.Actor.Advance(
                        deltaTime,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    fixture.Actor.MotionState,
                    Is.EqualTo(CityBusMotionState.Dwelling),
                    $"At {1f / deltaTime:F0} fps the bus never set off again " +
                    "after giving way, so its doors could never open.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void RemoteCycleWithoutEncounter_DoesNotSpawnInvisibleBus()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Remote Cycle",
                    CreateCyclicPlan(false));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.Zero,
                    "A fog-hidden anchor is not a valid candidate when " +
                    "its directed route never reaches the 24 m initial-" +
                    "approach area around the player.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void FallbackBand_SpawnsWhenPreferredBandHasNoAnchor()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Fallback Band",
                    CreateDirectedApproachPlan(
                        60.4f,
                        false,
                        70.4f));
                CityBusSpawnAnchor anchor =
                    fixture.Director.Plan.SpawnAnchors[0];
                Quaternion rotation = Quaternion.LookRotation(
                    anchor.Forward,
                    Vector3.up);
                float bodyDistance =
                    CityBusActor.GetClosestPlanarBodyDistance(
                        fixture.Player.position,
                        anchor.Position,
                        rotation,
                        fixture.Actor.LocalVisualBounds);
                Assert.That(
                    bodyDistance,
                    Is.GreaterThanOrEqualTo(
                        CityBusDirector.FogHiddenDistance));
                Assert.That(
                    bodyDistance,
                    Is.LessThan(CityBusDirector.MinimumSpawnDistance));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.EqualTo(1),
                    "A connected fog-hidden anchor must be used as a " +
                    "fallback when the preferred 76-86 m band is empty.");
                Assert.That(
                    fixture.Actor.GetClosestPlanarBodyDistance(
                        fixture.Player.position),
                    Is.LessThan(CityBusDirector.MinimumSpawnDistance),
                    "The actual spawned pose must come from the fallback " +
                    "band, not another preferred-band sample on the route.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void JunctionOnlyStraightRing_DoesNotProvideSpawnPose()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Junction Only",
                    CreateDirectedApproachPlan(
                        80.4f,
                        false,
                        100.4f,
                        true));

                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(
                    fixture.Director.ActiveCount,
                    Is.Zero,
                    "Runtime sampling must not treat a straight junction " +
                    "maneuver as a road-segment spawn anchor.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void SpawnCandidate_LeavesNormalApproachBeforeNextStop()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Stop Approach",
                    CreateDirectedApproachPlan(
                        80.4f,
                        true,
                        100.4f,
                        false,
                        true));
                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);

                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));
                CityBusStopDescriptor stop =
                    fixture.Director.Plan.Stops[0];
                float forwardStopDistance =
                    GetForwardLoopDistance(
                        fixture.Director.Plan,
                        fixture.Actor.CurrentLinkIndex,
                        fixture.Actor.DistanceAlongLink,
                        stop.LinkIndex,
                        stop.DistanceAlongLink);
                float longitudinalExtent =
                    Mathf.Abs(
                        fixture.Actor.LocalVisualBounds.center.z) +
                    fixture.Actor.LocalVisualBounds.extents.z;
                float minimumServiceApproach =
                    ((CityBusActor.CruiseSpeed *
                      CityBusActor.CruiseSpeed) /
                     (2f * CityBusActor.ServiceDeceleration)) +
                    CityBusActor.ObstacleStopPadding +
                    longitudinalExtent;

                Assert.That(
                    forwardStopDistance,
                    Is.GreaterThanOrEqualTo(
                        minimumServiceApproach - 0.001f),
                    "A runtime-sampled spawn must not make the bus dwell " +
                    "immediately at a hidden stop.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void DirectedAwayApproach_RemainsActiveUntilVisibleEncounter()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Directed Away",
                    CreateDirectedApproachPlan(80.4f, false));
                fixture.Director.Advance(
                    fixture.Director.TimeUntilNextSpawn + 0.01f);
                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));

                bool travelledBeyondOrdinaryRecycleDistance = false;
                bool reachedVisibleEncounter = false;
                float minimumBodyDistance = float.PositiveInfinity;
                for (int step = 0; step < 360; step++)
                {
                    fixture.Director.Advance(0.25f);
                    Assert.That(
                        fixture.Director.ActiveCount,
                        Is.EqualTo(1),
                        "A directed approach must not recycle before its " +
                        $"first encounter; nearest body distance was " +
                        $"{minimumBodyDistance:F2} m.");

                    float bodyDistance =
                        fixture.Actor.GetClosestPlanarBodyDistance(
                            fixture.Player.position);
                    minimumBodyDistance = Mathf.Min(
                        minimumBodyDistance,
                        bodyDistance);
                    travelledBeyondOrdinaryRecycleDistance |=
                        bodyDistance >= CityBusDirector.RecycleDistance;
                    if (bodyDistance <= VisibleEncounterDistance)
                    {
                        reachedVisibleEncounter = true;
                        break;
                    }
                }

                Assert.That(
                    travelledBeyondOrdinaryRecycleDistance,
                    Is.True,
                    "The fixture must exercise the directed-away recycle " +
                    "boundary before turning toward the player.");
                Assert.That(
                    reachedVisibleEncounter,
                    Is.True,
                    $"The successful lifecycle stayed outside the " +
                    $"{VisibleEncounterDistance:F0} m visible encounter " +
                    $"range; nearest body distance was " +
                    $"{minimumBodyDistance:F2} m.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void FixedRing_RouteSequenceDoesNotDependOnBehaviorSeed()
        {
            RuntimeFixture first = null;
            RuntimeFixture second = null;
            try
            {
                CityBusPlan plan = CreateDirectedApproachPlan(
                    80.4f,
                    false);
                first = RuntimeFixture.Create("Route Seed A", plan);
                second = RuntimeFixture.Create("Route Seed B", plan);
                first.SpawnDirectly(0x11111111u);
                second.SpawnDirectly(0xEEEEEEEEu);

                for (int step = 0; step < 600; step++)
                {
                    first.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                    second.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);

                    Assert.That(
                        first.Actor.CurrentLinkIndex,
                        Is.EqualTo(second.Actor.CurrentLinkIndex));
                    Assert.That(
                        first.Actor.DistanceAlongLink,
                        Is.EqualTo(second.Actor.DistanceAlongLink)
                            .Within(0.0001f));
                    Assert.That(
                        first.Actor.Position,
                        Is.EqualTo(second.Actor.Position));
                }
            }
            finally
            {
                first?.Destroy();
                second?.Destroy();
            }
        }

        [Test]
        public void FixedRing_ServesStopAgainOnEveryCompletedLoop()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Repeated Loop Stop",
                    CreateCyclicPlan());
                fixture.SpawnDirectly();

                for (int step = 0; step < 400; step++)
                {
                    fixture.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(
                    fixture.Actor.DwellCount,
                    Is.GreaterThanOrEqualTo(2),
                    "A fixed service stop must be re-enabled after the " +
                    "ordered route wraps, rather than remaining served " +
                    "for the complete pooled spawn.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void Yielding_StopsBeforeFiniteClearance_AndResumesWithoutCamera()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                Assert.That(fixture.Director.ActiveCount, Is.EqualTo(1));

                for (int step = 0; step < 8; step++)
                {
                    fixture.Actor.Advance(
                        0.25f,
                        CityBusObstacleState.Clear,
                        0f);
                }

                Assert.That(fixture.Actor.Speed, Is.GreaterThan(0f));
                float positionBeforeYield =
                    fixture.Actor.DistanceAlongLink;
                fixture.Actor.Advance(
                    0.5f,
                    new CityBusObstacleState(true, 0.20f),
                    0f);

                Assert.That(fixture.Actor.IsYielding, Is.True);
                Assert.That(fixture.Actor.Speed, Is.Zero);
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.EqualTo(positionBeforeYield).Within(0.0001f),
                    "The body must not consume the safety padding.");

                fixture.Actor.Advance(
                    1f,
                    CityBusObstacleState.Clear,
                    0f);
                Assert.That(fixture.Actor.IsYielding, Is.False);
                Assert.That(fixture.Actor.Speed, Is.GreaterThan(0f));
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.GreaterThan(positionBeforeYield));
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void ObstacleProbe_IgnoresBalconyHeightSeparation()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Vertical Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                Vector3 streetTarget =
                    fixture.Actor.Position + (Vector3.right * 6f);

                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        streetTarget,
                        0.35f,
                        12f,
                        out _),
                    Is.True);
                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        streetTarget + (Vector3.up *
                            PlayerHomeBalconyGeometry
                                .ApartmentFloorElevation),
                        0.35f,
                        12f,
                        out _),
                    Is.False);
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void ObstacleBeyondOrderedLinkBoundary_BrakesBeforeSeam()
        {
            RuntimeFixture fixture = null;
            try
            {
                fixture = RuntimeFixture.Create(
                    "Boundary Safety",
                    CreateCyclicPlan(false));
                fixture.SpawnDirectly();
                fixture.Actor.Advance(
                    10f,
                    CityBusObstacleState.Clear,
                    0f);
                fixture.Actor.Advance(
                    1.3f,
                    CityBusObstacleState.Clear,
                    0f);

                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.InRange(37f, 39.9f));
                Vector3 targetOnNextTraversal =
                    new Vector3(2f, 0.08f, 80.4f);
                Assert.That(
                    fixture.Actor.TryGetPathObstacleClearance(
                        targetOnNextTraversal,
                        0.35f,
                        8f,
                        out float clearance),
                    Is.True);
                Assert.That(clearance, Is.LessThan(8f));

                float distanceBeforeBraking =
                    fixture.Actor.DistanceAlongLink;
                fixture.Actor.Advance(
                    0.25f,
                    new CityBusObstacleState(true, clearance),
                    0f);

                Assert.That(fixture.Actor.IsBraking, Is.True);
                Assert.That(fixture.Actor.IsYielding, Is.True);
                Assert.That(
                    fixture.Actor.DistanceAlongLink,
                    Is.EqualTo(distanceBeforeBraking).Within(0.0001f),
                    "The bus must brake on the current link instead of " +
                    "crossing its ordered seam into the obstacle.");
            }
            finally
            {
                fixture?.Destroy();
            }
        }

        [Test]
        public void PlanWithoutSpawnAnchors_IsOperationallyEmpty()
        {
            CityBusPlan plan = CreateCyclicPlan(
                includeStop: false,
                includeAnchor: false);

            Assert.That(plan.Nodes, Is.Not.Empty);
            Assert.That(plan.Links, Is.Not.Empty);
            Assert.That(plan.SpawnAnchors, Is.Empty);
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void CollisionPolicy_KeepsPlayerAndPedestriansSolid()
        {
            Assert.That(
                LayerMask.NameToLayer(CityBusCollision.LayerName),
                Is.EqualTo(CityBusCollision.LayerIndex));

            CityBusCollision.EnsureRuntimePolicy();

            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityBusCollision.DefaultLayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.LayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityBusCollision.LayerIndex,
                    CityBusCollision.LayerIndex),
                Is.True);
        }

        private static CityBusPlan CreateCyclicPlan(
            bool includeStop = true,
            bool includeAnchor = true)
        {
            Vector3 start = new Vector3(0f, 0.08f, 80.4f);
            Vector3 cornerOne = start + (Vector3.right * 10f);
            Vector3 cornerTwo = cornerOne + (Vector3.back * 10f);
            Vector3 cornerThree = cornerTwo + (Vector3.left * 10f);
            RoadEdge edge = new RoadEdge(
                new Vector2Int(0, 0),
                new Vector2Int(0, 1));
            var samples = new List<CityBusPathSample>
            {
                new CityBusPathSample(start, Vector3.right, 0f),
                new CityBusPathSample(cornerOne, Vector3.back, 10f),
                new CityBusPathSample(cornerTwo, Vector3.left, 20f),
                new CityBusPathSample(cornerThree, Vector3.forward, 30f),
                new CityBusPathSample(start, Vector3.right, 40f)
            };
            CityBusClearanceResult clearance =
                new CityBusClearanceResult(
                    true,
                    CityBusClearanceFailureKind.None,
                    -1,
                    default,
                    CityBusDesignVehicle.Default.ClearanceMargin);
            var nodes = new List<CityBusRouteNode>
            {
                new CityBusRouteNode(
                    "node",
                    start,
                    Vector3.right,
                    edge,
                    edge.A,
                    edge.B,
                    new[] { 0 })
            };
            var links = new List<CityBusRouteLink>
            {
                new CityBusRouteLink(
                    "link",
                    0,
                    0,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    samples,
                    float.PositiveInfinity,
                    clearance)
            };
            var anchors = new List<CityBusSpawnAnchor>();
            if (includeAnchor)
            {
                anchors.Add(new CityBusSpawnAnchor(
                    "anchor",
                    0,
                    0f,
                    start,
                    Vector3.right,
                    edge));
            }
            var stops = new List<CityBusStopDescriptor>();
            if (includeStop)
            {
                stops.Add(new CityBusStopDescriptor(
                    "stop",
                    "shelter",
                    start + (Vector3.right * 5f) +
                    (Vector3.forward * 3f),
                    0,
                    5f,
                    start + (Vector3.right * 5f),
                    Vector3.right,
                    edge));
            }
            return new CityBusPlan(
                11,
                17,
                0x42555331u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                anchors,
                stops,
                new List<CityBusClearanceFailure>(),
                1,
                1);
        }

        private static CityBusPlan CreateDirectedApproachPlan(
            float anchorZ,
            bool includeStop,
            float farZ = 100.4f,
            bool markEveryLinkAsJunction = false,
            bool placeStopAtSpawnBand = false,
            float firstLinkRise = 0f)
        {
            Vector3 anchor = new Vector3(0f, 0.08f, anchorZ);
            Vector3 farNorth = new Vector3(
                0f,
                0.08f + firstLinkRise,
                farZ);
            Vector3 farEast = new Vector3(
                20f,
                0.08f + firstLinkRise,
                farZ);
            Vector3 nearEast = new Vector3(20f, 0.08f, 20f);
            Vector3 nearWest = new Vector3(0f, 0.08f, 20f);
            Vector3[] points =
            {
                anchor,
                farNorth,
                farEast,
                nearEast,
                nearWest
            };
            var nodeEdges = new RoadEdge[points.Length];
            for (int index = 0; index < nodeEdges.Length; index++)
            {
                int edgeColumn = markEveryLinkAsJunction
                    ? index
                    : index <= 1
                        ? 0
                        : index - 1;
                nodeEdges[index] = new RoadEdge(
                    new Vector2Int(edgeColumn, 0),
                    new Vector2Int(edgeColumn, 1));
            }

            RoadEdge edge = nodeEdges[0];
            CityBusClearanceResult clearance =
                new CityBusClearanceResult(
                    true,
                    CityBusClearanceFailureKind.None,
                    -1,
                    default,
                    CityBusDesignVehicle.Default.ClearanceMargin);
            var links = new List<CityBusRouteLink>(points.Length);
            var nodes = new List<CityBusRouteNode>(points.Length);
            for (int index = 0; index < points.Length; index++)
            {
                int next = (index + 1) % points.Length;
                Vector3 forward =
                    (points[next] - points[index]).normalized;
                float length = Vector3.Distance(
                    points[index],
                    points[next]);
                links.Add(new CityBusRouteLink(
                    "approach-link:" + index,
                    index,
                    next,
                    CityBusRouteLinkKind.Straight,
                    edge.B,
                    new[]
                    {
                        new CityBusPathSample(
                            points[index],
                            forward,
                            0f),
                        new CityBusPathSample(
                            points[next],
                            forward,
                            length)
                    },
                    float.PositiveInfinity,
                    clearance));
                nodes.Add(new CityBusRouteNode(
                    "approach-node:" + index,
                    points[index],
                    forward,
                    nodeEdges[index],
                    nodeEdges[index].A,
                    nodeEdges[index].B,
                    new[] { index }));
            }

            var stops = new List<CityBusStopDescriptor>();
            if (includeStop)
            {
                const int stopLinkIndex = 2;
                float stopDistance = placeStopAtSpawnBand ? 24f : 40f;
                int stopTargetIndex =
                    (stopLinkIndex + 1) % points.Length;
                Vector3 stopForward =
                    (points[stopTargetIndex] -
                     points[stopLinkIndex]).normalized;
                Vector3 stopPosition =
                    points[stopLinkIndex] +
                    (stopForward * stopDistance);
                stops.Add(new CityBusStopDescriptor(
                    "approach-stop",
                    "shelter",
                    stopPosition + (Vector3.forward * 3f),
                    stopLinkIndex,
                    stopDistance,
                    stopPosition,
                    stopForward,
                    edge));
            }

            return new CityBusPlan(
                11,
                17,
                0x42555332u,
                1.5f,
                CityBusDesignVehicle.Default,
                nodes,
                links,
                new List<CityBusSpawnAnchor>
                {
                    new CityBusSpawnAnchor(
                        "approach-anchor",
                        0,
                        0f,
                        anchor,
                        links[0].Samples[0].Forward,
                        edge)
                },
                stops,
                new List<CityBusClearanceFailure>(),
                points.Length,
                links.Count);
        }

        private static float GetForwardLoopDistance(
            CityBusPlan plan,
            int fromLinkIndex,
            float fromDistanceAlongLink,
            int toLinkIndex,
            float toDistanceAlongLink)
        {
            float from = float.NaN;
            float to = float.NaN;
            float loopDistance = 0f;
            for (int index = 0;
                 index < plan.OrderedLinkIndices.Count;
                 index++)
            {
                int linkIndex = plan.OrderedLinkIndices[index];
                if (linkIndex == fromLinkIndex)
                {
                    from = loopDistance + fromDistanceAlongLink;
                }

                if (linkIndex == toLinkIndex)
                {
                    to = loopDistance + toDistanceAlongLink;
                }

                loopDistance += plan.Links[linkIndex].Length;
            }

            Assert.That(float.IsNaN(from), Is.False);
            Assert.That(float.IsNaN(to), Is.False);
            float result = to - from;
            return result >= 0f ? result : result + plan.LoopLength;
        }

        private sealed class RuntimeFixture
        {
            private RuntimeFixture(
                GameObject root,
                Transform player,
                CityBusActor actor,
                CityBusPresentation presentation,
                CityBusDirector director)
            {
                Root = root;
                Player = player;
                Actor = actor;
                Presentation = presentation;
                Director = director;
            }

            public GameObject Root { get; }
            public Transform Player { get; }
            public CityBusActor Actor { get; }
            public CityBusPresentation Presentation { get; }
            public CityBusDirector Director { get; }

            public static RuntimeFixture Create(
                string name,
                CityBusPlan plan)
            {
                GameObject root = new GameObject(name);
                Transform player = new GameObject("Player").transform;
                player.SetParent(root.transform, false);
                Transform pool = new GameObject("Pool").transform;
                pool.SetParent(root.transform, false);

                CityBusAssetRegistry registry =
                    CreateRegistry(pool);
                CityBusPresentation presentation =
                    registry.gameObject.AddComponent<
                        CityBusPresentation>();
                presentation.Initialize(registry);
                presentation.gameObject.SetActive(false);

                GameObject actorObject = new GameObject("Actor");
                actorObject.layer = CityBusCollision.LayerIndex;
                actorObject.transform.SetParent(root.transform, false);
                CityBusActor actor =
                    actorObject.AddComponent<CityBusActor>();
                actor.Initialize(
                    registry.LocalBounds,
                    registry.Dimensions);

                CityBusDirector director =
                    root.AddComponent<CityBusDirector>();
                director.Initialize(
                    plan,
                    actor,
                    presentation,
                    player,
                    null,
                    pool,
                    () => 1f);
                return new RuntimeFixture(
                    root,
                    player,
                    actor,
                    presentation,
                    director);
            }

            public void Destroy()
            {
                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }
            }

            public void SpawnDirectly(
                uint behaviorSeed = 0x42555354u)
            {
                CityBusSpawnAnchor anchor = Plan.SpawnAnchors[0];
                Actor.PrepareSpawn(
                    Plan,
                    anchor,
                    behaviorSeed);
                Actor.BindPresentation(Presentation);
                Physics.SyncTransforms();
            }

            private CityBusPlan Plan => Director.Plan;

            private static CityBusAssetRegistry CreateRegistry(
                Transform parent)
            {
                GameObject model = new GameObject("Model");
                model.transform.SetParent(parent, false);
                CityBusAssetRegistry registry =
                    model.AddComponent<CityBusAssetRegistry>();
                Transform body = CreateChild("Body", model.transform);
                Transform frontLeftSteering = CreateChild(
                    "Front Left Steering",
                    body);
                Transform frontRightSteering = CreateChild(
                    "Front Right Steering",
                    body);
                Transform frontLeftWheel = CreateChild(
                    "Front Left Wheel",
                    frontLeftSteering);
                Transform frontRightWheel = CreateChild(
                    "Front Right Wheel",
                    frontRightSteering);
                Transform rearLeftWheel = CreateChild(
                    "Rear Left Wheel",
                    body);
                Transform rearRightWheel = CreateChild(
                    "Rear Right Wheel",
                    body);
                registry.Configure(
                    model.transform,
                    body,
                    CreateChild("Front Door Forward Leaf", body),
                    CreateChild("Front Door Rearward Leaf", body),
                    CreateChild("Rear Door Forward Leaf", body),
                    CreateChild("Rear Door Rearward Leaf", body),
                    frontLeftWheel,
                    frontRightWheel,
                    rearLeftWheel,
                    rearRightWheel,
                    frontLeftSteering,
                    frontRightSteering,
                    CreateChild("Driver", body),
                    CreateChild("Front Entry", body),
                    CreateChild("Rear Entry", body),
                    new Transform[0],
                    new Renderer[0],
                    new CityBusRendererBinding[0],
                    new Renderer[0],
                    new Renderer[0],
                    new Renderer[0],
                    new Bounds(
                        new Vector3(0f, 1.475f, 0f),
                        new Vector3(2.72f, 2.95f, 8.366f)),
                    new CityBusDimensions(
                        8.25f,
                        2.38f,
                        2.95f,
                        4.5f,
                        0.43f),
                    1,
                    "test",
                    "test",
                    "test",
                    configuredLeftWiperPivot:
                        CreateChild("Left Wiper", body),
                    configuredRightWiperPivot:
                        CreateChild("Right Wiper", body));
                return registry;
            }

            private static Transform CreateChild(
                string name,
                Transform parent)
            {
                Transform child = new GameObject(name).transform;
                child.SetParent(parent, false);
                return child;
            }
        }
    }
}
